using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Linqraft.Core;
using Linqraft.Core.AnalyzerHelpers;
using Linqraft.Core.SyntaxHelpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Linqraft.Analyzer;

/// <summary>
/// Code fix provider that converts IQueryable.Select with named types to SelectExpr
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(SelectToSelectExprNamedCodeFixProvider))]
[Shared]
public class SelectToSelectExprNamedCodeFixProvider : CodeFixProvider
{
    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        [SelectToSelectExprNamedAnalyzer.AnalyzerId];

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

        var invocation = root.FindToken(diagnosticSpan.Start)
            .Parent?.AncestorsAndSelf()
            .OfType<InvocationExpressionSyntax>()
            .First();

        if (invocation == null)
            return;

        // Register three code fixes
        context.RegisterCodeFix(
            CodeAction.Create(
                title: "Convert to SelectExpr<T, TDto> (convert all to anonymous)",
                createChangedDocument: c =>
                    ConvertToSelectExprExplicitDtoAllAsync(context.Document, invocation, c),
                equivalenceKey: "ConvertToSelectExprExplicitDtoAll"
            ),
            diagnostic
        );

        context.RegisterCodeFix(
            CodeAction.Create(
                title: "Convert to SelectExpr<T, TDto> (convert root only to anonymous)",
                createChangedDocument: c =>
                    ConvertToSelectExprExplicitDtoRootOnlyAsync(context.Document, invocation, c),
                equivalenceKey: "ConvertToSelectExprExplicitDtoRootOnly"
            ),
            diagnostic
        );

