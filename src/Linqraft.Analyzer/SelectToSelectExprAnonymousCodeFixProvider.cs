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
/// Code fix provider that converts IQueryable.Select to SelectExpr
/// </summary>
[ExportCodeFixProvider(
    LanguageNames.CSharp,
    Name = nameof(SelectToSelectExprAnonymousCodeFixProvider)
)]
[Shared]
public class SelectToSelectExprAnonymousCodeFixProvider : CodeFixProvider
{
    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        [SelectToSelectExprAnonymousAnalyzer.AnalyzerId];

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

        // Register two code fixes: anonymous pattern and explicit DTO pattern
        context.RegisterCodeFix(
            CodeAction.Create(
                title: "Convert to SelectExpr (anonymous)",
                createChangedDocument: c =>
                    ConvertToSelectExprAnonymousAsync(context.Document, invocation, c),
                equivalenceKey: "ConvertToSelectExprAnonymous"
            ),
            diagnostic
        );

        context.RegisterCodeFix(
            CodeAction.Create(
                title: "Convert to SelectExpr<T, TDto> (explicit DTO)",
                createChangedDocument: c =>
                    ConvertToSelectExprExplicitDtoAsync(context.Document, invocation, c),
                equivalenceKey: "ConvertToSelectExprExplicitDto"
            ),
            diagnostic
        );
    }

    private static async Task<Document> ConvertToSelectExprAnonymousAsync(
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

    private static async Task<Document> ConvertToSelectExprExplicitDtoAsync(
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

        // Find the anonymous type in arguments
        var anonymousType = FindAnonymousTypeInArguments(invocation.ArgumentList);
        if (anonymousType == null)
            return document;

        // Generate DTO name
        var dtoName = GenerateDtoName(invocation, anonymousType);

        // Create the new generic SelectExpr call with type arguments
        var newExpression = CreateTypedSelectExpr(
            invocation.Expression,
            sourceType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
            dtoName
        );
        if (newExpression == null)
            return document;

        // Replace the expression and simplify ternary null checks
        var newInvocation = TernaryNullCheckSimplifier.SimplifyTernaryNullChecksInInvocation(
            invocation.WithExpression(newExpression)
        );

        // Add capture parameter if needed
        if (variablesToCapture.Count > 0)
        {
            newInvocation = AddCaptureArgument(newInvocation, variablesToCapture);
        }

        var newRoot = root.ReplaceNode(invocation, newInvocation);

        // Add using directive for source type if needed
        newRoot = UsingDirectiveHelper.AddUsingDirectiveForType(newRoot, sourceType);

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

    private static AnonymousObjectCreationExpressionSyntax? FindAnonymousTypeInArguments(
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

            if (lambda is AnonymousObjectCreationExpressionSyntax anonymousObject)
            {
                return anonymousObject;
            }
        }

        return null;
    }

    private static string GenerateDtoName(
        InvocationExpressionSyntax invocation,
        AnonymousObjectCreationExpressionSyntax anonymousType
    )
    {
        return DtoNamingHelper.GenerateDtoName(invocation, anonymousType);
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
            return [];

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
            var newArgumentList = ArgumentListHelper.ReplaceArgument(
                invocation.ArgumentList,
                existingCaptureArgIndex,
                captureArgument
            );
            return invocation.WithArgumentList(newArgumentList);
        }
        else
        {
            // Add new capture argument using the helper that preserves trivia
            var newArgumentList = ArgumentListHelper.AddArgument(
                invocation.ArgumentList,
                captureArgument
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
