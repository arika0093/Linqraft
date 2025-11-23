using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Linqraft.Core;
using Linqraft.Core.Formatting;
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
/// Code fix provider that converts anonymous types to DTO classes
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(AnonymousTypeToDtoCodeFixProvider))]
[Shared]
public class AnonymousTypeToDtoCodeFixProvider : CodeFixProvider
{
    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(AnonymousTypeToDtoAnalyzer.AnalyzerId);

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

        // Option 1: Add DTO to end of current file (appears first/on top)
        context.RegisterCodeFix(
            CodeAction.Create(
                title: "Convert to Class (add to current file)",
                createChangedDocument: c =>
                    ConvertToDtoInSameFileAsync(context.Document, anonymousObject, c),
                equivalenceKey: "ConvertToDtoSameFile"
            ),
            diagnostic
        );

        // Option 2: Create new file for DTO
        context.RegisterCodeFix(
            CodeAction.Create(
                title: "Convert to Class (new file)",
                createChangedSolution: c =>
                    ConvertToDtoNewFileAsync(context.Document, anonymousObject, c),
                equivalenceKey: "ConvertToDtoNewFile"
            ),
            diagnostic
        );
    }

    private async Task<Document> ConvertToDtoInSameFileAsync(
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

        // Generate DTO class name
        var dtoClassName = GenerateDtoClassName(anonymousObject);

        // Get the namespace from the document
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null)
            return document;

        var namespaceDecl = root.DescendantNodes()
            .OfType<BaseNamespaceDeclarationSyntax>()
            .FirstOrDefault();
        var namespaceName = namespaceDecl?.Name.ToString() ?? "";

        // Collect all DTO classes (main + nested)
        var allDtoClasses = new List<GenerateDtoClassInfo>();
        var nestedDtoClasses = CollectNestedDtoClasses(dtoStructure, namespaceName);
        allDtoClasses.AddRange(nestedDtoClasses);

        // Create main DTO class info
        var dtoClassInfo = new GenerateDtoClassInfo
        {
            Structure = dtoStructure,
            Accessibility = "public",
            ClassName = dtoClassName,
            Namespace = namespaceName,
            NestedClasses = [.. nestedDtoClasses],
        };
        allDtoClasses.Add(dtoClassInfo);

        // Generate configuration
        var configuration = new LinqraftConfiguration();

        // Replace anonymous object with DTO instantiation
        var newRoot = ReplaceAnonymousWithDtoSync(
            root,
            anonymousObject,
            dtoClassName,
            namespaceName,
            semanticModel
        );

        // Add all DTO classes to the file (nested first, then main)
        foreach (var dtoClass in allDtoClasses)
        {
            var dtoClassCode = dtoClass.BuildCode(configuration);

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
                    // For global namespace, we need two linefeeds (one for empty line, one for line break)
                    dtoMember = dtoMember.WithLeadingTrivia(
                        SyntaxFactory.LineFeed,
                        SyntaxFactory.LineFeed
                    );

                    newRoot = compilationUnit.AddMembers(dtoMember);
                }
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

    private async Task<Solution> ConvertToDtoNewFileAsync(
        Document document,
        AnonymousObjectCreationExpressionSyntax anonymousObject,
        CancellationToken cancellationToken
    )
    {
        var semanticModel = await document
            .GetSemanticModelAsync(cancellationToken)
            .ConfigureAwait(false);
        if (semanticModel == null)
            return document.Project.Solution;

        // Get type info for the anonymous object
        var typeInfo = semanticModel.GetTypeInfo(anonymousObject, cancellationToken);
        var anonymousType = typeInfo.Type;
        if (anonymousType == null)
            return document.Project.Solution;

        // Analyze the anonymous type structure
        var dtoStructure = DtoStructure.AnalyzeAnonymousType(
            anonymousObject,
            semanticModel,
            anonymousType
        );
        if (dtoStructure == null)
            return document.Project.Solution;

        // Generate DTO class name
        var dtoClassName = GenerateDtoClassName(anonymousObject);

        // Get the namespace from the document
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var namespaceDecl = root
            ?.DescendantNodes()
            .OfType<BaseNamespaceDeclarationSyntax>()
            .FirstOrDefault();
        var namespaceName = namespaceDecl?.Name.ToString() ?? "";

        // Collect all DTO classes (main + nested)
        var nestedDtoClasses = CollectNestedDtoClasses(dtoStructure, namespaceName);

        // Create DTO class info
        var dtoClassInfo = new GenerateDtoClassInfo
        {
            Structure = dtoStructure,
            Accessibility = "public",
            ClassName = dtoClassName,
            Namespace = namespaceName,
            NestedClasses = [.. nestedDtoClasses],
        };

        // Generate configuration
        var configuration = new LinqraftConfiguration();

        // Build DTO code
        var dtoCode = BuildDtoFile(dtoClassInfo, nestedDtoClasses, configuration);

        // Create new document for the DTO
        var project = document.Project;
        var dtoFileName = $"{dtoClassName}.cs";

        // Check if file already exists
        var existingDoc = project.Documents.FirstOrDefault(d => d.Name == dtoFileName);
        if (existingDoc != null)
        {
            // Update existing document
            project = project.RemoveDocument(existingDoc.Id);
        }

        var dtoDocument = project.AddDocument(dtoFileName, dtoCode);
        project = dtoDocument.Project;

        // Replace anonymous object with DTO instantiation
        var newRoot = await ReplaceAnonymousWithDto(
                document,
                anonymousObject,
                dtoClassName,
                namespaceName,
                cancellationToken
            )
            .ConfigureAwait(false);

        if (newRoot != null)
        {
            var updatedDocument = project.GetDocument(document.Id);
            if (updatedDocument != null)
            {
                project = updatedDocument.WithSyntaxRoot(newRoot).Project;
            }
        }

        return project.Solution;
    }

    private static SyntaxNode ReplaceAnonymousWithDtoSync(
        SyntaxNode root,
        AnonymousObjectCreationExpressionSyntax anonymousObject,
        string dtoClassName,
        string namespaceName,
        SemanticModel semanticModel
    )
    {
        // Convert anonymous object initializers to regular object initializers
        // This handles both explicit (Name = value) and implicit (x.Name) properties
        var newInitializers = new List<ExpressionSyntax>();
        
        foreach (var initializer in anonymousObject.Initializers)
        {
            ExpressionSyntax newInitializer;
            
            if (initializer.NameEquals != null)
            {
                // Already an explicit assignment: Name = value
                // Just need to replace nested anonymous objects in the value
                var replacedValue = ReplaceNestedAnonymousObjects(
                    initializer.Expression,
                    namespaceName,
                    semanticModel
                );
                
                if (replacedValue != initializer.Expression)
                {
                    // Value was replaced, create new assignment preserving trivia
                    newInitializer = SyntaxFactory.AssignmentExpression(
                        SyntaxKind.SimpleAssignmentExpression,
                        SyntaxFactory.IdentifierName(initializer.NameEquals.Name.Identifier.Text)
                            .WithLeadingTrivia(initializer.NameEquals.GetLeadingTrivia())
                            .WithTrailingTrivia(SyntaxFactory.Space),
                        SyntaxFactory.Token(SyntaxKind.EqualsToken)
                            .WithTrailingTrivia(SyntaxFactory.Space),
                        replacedValue
                    ).WithLeadingTrivia(initializer.GetLeadingTrivia())
                     .WithTrailingTrivia(initializer.GetTrailingTrivia());
                }
                else
                {
                    // No nested replacements, convert NameEquals format to assignment
                    newInitializer = SyntaxFactory.AssignmentExpression(
                        SyntaxKind.SimpleAssignmentExpression,
                        SyntaxFactory.IdentifierName(initializer.NameEquals.Name.Identifier.Text)
                            .WithLeadingTrivia(initializer.NameEquals.GetLeadingTrivia())
                            .WithTrailingTrivia(SyntaxFactory.Space),
                        SyntaxFactory.Token(SyntaxKind.EqualsToken)
                            .WithTrailingTrivia(SyntaxFactory.Space),
                        initializer.Expression
                    ).WithLeadingTrivia(initializer.GetLeadingTrivia())
                     .WithTrailingTrivia(initializer.GetTrailingTrivia());
                }
            }
            else
            {
                // Implicit property: x.Name -> Name = x.Name
                var propertyName = GetPropertyNameFromExpression(initializer.Expression);
                var replacedValue = ReplaceNestedAnonymousObjects(
                    initializer.Expression,
                    namespaceName,
                    semanticModel
                );
                
                newInitializer = SyntaxFactory.AssignmentExpression(
                    SyntaxKind.SimpleAssignmentExpression,
                    SyntaxFactory.IdentifierName(propertyName)
                        .WithTrailingTrivia(SyntaxFactory.Space),
                    SyntaxFactory.Token(SyntaxKind.EqualsToken)
                        .WithTrailingTrivia(SyntaxFactory.Space),
                    replacedValue
                ).WithLeadingTrivia(initializer.GetLeadingTrivia())
                 .WithTrailingTrivia(initializer.GetTrailingTrivia());
            }
            
            newInitializers.Add(newInitializer);
        }

        // Preserve the original separators (commas with their trivia)
        var originalSeparators = anonymousObject.Initializers.GetSeparators().ToList();
        var newSeparatedList = SyntaxFactory.SeparatedList(newInitializers, originalSeparators);

        // Create new initializer expression preserving the original braces and their trivia
        var newInitializerExpression = SyntaxFactory.InitializerExpression(
            SyntaxKind.ObjectInitializerExpression,
            anonymousObject.OpenBraceToken,
            newSeparatedList,
            anonymousObject.CloseBraceToken
        );

        // Create the object creation expression, replacing "new" with "new DtoClassName"
        var newObjectCreation = SyntaxFactory
            .ObjectCreationExpression(
                SyntaxFactory.Token(SyntaxKind.NewKeyword)
                    .WithLeadingTrivia(anonymousObject.NewKeyword.LeadingTrivia)
                    .WithTrailingTrivia(SyntaxFactory.Space),
                SyntaxFactory.IdentifierName(dtoClassName),
                null, // no argument list
                newInitializerExpression
            )
            .WithTrailingTrivia(anonymousObject.GetTrailingTrivia());

        return root.ReplaceNode(anonymousObject, newObjectCreation);
    }

    /// <summary>
    /// Recursively replaces nested anonymous objects with DTO instantiations
    /// </summary>
    private static ExpressionSyntax ReplaceNestedAnonymousObjects(
        ExpressionSyntax expression,
        string namespaceName,
        SemanticModel semanticModel
    )
    {
        // Direct anonymous object creation
        if (expression is AnonymousObjectCreationExpressionSyntax nestedAnonymous)
        {
            var typeInfo = semanticModel.GetTypeInfo(nestedAnonymous);
            var anonymousType = typeInfo.Type;
            if (anonymousType != null)
            {
                // Infer source type from member accesses (same logic as DtoProperty.AnalyzeExpression)
                ITypeSymbol? sourceType = null;
                foreach (var initializer in nestedAnonymous.Initializers)
                {
                    var initExpr = initializer.Expression;
                    if (initExpr is MemberAccessExpressionSyntax memberAccess)
                    {
                        var baseTypeInfo = semanticModel.GetTypeInfo(memberAccess.Expression);
                        if (baseTypeInfo.Type is not null)
                        {
                            sourceType = baseTypeInfo.Type;
                            break;
                        }
                    }
                }
                sourceType ??= anonymousType;

                var structure = DtoStructure.AnalyzeAnonymousType(
                    nestedAnonymous,
                    semanticModel,
                    sourceType
                );
                if (structure != null)
                {
                    var nestedClassName =
                        $"{structure.SourceTypeName}Dto_{structure.GetUniqueId()}";

                    // Convert anonymous object initializers to regular object initializers
                    // preserving original formatting
                    var newInitializers = new List<ExpressionSyntax>();
                    
                    foreach (var initializer in nestedAnonymous.Initializers)
                    {
                        ExpressionSyntax newInitializer;
                        
                        if (initializer.NameEquals != null)
                        {
                            // Already an explicit assignment: Name = value
                            var replacedValue = ReplaceNestedAnonymousObjects(
                                initializer.Expression,
                                namespaceName,
                                semanticModel
                            );
                            
                            if (replacedValue != initializer.Expression)
                            {
                                // Value was replaced, create new assignment preserving trivia
                                newInitializer = SyntaxFactory.AssignmentExpression(
                                    SyntaxKind.SimpleAssignmentExpression,
                                    SyntaxFactory.IdentifierName(initializer.NameEquals.Name.Identifier.Text)
                                        .WithLeadingTrivia(initializer.NameEquals.GetLeadingTrivia())
                                        .WithTrailingTrivia(SyntaxFactory.Space),
                                    SyntaxFactory.Token(SyntaxKind.EqualsToken)
                                        .WithTrailingTrivia(SyntaxFactory.Space),
                                    replacedValue
                                ).WithLeadingTrivia(initializer.GetLeadingTrivia())
                                 .WithTrailingTrivia(initializer.GetTrailingTrivia());
                            }
                            else
                            {
                                // No nested replacements, convert NameEquals format to assignment
                                newInitializer = SyntaxFactory.AssignmentExpression(
                                    SyntaxKind.SimpleAssignmentExpression,
                                    SyntaxFactory.IdentifierName(initializer.NameEquals.Name.Identifier.Text)
                                        .WithLeadingTrivia(initializer.NameEquals.GetLeadingTrivia())
                                        .WithTrailingTrivia(SyntaxFactory.Space),
                                    SyntaxFactory.Token(SyntaxKind.EqualsToken)
                                        .WithTrailingTrivia(SyntaxFactory.Space),
                                    initializer.Expression
                                ).WithLeadingTrivia(initializer.GetLeadingTrivia())
                                 .WithTrailingTrivia(initializer.GetTrailingTrivia());
                            }
                        }
                        else
                        {
                            // Implicit property: x.Name -> Name = x.Name
                            var propertyName = GetPropertyNameFromExpression(initializer.Expression);
                            var replacedValue = ReplaceNestedAnonymousObjects(
                                initializer.Expression,
                                namespaceName,
                                semanticModel
                            );
                            
                            newInitializer = SyntaxFactory.AssignmentExpression(
                                SyntaxKind.SimpleAssignmentExpression,
                                SyntaxFactory.IdentifierName(propertyName)
                                    .WithTrailingTrivia(SyntaxFactory.Space),
                                SyntaxFactory.Token(SyntaxKind.EqualsToken)
                                    .WithTrailingTrivia(SyntaxFactory.Space),
                                replacedValue
                            ).WithLeadingTrivia(initializer.GetLeadingTrivia())
                             .WithTrailingTrivia(initializer.GetTrailingTrivia());
                        }
                        
                        newInitializers.Add(newInitializer);
                    }

                    // Preserve the original separators (commas with their trivia)
                    var originalSeparators = nestedAnonymous.Initializers.GetSeparators().ToList();
                    var newSeparatedList = SyntaxFactory.SeparatedList(newInitializers, originalSeparators);

                    // Create new initializer expression preserving the original braces and their trivia
                    var nestedInitializerExpression = SyntaxFactory.InitializerExpression(
                        SyntaxKind.ObjectInitializerExpression,
                        nestedAnonymous.OpenBraceToken,
                        newSeparatedList,
                        nestedAnonymous.CloseBraceToken
                    );

                    // Create the object creation expression, replacing "new" with "new NestedClassName"
                    var nestedObjectCreation = SyntaxFactory
                        .ObjectCreationExpression(
                            SyntaxFactory.Token(SyntaxKind.NewKeyword)
                                .WithLeadingTrivia(nestedAnonymous.NewKeyword.LeadingTrivia)
                                .WithTrailingTrivia(SyntaxFactory.Space),
                            SyntaxFactory.IdentifierName(nestedClassName),
                            null, // no argument list
                            nestedInitializerExpression
                        )
                        .WithTrailingTrivia(nestedAnonymous.GetTrailingTrivia());

                    return nestedObjectCreation;
                }
            }
        }

        // For lambda expressions with Select, we need to replace nested anonymous objects
        if (
            expression is InvocationExpressionSyntax invocation
            && invocation.Expression is MemberAccessExpressionSyntax selectMemberAccess
            && selectMemberAccess.Name.Identifier.Text == "Select"
        )
        {
            // Get the lambda argument
            if (
                invocation.ArgumentList.Arguments.Count > 0
                && invocation.ArgumentList.Arguments[0].Expression is LambdaExpressionSyntax lambda
            )
            {
                // Check if lambda body is an anonymous object
                if (lambda.Body is AnonymousObjectCreationExpressionSyntax lambdaAnonymous)
                {
                    // Replace the lambda body
                    var replacedBody = ReplaceNestedAnonymousObjects(
                        lambdaAnonymous,
                        namespaceName,
                        semanticModel
                    );

                    // Create new lambda with replaced body
                    LambdaExpressionSyntax newLambda;
                    if (lambda is SimpleLambdaExpressionSyntax simpleLambda)
                    {
                        newLambda = simpleLambda.WithBody(replacedBody);
                    }
                    else if (lambda is ParenthesizedLambdaExpressionSyntax parenLambda)
                    {
                        newLambda = parenLambda.WithBody(replacedBody);
                    }
                    else
                    {
                        // Fallback: return expression unchanged
                        return expression;
                    }

                    // Replace the lambda in the invocation
                    var newArg = invocation.ArgumentList.Arguments[0].WithExpression(newLambda);
                    var newArgList = invocation.ArgumentList.WithArguments(
                        invocation.ArgumentList.Arguments.Replace(
                            invocation.ArgumentList.Arguments[0],
                            newArg
                        )
                    );
                    return invocation.WithArgumentList(newArgList);
                }
            }
        }

        // No replacement needed, return original expression
        return expression;
    }

    /// <summary>
    /// Recursively collects nested DTO classes from a structure
    /// </summary>
    private static List<GenerateDtoClassInfo> CollectNestedDtoClasses(
        DtoStructure structure,
        string namespaceName
    )
    {
        var result = new List<GenerateDtoClassInfo>();

        foreach (var prop in structure.Properties)
        {
            if (prop.NestedStructure is not null)
            {
                // Recursively collect nested DTOs
                var nestedClasses = CollectNestedDtoClasses(prop.NestedStructure, namespaceName);
                result.AddRange(nestedClasses);

                // Create DTO for this nested structure
                var nestedClassName =
                    $"{prop.NestedStructure.SourceTypeName}Dto_{prop.NestedStructure.GetUniqueId()}";
                var nestedDtoInfo = new GenerateDtoClassInfo
                {
                    Accessibility = "public",
                    Namespace = namespaceName,
                    ClassName = nestedClassName,
                    Structure = prop.NestedStructure,
                    NestedClasses = [.. nestedClasses],
                };
                result.Add(nestedDtoInfo);
            }
        }

        return result;
    }

    private static string GenerateDtoClassName(
        AnonymousObjectCreationExpressionSyntax anonymousObject
    )
    {
        return DtoNamingHelper.GenerateDtoClassName(anonymousObject);
    }

    private static string BuildDtoFile(
        GenerateDtoClassInfo dtoClassInfo,
        List<GenerateDtoClassInfo> nestedClasses,
        LinqraftConfiguration configuration
    )
    {
        var sb = new StringBuilder();

        // Only add namespace declaration if not in global namespace
        if (!string.IsNullOrEmpty(dtoClassInfo.Namespace))
        {
            sb.AppendLine($"namespace {dtoClassInfo.Namespace};");
            sb.AppendLine();
        }

        // Generate nested classes first
        foreach (var nestedClass in nestedClasses)
        {
            sb.AppendLine(nestedClass.BuildCode(configuration));
        }

        // Generate main class
        sb.AppendLine(dtoClassInfo.BuildCode(configuration));

        return sb.ToString();
    }

    private static async Task<SyntaxNode?> ReplaceAnonymousWithDto(
        Document document,
        AnonymousObjectCreationExpressionSyntax anonymousObject,
        string dtoClassName,
        string namespaceName,
        CancellationToken cancellationToken
    )
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null)
            return null;

        var semanticModel = await document
            .GetSemanticModelAsync(cancellationToken)
            .ConfigureAwait(false);
        if (semanticModel == null)
            return null;

        // Use the synchronous version which handles nested anonymous objects
        return ReplaceAnonymousWithDtoSync(
            root,
            anonymousObject,
            dtoClassName,
            namespaceName,
            semanticModel
        );
    }

    private static string GetPropertyNameFromExpression(ExpressionSyntax expression)
    {
        return ExpressionHelper.GetPropertyNameOrDefault(expression, "Property");
    }
}
