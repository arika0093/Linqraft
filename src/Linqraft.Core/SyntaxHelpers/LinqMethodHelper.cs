using System.Linq;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Linqraft.Core.SyntaxHelpers;

/// <summary>
/// Helper methods for working with LINQ method invocations
/// </summary>
public static class LinqMethodHelper
{
    /// <summary>
    /// Finds a LINQ method invocation in an expression
    /// </summary>
    /// <param name="expression">The expression to search</param>
    /// <param name="methodNames">The LINQ method names to search for (e.g., "Select", "SelectMany")</param>
    /// <returns>The invocation expression, or null if not found</returns>
    public static InvocationExpressionSyntax? FindLinqMethodInvocation(
        ExpressionSyntax expression,
        params string[] methodNames
    )
    {
        // Handle binary expressions (e.g., ?? operator): s.OrderItems?.Select(...) ?? []
        if (expression is BinaryExpressionSyntax binaryExpr)
        {
            // Check left side for invocation
            var leftResult = FindLinqMethodInvocation(binaryExpr.Left, methodNames);
            if (leftResult is not null)
                return leftResult;
        }

        // Handle ternary expressions (condition ? trueExpr : falseExpr)
        // e.g., x != null ? x.Items.Select(...) : null
        if (expression is ConditionalExpressionSyntax conditionalExpr)
        {
            // Check the WhenTrue branch first (more common case)
            var whenTrueResult = FindLinqMethodInvocation(conditionalExpr.WhenTrue, methodNames);
            if (whenTrueResult is not null)
                return whenTrueResult;

            // Also check the WhenFalse branch (for reversed conditions)
            var whenFalseResult = FindLinqMethodInvocation(conditionalExpr.WhenFalse, methodNames);
            if (whenFalseResult is not null)
                return whenFalseResult;
        }

        // Handle conditional access (?.): s.OrderItems?.Select(...)
        if (expression is ConditionalAccessExpressionSyntax conditionalAccess)
        {
            // The WhenNotNull part contains the actual method call
            return FindLinqMethodInvocation(conditionalAccess.WhenNotNull, methodNames);
        }

        // Handle member binding expression (part of ?. expression): .Select(...)
        if (expression is MemberBindingExpressionSyntax)
        {
            // This is the .Select part of ?.Select - we need to look at the parent
            return null;
        }

        // Handle invocation binding expression (part of ?. expression): Select(...)
        if (
            expression is InvocationExpressionSyntax invocationBinding
            && invocationBinding.Expression is MemberBindingExpressionSyntax memberBinding
            && methodNames.Contains(memberBinding.Name.Identifier.Text)
        )
        {
            return invocationBinding;
        }

        // Direct invocation: s.Childs.Select(...)
        if (expression is InvocationExpressionSyntax invocation)
        {
            if (
                invocation.Expression is MemberAccessExpressionSyntax memberAccess
                && methodNames.Contains(memberAccess.Name.Identifier.Text)
            )
            {
                return invocation;
            }

            // Chained method call (e.g., ToList, ToArray, etc.): s.Childs.Select(...).ToList()
            // The invocation is for ToList, but we need to find the LINQ method in its expression
            if (invocation.Expression is MemberAccessExpressionSyntax chainedMemberAccess)
            {
                // Recursively search in the expression part (before the chained method)
                return FindLinqMethodInvocation(chainedMemberAccess.Expression, methodNames);
            }
        }

        return null;
    }

    /// <summary>
    /// Checks if an expression contains a Select invocation
    /// </summary>
    /// <param name="expression">The expression to check</param>
    /// <returns>True if the expression contains a Select invocation</returns>
    public static bool IsSelectInvocation(ExpressionSyntax expression)
    {
        return FindLinqMethodInvocation(expression, "Select") is not null;
    }

    /// <summary>
    /// Checks if an expression contains a SelectMany invocation
    /// </summary>
    /// <param name="expression">The expression to check</param>
    /// <returns>True if the expression contains a SelectMany invocation</returns>
    public static bool IsSelectManyInvocation(ExpressionSyntax expression)
    {
        return FindLinqMethodInvocation(expression, "SelectMany") is not null;
    }

    /// <summary>
    /// Information about a LINQ invocation
    /// </summary>
    public record LinqInvocationInfo(
        InvocationExpressionSyntax Invocation,
        string BaseExpression,
        string ParameterName,
        string ChainedMethods,
        bool HasNullableAccess,
        string? CoalescingDefaultValue,
        string? NullCheckExpression = null  // The expression to check for null (only used when HasNullableAccess is true)
    );

