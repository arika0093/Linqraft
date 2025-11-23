using System.Collections.Generic;
using Linqraft.Core.SyntaxHelpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Linqraft.Analyzer;

/// <summary>
/// Simplifies ternary operators used for null checks into null-conditional operators.
/// </summary>
internal static class TernaryNullCheckSimplifier
{
    /// <summary>
    /// Simplifies all ternary null checks in the given expression to use ?. and ?? operators.
    /// </summary>
    public static ExpressionSyntax SimplifyTernaryNullChecks(ExpressionSyntax expression)
    {
        var rewriter = new TernaryNullCheckRewriter();
        return (ExpressionSyntax)rewriter.Visit(expression);
    }

    /// <summary>
    /// Simplifies ternary null checks in lambda expressions within an invocation.
    /// Processes all lambda arguments and applies null-conditional operator simplifications.
    /// </summary>
    public static InvocationExpressionSyntax SimplifyTernaryNullChecksInInvocation(
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
                var simplifiedBody = SimplifyTernaryNullChecks(bodyExpr);
                var newLambda = simpleLambda.WithBody(simplifiedBody);
                newArguments.Add(argument.WithExpression(newLambda));
            }
            else if (
                argument.Expression is ParenthesizedLambdaExpressionSyntax parenLambda
                && parenLambda.Body is ExpressionSyntax parenBodyExpr
            )
            {
                var simplifiedBody = SimplifyTernaryNullChecks(parenBodyExpr);
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

    private class TernaryNullCheckRewriter : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitConditionalExpression(ConditionalExpressionSyntax node)
        {
            // First, visit children to handle nested ternaries
            node = (ConditionalExpressionSyntax)base.VisitConditionalExpression(node)!;

            // Try to simplify this ternary if it's a null check
            var simplified = TrySimplifyTernaryNullCheck(node);
            return simplified ?? node;
        }

        private static ExpressionSyntax? TrySimplifyTernaryNullCheck(
            ConditionalExpressionSyntax ternary
        )
        {
            // Check if the condition is a null check (simple or complex with &&)
            var nullChecks = NullConditionalHelper.ExtractNullChecks(ternary.Condition);
            if (nullChecks.Count == 0)
                return null;

            // Extract both expressions, removing any type casts
            var whenTrueExpr = NullConditionalHelper.RemoveNullableCast(ternary.WhenTrue);
            var whenFalseExpr = NullConditionalHelper.RemoveNullableCast(ternary.WhenFalse);

            // Check if one branch is null and the other contains an object creation
            // If so, don't simplify - this should be handled by LRQS004
            var whenTrueIsNull = NullConditionalHelper.IsNullOrNullCast(ternary.WhenTrue);
            var whenFalseIsNull = NullConditionalHelper.IsNullOrNullCast(ternary.WhenFalse);
            var whenTrueHasObject = IsObjectCreation(whenTrueExpr);
            var whenFalseHasObject = IsObjectCreation(whenFalseExpr);

            // If one branch is null and the other has an object creation, skip simplification
            // This handles both: condition ? new{} : null  AND  condition ? null : new{}
            if ((whenTrueIsNull && whenFalseHasObject) || (whenFalseIsNull && whenTrueHasObject))
            {
                return null;
            }

            // Only proceed if the else clause is null (standard pattern)
            if (!whenFalseIsNull)
                return null;

            // Build the null-conditional chain for the WhenTrue expression
            var nullConditionalExpr = NullConditionalHelper.BuildNullConditionalChain(
                whenTrueExpr,
                nullChecks
            );
            if (nullConditionalExpr == null)
                return null;

            // Preserve leading trivia (including continuation indentation)
            nullConditionalExpr = nullConditionalExpr.WithLeadingTrivia(
                ternary.GetLeadingTrivia()
            );

            // If the else clause is not simply null but a collection initializer, use ?? operator
            if (IsEmptyCollectionInitializer(ternary.WhenFalse))
            {
                return SyntaxFactory.BinaryExpression(
                    SyntaxKind.CoalesceExpression,
                    nullConditionalExpr,
                    ternary.WhenFalse
                );
            }

            return nullConditionalExpr;
        }

        private static bool IsEmptyCollectionInitializer(ExpressionSyntax expr)
        {
            // Check for [] pattern (collection expression)
            if (expr is CollectionExpressionSyntax collection && collection.Elements.Count == 0)
                return true;

            // Check for Array.Empty<T>() or similar patterns
            // This can be extended as needed

            return false;
        }

        private static bool IsObjectCreation(ExpressionSyntax expr)
        {
            // Check if the expression is an object creation (named or anonymous)
            return expr is ObjectCreationExpressionSyntax
                || expr is AnonymousObjectCreationExpressionSyntax;
        }
    }
}
