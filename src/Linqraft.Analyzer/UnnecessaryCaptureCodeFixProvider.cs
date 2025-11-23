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
/// Code fix provider that removes unnecessary capture variables from SelectExpr
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(UnnecessaryCaptureCodeFixProvider))]
[Shared]
public class UnnecessaryCaptureCodeFixProvider : CodeFixProvider
{
    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(UnnecessaryCaptureAnalyzer.AnalyzerId);

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
                title: "Remove unnecessary capture variables",
                createChangedDocument: c =>
                    RemoveUnnecessaryCapturesAsync(context.Document, invocation, c),
                equivalenceKey: "RemoveUnnecessaryCaptures"
            ),
            diagnostic
        );
    }

    private async Task<Document> RemoveUnnecessaryCapturesAsync(
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
        var lambdaParameters = LambdaHelper.GetLambdaParameterNames(lambda);

        // Get already captured variables
        var capturedVariables = CaptureHelper.GetCapturedVariables(invocation, semanticModel);

        // Find variables that are actually used in the lambda
        var usedVariables = FindUsedVariables(lambda, lambdaParameters, semanticModel);

        // Find variables that should remain in capture
        var variablesToKeep = new HashSet<string>(capturedVariables.Where(v => usedVariables.Contains(v)));

        // Create new invocation with updated or removed capture argument
        InvocationExpressionSyntax newInvocation;

        if (variablesToKeep.Count == 0)
        {
            // Remove the entire capture argument
            newInvocation = RemoveCaptureArgument(invocation);
        }
        else
        {
            // Update the capture argument with only the necessary variables
            newInvocation = UpdateCaptureArgument(invocation, variablesToKeep);
        }

        var newRoot = root.ReplaceNode(invocation, newInvocation);
        var documentWithNewRoot = document.WithSyntaxRoot(newRoot);
        return await CodeFixFormattingHelper
            .NormalizeLineEndingsOnlyAsync(documentWithNewRoot, cancellationToken)
            .ConfigureAwait(false);
    }

    private static InvocationExpressionSyntax RemoveCaptureArgument(
        InvocationExpressionSyntax invocation
    )
    {
        var captureArgIndex = FindCaptureArgumentIndex(invocation);
        if (captureArgIndex < 0)
        {
            return invocation;
        }

        var arguments = invocation.ArgumentList.Arguments.ToList();
        arguments.RemoveAt(captureArgIndex);

        var newArgumentList = SyntaxFactory.ArgumentList(
            SyntaxFactory.SeparatedList(arguments)
        );

        return invocation.WithArgumentList(newArgumentList);
    }

    private static InvocationExpressionSyntax UpdateCaptureArgument(
        InvocationExpressionSyntax invocation,
        HashSet<string> variablesToKeep
    )
    {
        var captureArgIndex = FindCaptureArgumentIndex(invocation);
        if (captureArgIndex < 0)
        {
            return invocation;
        }

        var captureArgument = CaptureHelper.CreateCaptureArgument(variablesToKeep);
        var arguments = invocation.ArgumentList.Arguments.ToList();
        arguments[captureArgIndex] = captureArgument;

        var newArgumentList = SyntaxFactory.ArgumentList(
            SyntaxFactory.SeparatedList(arguments)
        );

        return invocation.WithArgumentList(newArgumentList);
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

        // Check if there are more than 1 arguments (second would be capture)
        if (invocation.ArgumentList.Arguments.Count > 1)
        {
            return 1;
        }

        return -1;
    }

    private static bool IsSelectExprCall(ExpressionSyntax expression)
    {
        return expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.Text
                == SelectExprHelper.MethodName,
            IdentifierNameSyntax identifier => identifier.Identifier.Text
                == SelectExprHelper.MethodName,
            _ => false,
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

    private static HashSet<string> FindUsedVariables(
        LambdaExpressionSyntax lambda,
        ImmutableHashSet<string> lambdaParameters,
        SemanticModel semanticModel
    )
    {
        var usedVariables = new HashSet<string>();
        var bodyExpression = lambda switch
        {
            SimpleLambdaExpressionSyntax simple => simple.Body,
            ParenthesizedLambdaExpressionSyntax paren => paren.Body,
            _ => null,
        };

        if (bodyExpression == null)
        {
            return usedVariables;
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

            // Add to used variables
            usedVariables.Add(identifierName);
        }

        // Also check for member accesses (e.g., this.Property, obj.Property)
        var memberAccesses = bodyExpression
            .DescendantNodes()
            .OfType<MemberAccessExpressionSyntax>();

        foreach (var memberAccess in memberAccesses)
        {
            // Skip if this is a lambda parameter access (e.g., s.Property)
            if (
                memberAccess.Expression is IdentifierNameSyntax exprId
                && lambdaParameters.Contains(exprId.Identifier.Text)
            )
            {
                continue;
            }

            // Get symbol information for the member being accessed
            var symbolInfo = semanticModel.GetSymbolInfo(memberAccess);
            var symbol = symbolInfo.Symbol;

            if (symbol == null)
            {
                continue;
            }

            // Check if this is a field or property that might be captured
            if (symbol.Kind == SymbolKind.Field || symbol.Kind == SymbolKind.Property)
            {
                // Check if it's 'this.Member'
                if (memberAccess.Expression is ThisExpressionSyntax)
                {
                    var memberName = memberAccess.Name.Identifier.Text;
                    usedVariables.Add(memberName);
                }
                // Check if it's accessing a static member via type name
                else if (symbol.IsStatic && memberAccess.Expression is IdentifierNameSyntax)
                {
                    var memberName = memberAccess.Name.Identifier.Text;
                    usedVariables.Add(memberName);
                }
                // Check if it's accessing a member via a local variable/parameter
                else if (memberAccess.Expression is IdentifierNameSyntax exprIdentifier)
                {
                    // The expression part is the variable being used
                    usedVariables.Add(exprIdentifier.Identifier.Text);
                }
            }
        }

        return usedVariables;
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

        // Check if this is inside a NameEquals (property name in anonymous object)
        if (parent is NameEqualsSyntax)
        {
            return true;
        }

        // Check if this is the left side of an assignment in an object initializer
        if (parent is AssignmentExpressionSyntax assignment && assignment.Left == identifier)
        {
            return true;
        }

        return false;
    }
}