        context.RegisterCodeFix(
            CodeAction.Create(
                title: "Convert to SelectExpr (use predefined classes)",
                createChangedDocument: c =>
                    ConvertToSelectExprPredefinedDtoAsync(context.Document, invocation, c),
                equivalenceKey: "ConvertToSelectExprPredefinedDto"
            ),
            diagnostic
        );
    }

    private static async Task<Document> ConvertToSelectExprExplicitDtoAllAsync(
        Document document,
        InvocationExpressionSyntax invocation,
        CancellationToken cancellationToken
    )
    {
        var semanticModel = await document
            .GetSemanticModelAsync(cancellationToken)
            .ConfigureAwait(false);
        if (semanticModel == null)
            return document;

        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null)
            return document;

        // Find variables that need to be captured BEFORE modifying the syntax tree
        var variablesToCapture = FindVariablesToCapture(invocation, semanticModel);

        // Get the source type
        var sourceType = GetSourceType(invocation.Expression, semanticModel, cancellationToken);
        if (sourceType == null)
            return document;

        // Find the object creation in arguments
        var objectCreation = FindNamedObjectCreationInArguments(invocation.ArgumentList);
        if (objectCreation == null)
            return document;

        // Generate DTO name from the object creation
        var dtoName = GenerateDtoName(invocation, objectCreation);

        // Create the new generic SelectExpr call with type arguments
        var newExpression = CreateTypedSelectExpr(
            invocation.Expression,
            sourceType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
            dtoName
        );
        if (newExpression == null)
            return document;

        // Convert the named object creation to anonymous type (including nested)
        var anonymousCreation = ConvertToAnonymousTypeRecursive(objectCreation);

        // Simplify ternary null checks in the anonymous creation
        anonymousCreation = (AnonymousObjectCreationExpressionSyntax)
            TernaryNullCheckSimplifier.SimplifyTernaryNullChecks(anonymousCreation);

        // Replace both nodes in one operation using a dictionary
        var replacements = new Dictionary<SyntaxNode, SyntaxNode>
        {
            { invocation.Expression, newExpression },
            { objectCreation, anonymousCreation },
        };

        var newRoot = root.ReplaceNodes(replacements.Keys, (oldNode, _) => replacements[oldNode]);

        // Find the updated invocation in the new tree
        var newInvocation = newRoot
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .FirstOrDefault(inv =>
                inv.Expression.ToString().Contains("SelectExpr")
                && inv.Span.Start == invocation.Span.Start
            );

        // Add capture parameter if needed
        if (newInvocation != null && variablesToCapture.Count > 0)
        {
            var updatedInvocation = AddCaptureArgument(newInvocation, variablesToCapture);
            newRoot = newRoot.ReplaceNode(newInvocation, updatedInvocation);
        }

        // Add using directive for source type if needed
        newRoot = UsingDirectiveHelper.AddUsingDirectiveForType(newRoot, sourceType);

        var documentWithNewRoot = document.WithSyntaxRoot(newRoot);

        // Format and normalize line endings
        return await CodeFixFormattingHelper
            .FormatAndNormalizeLineEndingsAsync(documentWithNewRoot, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<Document> ConvertToSelectExprExplicitDtoRootOnlyAsync(
        Document document,
        InvocationExpressionSyntax invocation,
        CancellationToken cancellationToken
    )
    {
        var semanticModel = await document
            .GetSemanticModelAsync(cancellationToken)
            .ConfigureAwait(false);
        if (semanticModel == null)
            return document;

        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null)
            return document;

        // Find variables that need to be captured BEFORE modifying the syntax tree
        var variablesToCapture = FindVariablesToCapture(invocation, semanticModel);

        // Get the source type
        var sourceType = GetSourceType(invocation.Expression, semanticModel, cancellationToken);
        if (sourceType == null)
            return document;

        // Find the object creation in arguments
        var objectCreation = FindNamedObjectCreationInArguments(invocation.ArgumentList);
        if (objectCreation == null)
            return document;

        // Generate DTO name from the object creation
        var dtoName = GenerateDtoName(invocation, objectCreation);

        // Create the new generic SelectExpr call with type arguments
        var newExpression = CreateTypedSelectExpr(
            invocation.Expression,
            sourceType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
            dtoName
        );
        if (newExpression == null)
            return document;

        // Convert the named object creation to anonymous type (root only)
        var anonymousCreation = ConvertToAnonymousType(objectCreation, semanticModel);

        // Simplify ternary null checks in the anonymous creation
        anonymousCreation = (AnonymousObjectCreationExpressionSyntax)
            TernaryNullCheckSimplifier.SimplifyTernaryNullChecks(anonymousCreation);

        // Replace both nodes in one operation using a dictionary
        var replacements = new Dictionary<SyntaxNode, SyntaxNode>
        {
            { invocation.Expression, newExpression },
            { objectCreation, anonymousCreation },
        };

        var newRoot = root.ReplaceNodes(replacements.Keys, (oldNode, _) => replacements[oldNode]);

        // Find the updated invocation in the new tree
        var newInvocation = newRoot
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .FirstOrDefault(inv =>
                inv.Expression.ToString().Contains("SelectExpr")
                && inv.Span.Start == invocation.Span.Start
            );

        // Add capture parameter if needed
        if (newInvocation != null && variablesToCapture.Count > 0)
        {
            var updatedInvocation = AddCaptureArgument(newInvocation, variablesToCapture);
            newRoot = newRoot.ReplaceNode(newInvocation, updatedInvocation);
        }

        // Add using directive for source type if needed
        newRoot = UsingDirectiveHelper.AddUsingDirectiveForType(newRoot, sourceType);

        var documentWithNewRoot = document.WithSyntaxRoot(newRoot);

        // Format and normalize line endings
        return await CodeFixFormattingHelper
            .FormatAndNormalizeLineEndingsAsync(documentWithNewRoot, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<Document> ConvertToSelectExprPredefinedDtoAsync(
        Document document,
        InvocationExpressionSyntax invocation,
        CancellationToken cancellationToken
    )
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null)
            return document;

        var semanticModel = await document
            .GetSemanticModelAsync(cancellationToken)
            .ConfigureAwait(false);
        if (semanticModel == null)
            return document;

        // Find variables that need to be captured BEFORE modifying the syntax tree
        var variablesToCapture = FindVariablesToCapture(invocation, semanticModel);

        // Replace "Select" with "SelectExpr"
        var newExpression = ReplaceMethodName(invocation.Expression, "SelectExpr");
        if (newExpression == null)
            return document;

        // Simplify ternary null checks in the lambda body
        var newInvocation = TernaryNullCheckSimplifier.SimplifyTernaryNullChecksInInvocation(
            invocation.WithExpression(newExpression)
        );

        // Add capture parameter if needed
        if (variablesToCapture.Count > 0)
        {
            newInvocation = AddCaptureArgument(newInvocation, variablesToCapture);
        }

        var newRoot = root.ReplaceNode(invocation, newInvocation);
        var documentWithNewRoot = document.WithSyntaxRoot(newRoot);

        // Format and normalize line endings
        return await CodeFixFormattingHelper
            .FormatAndNormalizeLineEndingsAsync(documentWithNewRoot, cancellationToken)
            .ConfigureAwait(false);
    }

    private static ExpressionSyntax? ReplaceMethodName(
        ExpressionSyntax expression,
        string newMethodName
    )
    {
        if (expression is not MemberAccessExpressionSyntax memberAccess)
            return null;

        // Create the new identifier
        var newName = SyntaxFactory.IdentifierName(newMethodName);

        // Create the new member access expression
        return SyntaxFactory.MemberAccessExpression(
            SyntaxKind.SimpleMemberAccessExpression,
            memberAccess.Expression,
            memberAccess.OperatorToken,
            newName
        );
    }

    private static ExpressionSyntax? CreateTypedSelectExpr(
        ExpressionSyntax expression,
        string sourceTypeName,
        string dtoName
    )
    {
        if (expression is not MemberAccessExpressionSyntax memberAccess)
            return null;

        // Create type arguments
        var typeArguments = SyntaxFactory.TypeArgumentList(
            SyntaxFactory.SeparatedList<TypeSyntax>(
                new[]
                {
                    SyntaxFactory.ParseTypeName(sourceTypeName),
                    SyntaxFactory.ParseTypeName(dtoName),
                }
            )
        );

        // Create the generic name
        var genericName = SyntaxFactory.GenericName(
            SyntaxFactory.Identifier("SelectExpr"),
            typeArguments
        );

        // Create the new member access expression, preserving the original dot token's trivia
        return SyntaxFactory.MemberAccessExpression(
            SyntaxKind.SimpleMemberAccessExpression,
            memberAccess.Expression,
            memberAccess.OperatorToken,
            genericName
        );
    }

    private static ITypeSymbol? GetSourceType(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken
    )
    {
        if (expression is not MemberAccessExpressionSyntax memberAccess)
            return null;

        var typeInfo = semanticModel.GetTypeInfo(memberAccess.Expression, cancellationToken);
        var type = typeInfo.Type;

        if (type == null)
            return null;

        // Extract the element type from IQueryable<T>
        if (type is INamedTypeSymbol namedType && namedType.IsGenericType)
        {
            var typeArguments = namedType.TypeArguments;
            if (typeArguments.Length > 0)
            {
                return typeArguments[0];
            }
        }

        return null;
    }

    private static AnonymousObjectCreationExpressionSyntax ConvertToAnonymousType(
        ObjectCreationExpressionSyntax objectCreation
    )
    {
        return ObjectCreationHelper.ConvertToAnonymousType(objectCreation);
    }

    private static AnonymousObjectCreationExpressionSyntax ConvertToAnonymousType(
        ObjectCreationExpressionSyntax objectCreation,
        SemanticModel? semanticModel
    )
    {
        // Use the helper method - semantic model not needed for basic conversion
        return ObjectCreationHelper.ConvertToAnonymousType(objectCreation);
    }

    private static AnonymousObjectCreationExpressionSyntax ConvertToAnonymousTypeRecursive(
        ObjectCreationExpressionSyntax objectCreation
    )
    {
        return ObjectCreationHelper.ConvertToAnonymousTypeRecursive(objectCreation);
    }

    /// <summary>
    /// Processes an expression to recursively convert nested object creations to anonymous objects
    /// </summary>
    private static ExpressionSyntax ProcessExpressionForNestedConversions(
        ExpressionSyntax expression
    )
    {
        // Use a recursive rewriter to find and convert all nested object creations
        var rewriter = new NestedObjectCreationRewriter();
        return (ExpressionSyntax)rewriter.Visit(expression);
    }

    /// <summary>
    /// Syntax rewriter that converts ObjectCreationExpressionSyntax to AnonymousObjectCreationExpressionSyntax
    /// </summary>
    private class NestedObjectCreationRewriter : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitObjectCreationExpression(
            ObjectCreationExpressionSyntax node
        )
        {
            // Only convert if it has an initializer with assignment expressions
            // Don't convert things like "new List()" or "new List<T>()"
            if (node.Initializer != null && HasAssignmentExpressions(node.Initializer))
            {
                // Convert this object creation to anonymous type using the helper
                var anonymousType = ObjectCreationHelper.ConvertToAnonymousType(node);

                // Continue visiting children to convert nested object creations
                return base.Visit(anonymousType) ?? anonymousType;
            }

            // For object creations without proper initializers, don't convert but still visit children
            return base.VisitObjectCreationExpression(node);
        }

        private static bool HasAssignmentExpressions(InitializerExpressionSyntax initializer)
        {
            return initializer.Expressions.Any(e => e is AssignmentExpressionSyntax);
        }
    }

    private static ObjectCreationExpressionSyntax? FindNamedObjectCreationInArguments(
        ArgumentListSyntax argumentList
    )
    {
        foreach (var argument in argumentList.Arguments)
        {
            var lambda = argument.Expression switch
            {
                SimpleLambdaExpressionSyntax simple => simple.Body,
                ParenthesizedLambdaExpressionSyntax paren => paren.Body,
                _ => null,
            };

            if (lambda is ObjectCreationExpressionSyntax objectCreation)
            {
                return objectCreation;
            }
        }

        return null;
    }

    private static string GenerateDtoName(
        InvocationExpressionSyntax invocation,
        ObjectCreationExpressionSyntax objectCreation
    )
    {
        // For explicit DTO pattern, generate a DTO name similar to anonymous types
        // We'll use the existing naming logic but pass the initializer as if it were anonymous

        // If there's an initializer, convert it to represent the properties
        if (objectCreation.Initializer != null)
        {
            // Create a synthetic anonymous object for naming purposes
            var anonymousSyntax = SyntaxFactory.AnonymousObjectCreationExpression(
                SyntaxFactory.SeparatedList(
                    objectCreation
                        .Initializer.Expressions.OfType<AssignmentExpressionSyntax>()
                        .Where(assignment => assignment.Left is IdentifierNameSyntax)
                        .Select(assignment =>
                            SyntaxFactory.AnonymousObjectMemberDeclarator(
                                SyntaxFactory.NameEquals(
                                    ((IdentifierNameSyntax)assignment.Left).Identifier.Text
                                ),
                                assignment.Right
                            )
                        )
                )
            );

            return DtoNamingHelper.GenerateDtoName(invocation, anonymousSyntax);
        }

        // Fallback to a default name
        return "ResultDto";
    }

    /// <summary>
    /// Finds variables that need to be captured in the invocation's lambda expression.
    /// </summary>
    private static HashSet<string> FindVariablesToCapture(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel
    )
    {
        // Find the lambda expression
        var lambda = FindLambdaExpression(invocation.ArgumentList);
        if (lambda == null)
            return new HashSet<string>();

        // Get lambda parameter names
        var lambdaParameters = LambdaHelper.GetLambdaParameterNames(lambda);

        // Find variables that need to be captured
        var variablesToCapture = CaptureHelper.FindSimpleVariablesToCapture(
            lambda,
            lambdaParameters,
            semanticModel
        );

        // Get already captured variables (if any)
        var capturedVariables = CaptureHelper.GetCapturedVariables(invocation, semanticModel);

        // Add any new variables to the set
        capturedVariables.UnionWith(variablesToCapture);

        return capturedVariables;
    }

    /// <summary>
    /// Adds a capture argument to the invocation with the specified variables.
    /// </summary>
    private static InvocationExpressionSyntax AddCaptureArgument(
        InvocationExpressionSyntax invocation,
        HashSet<string> variablesToCapture
    )
    {
        // Create capture argument
        var captureArgument = CaptureHelper.CreateCaptureArgument(variablesToCapture);

        // Update or add capture argument
        var existingCaptureArgIndex = FindCaptureArgumentIndex(invocation);
        if (existingCaptureArgIndex >= 0)
        {
            // Replace existing capture argument
            var arguments = invocation.ArgumentList.Arguments.ToList();
            arguments[existingCaptureArgIndex] = captureArgument;
            var newArgumentList = SyntaxFactory.ArgumentList(
                SyntaxFactory.SeparatedList(arguments)
            );
            return invocation.WithArgumentList(newArgumentList);
        }
        else
        {
            // Add new capture argument
            var arguments = invocation.ArgumentList.Arguments.ToList();
            arguments.Add(captureArgument);
            var newArgumentList = SyntaxFactory.ArgumentList(
                SyntaxFactory.SeparatedList(arguments)
            );
            return invocation.WithArgumentList(newArgumentList);
        }
    }

    private static int FindCaptureArgumentIndex(InvocationExpressionSyntax invocation)
    {
        for (int i = 0; i < invocation.ArgumentList.Arguments.Count; i++)
        {
            var argument = invocation.ArgumentList.Arguments[i];
            if (argument.NameColon?.Name.Identifier.Text == "capture")
            {
                return i;
            }
        }

        return -1;
    }

    private static LambdaExpressionSyntax? FindLambdaExpression(ArgumentListSyntax argumentList)
    {
        foreach (var argument in argumentList.Arguments)
        {
            if (argument.Expression is LambdaExpressionSyntax lambda)
            {
                return lambda;
            }
        }

        return null;
    }
}
