using System.Collections.Generic;
using System.Linq;
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
            var nullChecks = ExtractNullChecks(ternary.Condition);
            if (nullChecks.Count == 0)
                return null;

            // Check if the else clause is null or a null cast
            if (!IsNullOrNullCast(ternary.WhenFalse))
                return null;

            // Extract the "when true" expression, removing any type casts
            var whenTrueExpr = RemoveNullableCast(ternary.WhenTrue);

            // Build the null-conditional chain
            var nullConditionalExpr = BuildNullConditionalChain(whenTrueExpr, nullChecks);
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

        private static List<ExpressionSyntax> ExtractNullChecks(ExpressionSyntax condition)
        {
            var nullChecks = new List<ExpressionSyntax>();

            // Handle chained && expressions
            var current = condition;
            while (current is BinaryExpressionSyntax binary
                && binary.Kind() == SyntaxKind.LogicalAndExpression)
            {
                // Check the right side
                if (IsNullCheckComparison(binary.Right, out var checkedExpr))
                {
                    nullChecks.Insert(0, checkedExpr);
                }

                current = binary.Left;
            }

            // Check the final (leftmost) expression
            if (IsNullCheckComparison(current, out var finalCheckedExpr))
            {
                nullChecks.Insert(0, finalCheckedExpr);
            }

            return nullChecks;
        }

        private static bool IsNullCheckComparison(
            ExpressionSyntax expr,
            out ExpressionSyntax checkedExpression
        )
        {
            checkedExpression = null!;

            if (expr is not BinaryExpressionSyntax binary)
                return false;

            // Check for "x != null" pattern
            if (binary.Kind() == SyntaxKind.NotEqualsExpression)
            {
                if (IsNullLiteral(binary.Right))
                {
                    checkedExpression = binary.Left;
                    return true;
                }
                if (IsNullLiteral(binary.Left))
                {
                    checkedExpression = binary.Right;
                    return true;
                }
            }

            return false;
        }

        private static bool IsNullLiteral(ExpressionSyntax expr)
        {
            return expr is LiteralExpressionSyntax literal
                && literal.Kind() == SyntaxKind.NullLiteralExpression;
        }

        private static bool IsNullOrNullCast(ExpressionSyntax expr)
        {
            // Check for simple null
            if (IsNullLiteral(expr))
                return true;

            // Check for (Type?)null cast
            if (
                expr is CastExpressionSyntax cast
                && cast.Type is NullableTypeSyntax
                && IsNullLiteral(cast.Expression)
            )
                return true;

            return false;
        }

        private static bool IsEmptyCollectionInitializer(ExpressionSyntax expr)
        {
            // Check for [] pattern (collection expression)
            if (
                expr is CollectionExpressionSyntax collection
                && collection.Elements.Count == 0
            )
                return true;

            // Check for Array.Empty<T>() or similar patterns
            // This can be extended as needed

            return false;
        }

        private static ExpressionSyntax RemoveNullableCast(ExpressionSyntax expr)
        {
            // Remove (Type?) cast if present
            if (
                expr is CastExpressionSyntax cast
                && cast.Type is NullableTypeSyntax
            )
            {
                return cast.Expression;
            }

            return expr;
        }

        private static ExpressionSyntax? BuildNullConditionalChain(
            ExpressionSyntax whenTrueExpr,
            List<ExpressionSyntax> nullChecks
        )
        {
            if (nullChecks.Count == 0)
                return null;

            // Verify that the whenTrueExpr uses the null-checked expressions
            // and build the appropriate null-conditional chain

            // Start with the whenTrueExpr
            var result = whenTrueExpr;

            // We need to match each null check with the corresponding member access
            // For example:
            // - null checks: [s.Foo, s.Foo.Bar]
            // - whenTrue: s.Foo.Bar.Id
            // - result: s.Foo?.Bar?.Id

            // Build a mapping of expressions that need to be converted to null-conditional
            var nullCheckStrings = new HashSet<string>(nullChecks.Select(nc => nc.ToString()));

            // Use a rewriter to convert member accesses to null-conditional accesses
            var rewriter = new NullConditionalAccessRewriter(nullCheckStrings);
            result = (ExpressionSyntax)rewriter.Visit(result);

            return result;
        }
    }

    private class NullConditionalAccessRewriter : CSharpSyntaxRewriter
    {
        private readonly HashSet<string> _nullCheckedExpressions;

        public NullConditionalAccessRewriter(HashSet<string> nullCheckedExpressions)
        {
            _nullCheckedExpressions = nullCheckedExpressions;
        }

        public override SyntaxNode? VisitMemberAccessExpression(
            MemberAccessExpressionSyntax node
        )
        {
            // Check if this member access expression's base matches any null check
            var baseExpr = node.Expression;
            var baseExprString = baseExpr.ToString();

            if (_nullCheckedExpressions.Contains(baseExprString))
            {
                // Convert to conditional access: a.b -> a?.b
                var conditionalAccess = SyntaxFactory.ConditionalAccessExpression(
                    baseExpr,
                    SyntaxFactory.MemberBindingExpression(node.Name)
                );

                // Continue visiting the result to handle chained accesses
                return base.Visit(conditionalAccess);
            }

            // Recursively check if we're part of a longer chain
            // Visit the base expression first
            var newBase = (ExpressionSyntax)Visit(baseExpr)!;

            // If the base was changed to a conditional access, we need to convert this member access too
            if (newBase is ConditionalAccessExpressionSyntax)
            {
                // We're part of a chain, convert to member binding
                var conditionalAccess = SyntaxFactory.ConditionalAccessExpression(
                    baseExpr,
                    SyntaxFactory.MemberBindingExpression(node.Name)
                );
                return base.Visit(conditionalAccess);
            }

            if (newBase != baseExpr)
            {
                return node.WithExpression(newBase);
            }

            return base.VisitMemberAccessExpression(node);
        }
    }
}
