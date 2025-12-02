using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Linqraft.Core;
using Linqraft.Core.SyntaxHelpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Text;

namespace Linqraft.Analyzer;

/// <summary>
/// Code fix provider that converts anonymous type keys in GroupBy to named DTO classes.
/// This is required when using SelectExpr after GroupBy, as the generated code cannot
/// handle anonymous types in the input type parameter.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(GroupByAnonymousKeyCodeFixProvider))]
[Shared]
public class GroupByAnonymousKeyCodeFixProvider : CodeFixProvider
{
    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        [GroupByAnonymousKeyAnalyzer.AnalyzerId];

    public sealed override FixAllProvider GetFixAllProvider() =>
        WellKnownFixAllProviders.BatchFixer;

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context
            .Document.GetSyntaxRootAsync(context.CancellationToken)
            .ConfigureAwait(false);
        if (root == null)
            return;

        var diagnostic = context.Diagnostics.First();
        var diagnosticSpan = diagnostic.Location.SourceSpan;

        var anonymousObject = root.FindToken(diagnosticSpan.Start)
            .Parent?.AncestorsAndSelf()
            .OfType<AnonymousObjectCreationExpressionSyntax>()
            .First();

        if (anonymousObject == null)
            return;

        // Register code fix to convert anonymous type to DTO
        context.RegisterCodeFix(
            CodeAction.Create(
                title: "Convert GroupBy key to named class (same file)",
                createChangedDocument: c =>
                    ConvertGroupByKeyToDtoAsync(context.Document, anonymousObject, c),
                equivalenceKey: "ConvertGroupByKeyToDtoSameFile"
            ),
            diagnostic
        );
    }

    private async Task<Document> ConvertGroupByKeyToDtoAsync(
        Document document,
        AnonymousObjectCreationExpressionSyntax anonymousObject,
        CancellationToken cancellationToken
    )
    {
        var semanticModel = await document
            .GetSemanticModelAsync(cancellationToken)
            .ConfigureAwait(false);
        if (semanticModel == null)
            return document;

        // Get type info for the anonymous object
        var typeInfo = semanticModel.GetTypeInfo(anonymousObject, cancellationToken);
        var anonymousType = typeInfo.Type;
        if (anonymousType == null)
            return document;

        // Analyze the anonymous type structure
        var dtoStructure = DtoStructure.AnalyzeAnonymousType(
            anonymousObject,
            semanticModel,
            anonymousType
        );
        if (dtoStructure == null)
            return document;

        // Generate DTO class name based on context (GroupBy key)
        var dtoClassName = GenerateGroupByKeyClassName(anonymousObject);

        // Get the namespace from the document
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null)
            return document;

        var namespaceDecl = root.DescendantNodes()
            .OfType<BaseNamespaceDeclarationSyntax>()
            .FirstOrDefault();
        var namespaceName = namespaceDecl?.Name.ToString() ?? "";

        // Create DTO class info
        var dtoClassInfo = new GenerateDtoClassInfo
        {
            Structure = dtoStructure,
            Accessibility = "public",
            ClassName = dtoClassName,
            Namespace = namespaceName,
            NestedClasses = [],
        };

        // Generate configuration (use None for comment output since these are not SelectExpr DTOs)
        var configuration = new LinqraftConfiguration { CommentOutput = CommentOutputMode.None };

        // Replace anonymous object with DTO instantiation
        var newRoot = ReplaceAnonymousWithDtoSync(
            root,
            anonymousObject,
            dtoClassName,
            semanticModel
        );

        // Add the DTO class to the file
        var dtoClassCode = dtoClassInfo.BuildCode(configuration);

        if (namespaceDecl != null)
        {
            // Add inside the namespace - get the updated namespace from newRoot
            var dtoMember = SyntaxFactory.ParseMemberDeclaration(dtoClassCode);
            if (dtoMember != null)
            {
                // Add leading trivia (empty line before DTO class)
                dtoMember = dtoMember.WithLeadingTrivia(SyntaxFactory.LineFeed);

                var updatedNamespaceDecl = newRoot
                    .DescendantNodes()
                    .OfType<BaseNamespaceDeclarationSyntax>()
                    .First();
                var newNamespaceDecl = updatedNamespaceDecl.AddMembers(dtoMember);
                newRoot = newRoot.ReplaceNode(updatedNamespaceDecl, newNamespaceDecl);
            }
        }
        else
        {
            // Add at file level (global namespace)
            var dtoMember = SyntaxFactory.ParseMemberDeclaration(dtoClassCode);
            if (dtoMember != null && newRoot is CompilationUnitSyntax compilationUnit)
            {
                // Add leading trivia (empty line before DTO class)
                dtoMember = dtoMember.WithLeadingTrivia(
                    SyntaxFactory.LineFeed,
                    SyntaxFactory.LineFeed
                );

                newRoot = compilationUnit.AddMembers(dtoMember);
            }
        }

        var documentWithNewRoot = document.WithSyntaxRoot(newRoot);

        // Format the document to ensure proper indentation for DTO classes
        var formattedDocument = await Formatter
            .FormatAsync(documentWithNewRoot, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        // Normalize line endings
        formattedDocument = await CodeFixFormattingHelper
            .NormalizeLineEndingsOnlyAsync(formattedDocument, cancellationToken)
            .ConfigureAwait(false);

        // Remove leading and trailing empty lines from the document text
        var formattedText = await formattedDocument
            .GetTextAsync(cancellationToken)
            .ConfigureAwait(false);
        var textContent = formattedText.ToString();

        // Remove leading empty lines
        var lines = textContent.Split('\n');
        var firstNonEmptyIndex = 0;
        while (
            firstNonEmptyIndex < lines.Length
            && string.IsNullOrWhiteSpace(lines[firstNonEmptyIndex])
        )
        {
            firstNonEmptyIndex++;
        }

        // Remove trailing empty lines
        var lastNonEmptyIndex = lines.Length - 1;
        while (lastNonEmptyIndex >= 0 && string.IsNullOrWhiteSpace(lines[lastNonEmptyIndex]))
        {
            lastNonEmptyIndex--;
        }

        if (firstNonEmptyIndex > 0 || lastNonEmptyIndex < lines.Length - 1)
        {
            var trimmedLines = lines
                .Skip(firstNonEmptyIndex)
                .Take(lastNonEmptyIndex - firstNonEmptyIndex + 1);
            var trimmedText = string.Join("\n", trimmedLines);

            var encoding = formattedText.Encoding;
            formattedDocument = formattedDocument.WithText(
                encoding != null
                    ? SourceText.From(trimmedText, encoding)
                    : SourceText.From(trimmedText)
            );
        }

        return formattedDocument;
    }

    private static SyntaxNode ReplaceAnonymousWithDtoSync(
        SyntaxNode root,
        AnonymousObjectCreationExpressionSyntax anonymousObject,
        string dtoClassName,
        SemanticModel semanticModel
    )
    {
        // Use ObjectCreationHelper to convert with trivia preservation
        var newObjectCreation = ObjectCreationHelper.ConvertToNamedType(
            anonymousObject,
            dtoClassName,
            convertMemberCallback: initializer =>
            {
                if (initializer.NameEquals != null)
                {
                    return CreateAssignmentFromNameEquals(initializer);
                }
                else
                {
                    return CreateAssignmentFromImplicitProperty(initializer);
                }
            }
        );

        return root.ReplaceNode(anonymousObject, newObjectCreation);
    }

    private static string GenerateGroupByKeyClassName(
        AnonymousObjectCreationExpressionSyntax anonymousObject
    )
    {
        // Try to find context from the GroupBy method chain
        var current = anonymousObject.Parent;
        while (current != null)
        {
            // Look for method invocation to get context
            if (current is InvocationExpressionSyntax invocation)
            {
                // Check if this is a GroupBy call
                if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
                {
                    if (memberAccess.Name.Identifier.Text == "GroupBy")
                    {
                        // Try to get the source entity type name
                        var sourceExpression = memberAccess.Expression;
                        var sourceName = GetSourceTypeName(sourceExpression);
                        if (!string.IsNullOrEmpty(sourceName))
                        {
                            return $"{sourceName}GroupKey";
                        }
                    }
                }
            }

            current = current.Parent;
        }

        // Fallback to generic name based on property names
        var propertyNames = anonymousObject.Initializers
            .Select(i => GetPropertyName(i))
            .Where(n => !string.IsNullOrEmpty(n))
            .Take(2)
            .ToList();

        if (propertyNames.Count > 0)
        {
            return $"{string.Join("", propertyNames)}GroupKey";
        }

        return "GroupKey";
    }

    private static string? GetSourceTypeName(ExpressionSyntax expression)
    {
        // Try to extract type name from the expression
        switch (expression)
        {
            case IdentifierNameSyntax identifier:
                // e.g., entities.GroupBy(...)
                var name = identifier.Identifier.Text;
                // Try to convert plural to singular (simple heuristic)
                string result;
                if (name.EndsWith("ies", System.StringComparison.Ordinal))
                {
                    result = name.Substring(0, name.Length - 3) + "y";
                }
                else if (name.EndsWith("s", System.StringComparison.Ordinal) && name.Length > 1)
                {
                    result = name.Substring(0, name.Length - 1);
                }
                else
                {
                    result = name;
                }
                // Capitalize the first letter
                return CapitalizeFirstLetter(result);

            case MemberAccessExpressionSyntax memberAccess:
                // e.g., dbContext.Entities.GroupBy(...)
                return GetSourceTypeName(memberAccess.Name);

            case InvocationExpressionSyntax invocation:
                // e.g., entities.Where(...).GroupBy(...)
                if (invocation.Expression is MemberAccessExpressionSyntax invocationMemberAccess)
                {
                    return GetSourceTypeName(invocationMemberAccess.Expression);
                }
                return null;

            default:
                return null;
        }
    }

    private static string CapitalizeFirstLetter(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }
        if (char.IsUpper(text[0]))
        {
            return text;
        }
        return char.ToUpperInvariant(text[0]) + text.Substring(1);
    }

    private static string GetPropertyName(AnonymousObjectMemberDeclaratorSyntax initializer)
    {
        if (initializer.NameEquals != null)
        {
            return initializer.NameEquals.Name.Identifier.Text;
        }

        return ExpressionHelper.GetPropertyNameOrDefault(initializer.Expression, "");
    }

    private static ExpressionSyntax CreateAssignmentFromNameEquals(
        AnonymousObjectMemberDeclaratorSyntax initializer
    )
    {
        var propertyName = initializer.NameEquals!.Name.Identifier.Text;

        // Create assignment expression
        var assignment = SyntaxFactory.AssignmentExpression(
            SyntaxKind.SimpleAssignmentExpression,
            SyntaxFactory.IdentifierName(propertyName).WithTrailingTrivia(SyntaxFactory.Space),
            SyntaxFactory.Token(SyntaxKind.EqualsToken).WithTrailingTrivia(SyntaxFactory.Space),
            initializer.Expression
        );

        // Apply trivia from the original initializer
        return assignment
            .WithLeadingTrivia(initializer.GetLeadingTrivia())
            .WithTrailingTrivia(initializer.GetTrailingTrivia());
    }

    private static ExpressionSyntax CreateAssignmentFromImplicitProperty(
        AnonymousObjectMemberDeclaratorSyntax initializer
    )
    {
        var propertyName = ExpressionHelper.GetPropertyNameOrDefault(
            initializer.Expression,
            "Property"
        );

        // Create assignment expression
        var assignment = SyntaxFactory.AssignmentExpression(
            SyntaxKind.SimpleAssignmentExpression,
            SyntaxFactory.IdentifierName(propertyName).WithTrailingTrivia(SyntaxFactory.Space),
            SyntaxFactory.Token(SyntaxKind.EqualsToken).WithTrailingTrivia(SyntaxFactory.Space),
            initializer.Expression
        );

        // Apply trivia from the original initializer
        return assignment
            .WithLeadingTrivia(initializer.GetLeadingTrivia())
            .WithTrailingTrivia(initializer.GetTrailingTrivia());
    }
}
