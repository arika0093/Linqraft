using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Linqraft.Core.SyntaxHelpers;

/// <summary>
/// Helper methods for working with null-conditional operators and null checks
/// </summary>
public static class NullConditionalHelper
{
    /// <summary>
    /// Checks if an expression contains a null-conditional access operator (?.) at the top level
    /// (excluding nested lambdas)
    /// </summary>
    /// <param name="expression">The expression to check</param>
    /// <returns>True if the expression contains a top-level null-conditional access</returns>
    public static bool HasNullConditionalAccess(ExpressionSyntax expression)
    {
        // Check if ?. operator is used at the top level (excluding nested lambdas)
        var conditionalAccesses = expression
            .DescendantNodesAndSelf()
            .OfType<ConditionalAccessExpressionSyntax>();

        foreach (var conditionalAccess in conditionalAccesses)
        {
            // Check if this conditional access is inside a lambda expression
            var ancestors = conditionalAccess.Ancestors();
            var isInsideLambda = ancestors
                .TakeWhile(n => n != expression)
                .OfType<LambdaExpressionSyntax>()
                .Any();

            // If not inside a lambda, this is a top-level nullable access
            if (!isInsideLambda)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Syntax-based heuristic to determine if null check should be generated.
    /// This is a fallback for when Roslyn's NullableAnnotation is unreliable.
    /// </summary>
    /// <param name="expression">The expression to analyze</param>
    /// <returns>True if a null check should be generated based on syntax</returns>
    public static bool ShouldGenerateNullCheckFromSyntax(ExpressionSyntax expression)
    {
        // 1. If ?. is used, definitely needs null check
        if (expression.DescendantNodesAndSelf().OfType<ConditionalAccessExpressionSyntax>().Any())
        {
            return true;
        }

        // 2. Check for nested member access (e.g., x.Child.Name)
        var memberAccesses = expression
            .DescendantNodesAndSelf()
            .OfType<MemberAccessExpressionSyntax>()
            .Where(ma =>
            {
                // Exclude nested Select lambdas
                var parentLambda = ma.Ancestors().OfType<LambdaExpressionSyntax>().FirstOrDefault();
                var topLambda = expression
                    .Ancestors()
                    .OfType<LambdaExpressionSyntax>()
                    .FirstOrDefault();
                return parentLambda == topLambda;
            })
            .ToList();

        // If there are multiple levels of member access, generate null check for safety
        if (memberAccesses.Count >= 2)
        {
            // Check if the chain starts from a lambda parameter
            var firstAccess = memberAccesses.FirstOrDefault();
            if (firstAccess?.Expression is IdentifierNameSyntax)
            {
                // s.Child.Name - has intermediate navigation, needs null check
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Checks if an expression is a null literal
    /// </summary>
    /// <param name="expr">The expression to check</param>
    /// <returns>True if the expression is a null literal</returns>
    public static bool IsNullLiteral(ExpressionSyntax expr)
    {
        return expr is LiteralExpressionSyntax literal
            && literal.Kind() == SyntaxKind.NullLiteralExpression;
    }

    /// <summary>
    /// Checks if an expression is null or a nullable cast to null (e.g., (Type?)null)
    /// </summary>
    /// <param name="expr">The expression to check</param>
    /// <returns>True if the expression is null or a nullable cast to null</returns>
    public static bool IsNullOrNullCast(ExpressionSyntax expr)
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

    /// <summary>
    /// Removes nullable cast from an expression if present
    /// </summary>
    /// <param name="expr">The expression to process</param>
    /// <returns>The expression with nullable cast removed</returns>
    public static ExpressionSyntax RemoveNullableCast(ExpressionSyntax expr)
    {
        // Remove (Type?) cast if present
        if (expr is CastExpressionSyntax cast && cast.Type is NullableTypeSyntax)
        {
            return cast.Expression;
        }

        return expr;
    }

    /// <summary>
    /// Extracts null check expressions from a condition
    /// </summary>
    /// <param name="condition">The condition expression to analyze</param>
    /// <returns>List of expressions being null-checked</returns>
    public static List<ExpressionSyntax> ExtractNullChecks(ExpressionSyntax condition)
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

    /// <summary>
    /// Checks if a condition has any null check (simple or complex with &&)
    /// </summary>
    /// <param name="condition">The condition expression to check</param>
    /// <returns>True if the condition contains a null check</returns>
    public static bool HasNullCheck(ExpressionSyntax condition)
    {
        // Check for simple null check: x != null or x == null
        if (condition is BinaryExpressionSyntax binary)
        {
            if (
                binary.Kind() == SyntaxKind.NotEqualsExpression
                || binary.Kind() == SyntaxKind.EqualsExpression
            )
            {
                return IsNullLiteral(binary.Left) || IsNullLiteral(binary.Right);
            }
        }

        // Check for chained null checks: x != null && y != null
        if (
            condition is BinaryExpressionSyntax logicalAnd
            && logicalAnd.Kind() == SyntaxKind.LogicalAndExpression
        )
        {
            return HasNullCheck(logicalAnd.Left) || HasNullCheck(logicalAnd.Right);
        }

        return false;
    }

    /// <summary>
    /// Builds a null-conditional chain expression from a base expression and null checks
    /// </summary>
    /// <param name="whenTrueExpr">The expression to convert to null-conditional form</param>
    /// <param name="nullChecks">List of expressions being null-checked</param>
    /// <returns>The converted null-conditional expression, or null if conversion is not possible</returns>
    public static ExpressionSyntax? BuildNullConditionalChain(
        ExpressionSyntax whenTrueExpr,
        List<ExpressionSyntax> nullChecks
    )
    {
        if (nullChecks.Count == 0)
            return null;

        // First, try to handle complex expressions with method invocations
        // For expressions like: d.InnerData.ChildMaybeNull.AnotherChilds.Where(...).Select(...).ToList()
        // We need to find where the null-checked expression appears and insert ?. there
        var result = TryBuildNullConditionalForComplexExpression(whenTrueExpr, nullChecks);
        if (result != null)
            return result;

        // Fallback to original member access chain parsing for simple expressions
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
        ExpressionSyntax whenNotNull = BuildWhenNotNullChain(
            parts,
            needsConditional,
            firstConditionalIndex + 1
        );

        return SyntaxFactory.ConditionalAccessExpression(baseExpr, whenNotNull);
    }

    /// <summary>
    /// Tries to build a null-conditional expression for complex expressions that contain
    /// method invocations (like .Where(), .Select(), .ToList(), etc.)
    /// </summary>
    private static ExpressionSyntax? TryBuildNullConditionalForComplexExpression(
        ExpressionSyntax whenTrueExpr,
        List<ExpressionSyntax> nullChecks
    )
    {
        // Check if expression contains method invocations
        var hasInvocations = whenTrueExpr
            .DescendantNodesAndSelf()
            .OfType<InvocationExpressionSyntax>()
            .Any();

        if (!hasInvocations)
            return null;

        // For each null check, try to find where it appears in the expression and insert ?.
        foreach (var nullCheck in nullChecks)
        {
            var nullCheckText = NormalizeExpressionText(nullCheck.ToString());

            // Find the member access in the expression that matches the null check
            // For example, if nullCheck is "d.InnerData.ChildMaybeNull", we need to find
            // where this appears in the expression and make it the base of a conditional access
            var matchingMemberAccess = FindMatchingMemberAccess(whenTrueExpr, nullCheckText);
            if (matchingMemberAccess != null)
            {
                // Find what comes after this member access (the "whenNotNull" part)
                var parentNode = matchingMemberAccess.Parent;

                // Handle case where the null-checked expression is the base of a member access
                // e.g., d.InnerData.ChildMaybeNull.AnotherChilds
                if (
                    parentNode is MemberAccessExpressionSyntax parentMemberAccess
                    && parentMemberAccess.Expression == matchingMemberAccess
                )
                {
                    // Build the conditional access expression:
                    // d.InnerData.ChildMaybeNull?.AnotherChilds...
                    var whenNotNullExpr = BuildWhenNotNullFromParent(
                        whenTrueExpr,
                        matchingMemberAccess,
                        parentMemberAccess.Name
                    );
                    if (whenNotNullExpr != null)
                    {
                        // Normalize the base expression by removing internal whitespace/trivia
                        // This ensures d.InnerData.ChildMaybeNull is on a single line
                        var normalizedBase = NormalizeMemberAccessChain(matchingMemberAccess);
                        return SyntaxFactory.ConditionalAccessExpression(
                            normalizedBase,
                            whenNotNullExpr
                        );
                    }
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Normalizes expression text by removing whitespace for comparison
    /// </summary>
    private static string NormalizeExpressionText(string text)
    {
        return System.Text.RegularExpressions.Regex.Replace(text, @"\s+", "");
    }

    /// <summary>
    /// Normalizes a member access chain by removing internal whitespace/trivia
    /// so that the entire chain is on a single line
    /// </summary>
    private static ExpressionSyntax NormalizeMemberAccessChain(
        MemberAccessExpressionSyntax memberAccess
    )
    {
        // Recursively normalize the expression to remove all internal whitespace
        return NormalizeExpressionRecursive(memberAccess);
    }

    /// <summary>
    /// Recursively normalizes an expression by removing internal whitespace
    /// </summary>
    private static ExpressionSyntax NormalizeExpressionRecursive(ExpressionSyntax expr)
    {
        if (expr is MemberAccessExpressionSyntax memberAccess)
        {
            var normalizedExpression = NormalizeExpressionRecursive(memberAccess.Expression);
            // Create a clean member access without any trivia on the dot or name
            return SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                normalizedExpression,
                SyntaxFactory.Token(SyntaxKind.DotToken),
                memberAccess.Name.WithoutTrivia()
            );
        }
        else if (expr is IdentifierNameSyntax identifier)
        {
            return identifier.WithoutTrivia();
        }
        else if (expr is InvocationExpressionSyntax invocation)
        {
            var normalizedExpression = NormalizeExpressionRecursive(invocation.Expression);
            return SyntaxFactory.InvocationExpression(
                normalizedExpression,
                invocation.ArgumentList
            );
        }
        // For other expressions, just remove leading/trailing trivia
        return expr.WithoutTrivia();
    }

    /// <summary>
    /// Finds a member access expression that matches the given text pattern
    /// </summary>
    private static MemberAccessExpressionSyntax? FindMatchingMemberAccess(
        ExpressionSyntax expression,
        string targetText
    )
    {
        var memberAccesses = expression
            .DescendantNodesAndSelf()
            .OfType<MemberAccessExpressionSyntax>()
            .OrderByDescending(ma => ma.Span.Length); // Start with longer matches

        foreach (var memberAccess in memberAccesses)
        {
            var normalizedText = NormalizeExpressionText(memberAccess.ToString());
            if (normalizedText == targetText)
            {
                return memberAccess;
            }
        }

        return null;
    }

    /// <summary>
    /// Adjusts the leading trivia of a dot token by reducing indentation
    /// </summary>
    private static SyntaxToken AdjustDotTokenTrivia(SyntaxToken dotToken, int spacesToRemove)
    {
        var leadingTrivia = dotToken.LeadingTrivia;
        var adjustedTrivia = new List<SyntaxTrivia>();

        foreach (var trivia in leadingTrivia)
        {
            if (trivia.IsKind(SyntaxKind.WhitespaceTrivia))
            {
                var whitespace = trivia.ToString();
                if (whitespace.Length > spacesToRemove)
                {
                    // Remove the specified number of spaces
                    adjustedTrivia.Add(
                        SyntaxFactory.Whitespace(whitespace.Substring(spacesToRemove))
                    );
                }
                else
                {
                    // If whitespace is too short, just keep it
                    adjustedTrivia.Add(trivia);
                }
            }
            else
            {
                adjustedTrivia.Add(trivia);
            }
        }

        return dotToken.WithLeadingTrivia(adjustedTrivia);
    }

    /// <summary>
    /// Builds the "whenNotNull" expression for a conditional access, starting from the member name
    /// after the null-checked expression
    /// </summary>
    private static ExpressionSyntax? BuildWhenNotNullFromParent(
        ExpressionSyntax fullExpr,
        MemberAccessExpressionSyntax nullCheckedExpr,
        SimpleNameSyntax firstMemberAfterNullCheck
    )
    {
        // We need to reconstruct the expression after the null-checked part
        // For: d.InnerData.ChildMaybeNull.AnotherChilds.Where(...).Select(...).ToList()
        // If nullCheckedExpr is d.InnerData.ChildMaybeNull,
        // We want to build: .AnotherChilds.Where(...).Select(...).ToList()
        // As a MemberBindingExpression followed by invocations

        // Find the position in the tree where nullCheckedExpr ends
        // and build everything after it as member bindings and invocations
        var result = RebuildExpressionAsConditionalAccess(fullExpr, nullCheckedExpr);
        return result;
    }

    /// <summary>
    /// Rebuilds an expression tree replacing a member access point with a conditional access
    /// </summary>
    private static ExpressionSyntax? RebuildExpressionAsConditionalAccess(
        ExpressionSyntax fullExpr,
        MemberAccessExpressionSyntax nullCheckedExpr
    )
    {
        // Find where nullCheckedExpr is in the tree and what comes after it
        // Walk up from nullCheckedExpr to understand the structure

        var current = nullCheckedExpr.Parent;
        var accessChain = new List<SyntaxNode>();

        // Collect all nodes from the null-checked expression up to the full expression
        while (current != null && current != fullExpr.Parent)
        {
            accessChain.Add(current);
            if (current == fullExpr)
                break;
            current = current.Parent;
        }

        if (accessChain.Count == 0)
            return null;

        // Now build the whenNotNull expression from the collected chain
        // The first item after nullCheckedExpr should become a MemberBindingExpression
        var firstNode = accessChain[0];

        if (
            firstNode is MemberAccessExpressionSyntax firstMemberAccess
            && firstMemberAccess.Expression == nullCheckedExpr
        )
        {
            // Start with a member binding for the first member after null check
            // Use a clean dot token without extra trivia (it goes right after ?.)
            var memberBinding = SyntaxFactory.MemberBindingExpression(
                SyntaxFactory.Token(SyntaxKind.DotToken),
                firstMemberAccess.Name.WithoutTrivia()
            );
            ExpressionSyntax whenNotNull = memberBinding;

            // Track if we've had an invocation yet - after the first invocation,
            // chained methods should preserve their leading trivia (newlines + indentation)
            bool hadInvocation = false;

            // Now rebuild the rest of the chain
            for (int i = 1; i < accessChain.Count; i++)
            {
                var node = accessChain[i];

                if (node is MemberAccessExpressionSyntax memberAccess)
                {
                    // For chained method calls after the first invocation,
                    // preserve the leading trivia (newlines + indentation)
                    // but adjust the indentation to remove extra spaces from the ternary
                    var dotToken = hadInvocation
                        ? AdjustDotTokenTrivia(memberAccess.OperatorToken, 4) // Remove 4 spaces of indentation
                        : SyntaxFactory.Token(SyntaxKind.DotToken);
                    var name = hadInvocation
                        ? memberAccess.Name
                        : memberAccess.Name.WithoutTrivia();

                    whenNotNull = SyntaxFactory.MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        whenNotNull,
                        dotToken,
                        name
                    );
                }
                else if (node is InvocationExpressionSyntax invocation)
                {
                    // Add an invocation with the argument list
                    whenNotNull = SyntaxFactory.InvocationExpression(
                        whenNotNull,
                        invocation.ArgumentList
                    );
                    hadInvocation = true;
                }
                else if (node == fullExpr)
                {
                    // We've reached the full expression
                    break;
                }
            }

            return whenNotNull;
        }

        return null;
    }

    // Private helper methods

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

    private static ExpressionSyntax BuildWhenNotNullChain(
        List<string> parts,
        bool[] needsConditional,
        int startIndex
    )
    {
        // Guard against invalid input
        if (startIndex >= parts.Count)
        {
            return SyntaxFactory.IdentifierName("Error");
        }

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
            var innerWhenNotNull = BuildWhenNotNullChain(parts, needsConditional, startIndex + 1);
            var memberBinding = SyntaxFactory.MemberBindingExpression(currentName);
            return SyntaxFactory.ConditionalAccessExpression(memberBinding, innerWhenNotNull);
        }

        // Otherwise, just return this member binding
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
