using System;
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
            while (
                current is BinaryExpressionSyntax binary
                && binary.Kind() == SyntaxKind.LogicalAndExpression
            )
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
            if (expr is CollectionExpressionSyntax collection && collection.Elements.Count == 0)
                return true;

            // Check for Array.Empty<T>() or similar patterns
            // This can be extended as needed

            return false;
        }

        private static ExpressionSyntax RemoveNullableCast(ExpressionSyntax expr)
        {
            // Remove (Type?) cast if present
            if (expr is CastExpressionSyntax cast && cast.Type is NullableTypeSyntax)
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

            // Parse the full member access chain
            var parts = ParseMemberAccessChain(whenTrueExpr);
            if (parts.Count == 0)
                return whenTrueExpr;

            // Determine which positions need conditional access (?)
            var nullCheckSet = new HashSet<string>(nullChecks.Select(nc => nc.ToString()));
            var needsConditional = new bool[parts.Count - 1];

            for (int i = 0; i < parts.Count - 1; i++)
            {
                var partialPath = string.Join(".", parts.Take(i + 1));
                needsConditional[i] = nullCheckSet.Contains(partialPath);
            }

            // Find the first conditional position
            int firstConditionalIndex = -1;
            for (int i = 0; i < needsConditional.Length; i++)
            {
                if (needsConditional[i])
                {
                    firstConditionalIndex = i;
                    break;
                }
            }

            if (firstConditionalIndex == -1)
                return whenTrueExpr; // No conditionals needed

            // Build the base expression (everything before the first conditional)
            ExpressionSyntax baseExpr = SyntaxFactory.IdentifierName(parts[0]);
            for (int i = 1; i <= firstConditionalIndex; i++)
            {
                baseExpr = SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    baseExpr,
                    SyntaxFactory.IdentifierName(parts[i])
                );
            }

            // Build the whenNotNull expression (everything after the first conditional)
            // This is built recursively from right to left
            ExpressionSyntax whenNotNull = BuildWhenNotNullChain(
                parts,
                needsConditional,
                firstConditionalIndex + 1
            );

            return SyntaxFactory.ConditionalAccessExpression(baseExpr, whenNotNull);
        }

        private static ExpressionSyntax BuildWhenNotNullChain(
            List<string> parts,
            bool[] needsConditional,
            int startIndex
        )
        {
            // Guard against invalid input
            if (startIndex >= parts.Count)
            {
                // This shouldn't happen in normal usage
                return SyntaxFactory.IdentifierName("Error");
            }

            // Build the chain of member bindings with possible nested conditional accesses
            var currentName = SyntaxFactory.IdentifierName(parts[startIndex]);

            // If this is the last element, just return a member binding
            if (startIndex == parts.Count - 1)
            {
                return SyntaxFactory.MemberBindingExpression(currentName);
            }

            // Check if the next position needs conditional access
            if (startIndex < needsConditional.Length && needsConditional[startIndex])
            {
                // Build a conditional access for the next member
                var innerWhenNotNull = BuildWhenNotNullChain(
                    parts,
                    needsConditional,
                    startIndex + 1
                );
                var memberBinding = SyntaxFactory.MemberBindingExpression(currentName);
                return SyntaxFactory.ConditionalAccessExpression(memberBinding, innerWhenNotNull);
            }

            // Otherwise, just return this member binding
            // Note: In typical ternary null check patterns, we won't have consecutive
            // non-conditional members in the whenNotNull part, so this is the terminal case
            return SyntaxFactory.MemberBindingExpression(currentName);
        }

        private static List<string> ParseMemberAccessChain(ExpressionSyntax expr)
        {
            var parts = new List<string>();
            var current = expr;

            while (current != null)
            {
                switch (current)
                {
                    case MemberAccessExpressionSyntax memberAccess:
                        parts.Insert(0, memberAccess.Name.Identifier.Text);
                        current = memberAccess.Expression;
                        break;
                    case IdentifierNameSyntax identifier:
                        parts.Insert(0, identifier.Identifier.Text);
                        current = null;
                        break;
                    default:
                        current = null;
                        break;
                }
            }

            return parts;
        }
    }
}
