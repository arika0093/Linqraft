using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Linqraft.Core;
using Linqraft.Core.SyntaxHelpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Text;

namespace Linqraft.Analyzer;

/// <summary>
/// Shared helper class for converting anonymous types to DTO classes.
/// Used by both <see cref="AnonymousTypeToDtoCodeFixProvider"/> and <see cref="GroupByAnonymousKeyCodeFixProvider"/>.
/// </summary>
internal static class AnonymousToDtoCodeFixHelper
{
    /// <summary>
    /// Converts an anonymous type to a DTO class and adds it to the same file.
    /// </summary>
    /// <param name="document">The document to modify</param>
    /// <param name="anonymousObject">The anonymous object to convert</param>
    /// <param name="dtoClassName">The class name for the DTO</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The modified document</returns>
    public static async Task<Document> ConvertToDtoInSameFileAsync(
        Document document,
        AnonymousObjectCreationExpressionSyntax anonymousObject,
        string dtoClassName,
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
        formattedDocument = await TrimEmptyLinesAsync(formattedDocument, cancellationToken)
            .ConfigureAwait(false);

        return formattedDocument;
    }

    /// <summary>
    /// Replaces an anonymous object creation expression with a named DTO type instantiation.
    /// </summary>
    public static SyntaxNode ReplaceAnonymousWithDtoSync(
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

    /// <summary>
    /// Creates an assignment expression from an anonymous object initializer with explicit name.
    /// </summary>
    public static ExpressionSyntax CreateAssignmentFromNameEquals(
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

    /// <summary>
    /// Creates an assignment expression from an anonymous object initializer with implicit name.
    /// </summary>
    public static ExpressionSyntax CreateAssignmentFromImplicitProperty(
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

    /// <summary>
    /// Removes leading and trailing empty lines from the document text.
    /// </summary>
    public static async Task<Document> TrimEmptyLinesAsync(
        Document document,
        CancellationToken cancellationToken
    )
    {
        var text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
        var textContent = text.ToString();

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

            var encoding = text.Encoding;
            return document.WithText(
                encoding != null
                    ? SourceText.From(trimmedText, encoding)
                    : SourceText.From(trimmedText)
            );
        }

        return document;
    }
}
