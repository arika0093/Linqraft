using System;
using System.Collections.Generic;
using System.Linq;
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