    /// <summary>
    /// Extracts detailed information about a LINQ method invocation
    /// </summary>
    /// <param name="syntax">The expression syntax</param>
    /// <param name="methodName">The LINQ method name (e.g., "Select", "SelectMany")</param>
    /// <returns>Information about the LINQ invocation, or null if not found</returns>
    public static LinqInvocationInfo? ExtractLinqInvocationInfo(
        ExpressionSyntax syntax,
        string methodName
    )
    {
        string? coalescingDefaultValue = null;
        var currentSyntax = syntax;

        // Check for coalescing operator (??)
        if (
            syntax is BinaryExpressionSyntax
            {
                RawKind: (int)SyntaxKind.CoalesceExpression
            } binaryExpr
        )
        {
            var rightSide = binaryExpr.Right.ToString().Trim();
            coalescingDefaultValue = rightSide == "[]" ? null : rightSide;
            currentSyntax = binaryExpr.Left;
        }

        // Check for conditional access (?.)
        bool hasNullableAccess = false;
        ConditionalAccessExpressionSyntax? conditionalAccess = null;
        if (currentSyntax is ConditionalAccessExpressionSyntax condAccess)
        {
            hasNullableAccess = true;
            conditionalAccess = condAccess;
            currentSyntax = condAccess.WhenNotNull;
        }

        // Find the LINQ method invocation
        InvocationExpressionSyntax? linqInvocation = null;
        string chainedMethods = "";

        if (currentSyntax is InvocationExpressionSyntax invocation)
        {
            // Check if this is the LINQ method or chained (e.g., .Select().ToList())
            if (
                invocation.Expression is MemberAccessExpressionSyntax memberAccess
                && memberAccess.Name.Identifier.Text == methodName
            )
            {
                linqInvocation = invocation;
            }
            else if (
                invocation.Expression is MemberBindingExpressionSyntax memberBinding
                && memberBinding.Name.Identifier.Text == methodName
            )
            {
                linqInvocation = invocation;
            }
            else if (invocation.Expression is MemberAccessExpressionSyntax chainedMember)
            {
                // This is a chained method like .ToList()
                chainedMethods = $".{chainedMember.Name}{invocation.ArgumentList}";
                if (chainedMember.Expression is InvocationExpressionSyntax innerInvocation)
                {
                    if (
                        innerInvocation.Expression is MemberAccessExpressionSyntax innerMember
                        && innerMember.Name.Identifier.Text == methodName
                    )
                    {
                        linqInvocation = innerInvocation;
                    }
                    else if (
                        innerInvocation.Expression is MemberBindingExpressionSyntax innerBinding
                        && innerBinding.Name.Identifier.Text == methodName
                    )
                    {
                        linqInvocation = innerInvocation;
                    }
                }
            }
        }

        if (linqInvocation is null)
            return null;

        // Extract lambda parameter name using Roslyn
        var lambda = LambdaHelper.FindLambdaInArguments(linqInvocation.ArgumentList);
        string paramName = lambda is not null ? LambdaHelper.GetLambdaParameterName(lambda) : "x"; // Default

        // Extract base expression (the collection being operated on)
        // And null check expression (the part before ?. that needs to be checked for null)
        string baseExpression;
        string? nullCheckExpression = null;
        if (hasNullableAccess && conditionalAccess is not null)
        {
            // For ?.Select or ?.SomeProperty.Select, extract the full base expression
            // The base before ?. is conditionalAccess.Expression
            // There might be additional member access between ?. and .Select
            var beforeNullConditional = conditionalAccess.Expression.ToString();
            nullCheckExpression = beforeNullConditional; // This is what we check for null

            // Check if there's member access between ?. and .Select
            // For example: x?.Child4s.Select(...) - we need "x.Child4s" as base
            if (linqInvocation.Expression is MemberAccessExpressionSyntax linqMemberAccess)
            {
                // The expression before .Select might have more member access
                var beforeSelect = linqMemberAccess.Expression;
                if (beforeSelect is MemberBindingExpressionSyntax memberBindingBeforeSelect)
                {
                    // This is like ".Child4s" - combine with the base before ?.
                    baseExpression = beforeNullConditional + memberBindingBeforeSelect.ToString();
                }
                else
                {
                    // Normal member access
                    baseExpression = beforeNullConditional;
                }
            }
            else if (linqInvocation.Expression is MemberBindingExpressionSyntax)
            {
                // Direct ?.Select case (no additional member access)
                baseExpression = beforeNullConditional;
            }
            else
            {
                baseExpression = beforeNullConditional;
            }
        }
        else if (linqInvocation.Expression is MemberAccessExpressionSyntax linqMember)
        {
            baseExpression = linqMember.Expression.ToString();
        }
        else if (linqInvocation.Expression is MemberBindingExpressionSyntax)
        {
            // For ?.Select case, the base should be from conditional access
            baseExpression = conditionalAccess?.Expression.ToString() ?? "";
        }
        else
        {
            return null;
        }

        return new LinqInvocationInfo(
            linqInvocation,
            baseExpression,
            paramName,
            chainedMethods,
            hasNullableAccess,
            coalescingDefaultValue,
            nullCheckExpression
        );
    }
}
