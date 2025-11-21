using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Linqraft.Core;
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
        ImmutableArray.Create(SelectToSelectExprAnonymousAnalyzer.DiagnosticId);

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

        // Replace "Select" with "SelectExpr"
        var newExpression = ReplaceMethodName(invocation.Expression, "SelectExpr");
        if (newExpression == null)
            return document;

        // Simplify ternary null checks in the lambda body
        var newInvocation = SimplifyTernaryNullChecksInInvocation(
            invocation.WithExpression(newExpression)
        );

        var newRoot = root.ReplaceNode(invocation, newInvocation);

        return document.WithSyntaxRoot(newRoot);
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
        var newInvocation = SimplifyTernaryNullChecksInInvocation(
            invocation.WithExpression(newExpression)
        );

        var newRoot = root.ReplaceNode(invocation, newInvocation);

        // Add using directive for source type if needed
        newRoot = AddUsingDirectiveForType(newRoot, sourceType);

        return document.WithSyntaxRoot(newRoot);
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

    private static InvocationExpressionSyntax SimplifyTernaryNullChecksInInvocation(
        InvocationExpressionSyntax invocation
    )
    {
        // Find and simplify ternary null checks in lambda body
        var newArguments = new List<ArgumentSyntax>();
        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            if (
                argument.Expression is SimpleLambdaExpressionSyntax simpleLambda
                && simpleLambda.Body is ExpressionSyntax bodyExpr
            )
            {
                var simplifiedBody = TernaryNullCheckSimplifier.SimplifyTernaryNullChecks(bodyExpr);
                var newLambda = simpleLambda.WithBody(simplifiedBody);
                newArguments.Add(argument.WithExpression(newLambda));
            }
            else if (
                argument.Expression is ParenthesizedLambdaExpressionSyntax parenLambda
                && parenLambda.Body is ExpressionSyntax parenBodyExpr
            )
            {
                var simplifiedBody = TernaryNullCheckSimplifier.SimplifyTernaryNullChecks(
                    parenBodyExpr
                );
                var newLambda = parenLambda.WithBody(simplifiedBody);
                newArguments.Add(argument.WithExpression(newLambda));
            }
            else
            {
                newArguments.Add(argument);
            }
        }

        return invocation.WithArgumentList(
            SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(newArguments))
        );
    }

    private static string GenerateDtoName(
        InvocationExpressionSyntax invocation,
        AnonymousObjectCreationExpressionSyntax anonymousType
    )
    {
        return DtoNamingHelper.GenerateDtoName(invocation, anonymousType);
    }

    private static SyntaxNode AddUsingDirectiveForType(SyntaxNode root, ITypeSymbol typeSymbol)
    {
        if (root is not CompilationUnitSyntax compilationUnit)
            return root;

        // Get the namespace of the type
        var namespaceName = typeSymbol.ContainingNamespace?.ToDisplayString();
        if (string.IsNullOrEmpty(namespaceName) || namespaceName == "<global namespace>")
            return root;

        // Check if using directive already exists
        var hasUsing = compilationUnit.Usings.Any(u => u.Name?.ToString() == namespaceName);
        if (hasUsing)
            return root;

        // Detect the line ending used in the file by looking at existing using directives
        var endOfLineTrivia = compilationUnit.Usings.Any()
            ? compilationUnit.Usings.Last().GetTrailingTrivia().LastOrDefault()
            : SyntaxFactory.EndOfLine("\n");

        // If the detected trivia is not an end of line, use a default
        if (endOfLineTrivia.Kind() != SyntaxKind.EndOfLineTrivia)
        {
            endOfLineTrivia = SyntaxFactory.EndOfLine("\n");
        }

        // Add using directive (namespaceName is guaranteed non-null here due to the check above)
        var usingDirective = SyntaxFactory
            .UsingDirective(SyntaxFactory.ParseName(namespaceName!))
            .WithTrailingTrivia(endOfLineTrivia);

        return compilationUnit.AddUsings(usingDirective);
    }
}
