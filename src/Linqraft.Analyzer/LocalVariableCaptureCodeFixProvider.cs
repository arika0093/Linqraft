using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Linqraft.Analyzer;

/// <summary>
/// Code fix provider that adds capture parameter to SelectExpr for local variables
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(LocalVariableCaptureCodeFixProvider))]
[Shared]
public class LocalVariableCaptureCodeFixProvider : CodeFixProvider
{
    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(LocalVariableCaptureAnalyzer.DiagnosticId);

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

        // Find the invocation expression containing the SelectExpr call
        var invocation = root.FindToken(diagnosticSpan.Start)
            .Parent?.AncestorsAndSelf()
            .OfType<InvocationExpressionSyntax>()
            .FirstOrDefault(inv => IsSelectExprCall(inv.Expression));

        if (invocation == null)
            return;

        context.RegisterCodeFix(
            CodeAction.Create(
                title: "Add capture parameter",
                createChangedDocument: c =>
                    AddCaptureParameterAsync(context.Document, invocation, c),
                equivalenceKey: "AddCaptureParameter"
            ),
            diagnostic
        );
    }

    private async Task<Document> AddCaptureParameterAsync(
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

        // Find the lambda expression
        var lambda = FindLambdaExpression(invocation.ArgumentList);
        if (lambda == null)
            return document;

        // Get lambda parameter names
        var lambdaParameters = GetLambdaParameterNames(lambda);

        // Find all local variables used in the lambda
        var localVariables = FindLocalVariables(lambda, lambdaParameters, semanticModel);

        if (localVariables.Count == 0)
            return document;

        // Create capture argument with anonymous object containing all local variables
        var captureProperties = localVariables
            .Select(name =>
                SyntaxFactory.AnonymousObjectMemberDeclarator(
                    SyntaxFactory.IdentifierName(name)
                )
            )
            .ToArray();

        var captureObject = SyntaxFactory.AnonymousObjectCreationExpression(
            SyntaxFactory.SeparatedList(captureProperties)
        );

        // Create named argument for capture
        var captureArgument = SyntaxFactory.Argument(
            SyntaxFactory.NameColon(SyntaxFactory.IdentifierName("capture")),
            default,
            captureObject
        );

        // Add the capture argument to the invocation
        var newArgumentList = invocation.ArgumentList.AddArguments(captureArgument);
        var newInvocation = invocation.WithArgumentList(newArgumentList);

        // Replace the old invocation with the new one
        var newRoot = root.ReplaceNode(invocation, newInvocation);

        return document.WithSyntaxRoot(newRoot);
    }

    private static bool IsSelectExprCall(ExpressionSyntax expression)
    {
        return expression switch
        {
            MemberAccessExpressionSyntax memberAccess =>
                memberAccess.Name.Identifier.Text == "SelectExpr",
            IdentifierNameSyntax identifier => identifier.Identifier.Text == "SelectExpr",
            _ => false
        };
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

    private static ImmutableHashSet<string> GetLambdaParameterNames(LambdaExpressionSyntax lambda)
    {
        return lambda switch
        {
            SimpleLambdaExpressionSyntax simple =>
                ImmutableHashSet.Create(simple.Parameter.Identifier.Text),
            ParenthesizedLambdaExpressionSyntax paren =>
                paren.ParameterList.Parameters.Select(p => p.Identifier.Text).ToImmutableHashSet(),
            _ => ImmutableHashSet<string>.Empty
        };
    }

    private static HashSet<string> FindLocalVariables(
        LambdaExpressionSyntax lambda,
        ImmutableHashSet<string> lambdaParameters,
        SemanticModel semanticModel
    )
    {
        var localVariables = new HashSet<string>();
        var bodyExpression = lambda switch
        {
            SimpleLambdaExpressionSyntax simple => simple.Body,
            ParenthesizedLambdaExpressionSyntax paren => paren.Body,
            _ => null
        };

        if (bodyExpression == null)
        {
            return localVariables;
        }

        // Find all identifier references in the lambda body
        var identifiers = bodyExpression.DescendantNodes().OfType<IdentifierNameSyntax>();

        foreach (var identifier in identifiers)
        {
            var identifierName = identifier.Identifier.Text;

            // Skip if it's a lambda parameter
            if (lambdaParameters.Contains(identifierName))
            {
                continue;
            }

            // Skip if it's part of a member access expression on the right side
            if (IsPartOfMemberAccess(identifier))
            {
                continue;
            }

            // Get symbol information
            var symbolInfo = semanticModel.GetSymbolInfo(identifier);
            var symbol = symbolInfo.Symbol;

            if (symbol == null)
            {
                continue;
            }

            // Check if it's a local variable or parameter (not a lambda parameter)
            if (symbol.Kind == SymbolKind.Local)
            {
                // Skip constants - they are compile-time values and don't need capture
                if (symbol is ILocalSymbol localSymbol && localSymbol.IsConst)
                {
                    continue;
                }

                // Ensure this is truly a local variable from outer scope
                var symbolLocation = symbol.Locations.FirstOrDefault();
                if (symbolLocation != null && !lambda.Span.Contains(symbolLocation.SourceSpan))
                {
                    localVariables.Add(identifierName);
                }
            }
            else if (
                symbol.Kind == SymbolKind.Parameter && !lambdaParameters.Contains(identifierName)
            )
            {
                // This is a parameter from an outer scope
                var symbolLocation = symbol.Locations.FirstOrDefault();
                if (symbolLocation != null && !lambda.Span.Contains(symbolLocation.SourceSpan))
                {
                    localVariables.Add(identifierName);
                }
            }
        }

        return localVariables;
    }

    private static bool IsPartOfMemberAccess(IdentifierNameSyntax identifier)
    {
        var parent = identifier.Parent;

        if (parent is MemberAccessExpressionSyntax memberAccess)
        {
            return memberAccess.Name == identifier;
        }

        if (parent is MemberBindingExpressionSyntax)
        {
            return true;
        }

        return false;
    }
}
