using System.Collections.Generic;
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
    /// Finds a SelectExpr invocation in an expression
    /// </summary>
    /// <param name="expression">The expression to search</param>
    /// <returns>The invocation expression, or null if not found</returns>
    public static InvocationExpressionSyntax? FindSelectExprInvocation(ExpressionSyntax expression)
    {
        return FindLinqMethodInvocation(expression, "SelectExpr");
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
        string? NullCheckExpression = null,
        /// <summary>
        /// The list of chained method invocation syntax nodes AFTER the target method (e.g., .FirstOrDefault(...))
        /// These nodes are ordered from innermost to outermost (same order as ChainedMethods string)
        /// </summary>
        IReadOnlyList<InvocationExpressionSyntax>? ChainedInvocations = null,
        /// <summary>
        /// The list of method invocation syntax nodes BEFORE the target method (e.g., .Where(...), .OrderBy(...))
        /// These are part of the base expression and also need to be fully qualified.
        /// </summary>
        IReadOnlyList<InvocationExpressionSyntax>? BaseInvocations = null
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

        // Find the LINQ method invocation and collect chained methods
        InvocationExpressionSyntax? linqInvocation = null;
        var chainedMethodsList = new List<string>();
        var chainedInvocationsList = new List<InvocationExpressionSyntax>();

        // Walk the invocation chain to find the target LINQ method and collect chained methods
        var processingExpr = currentSyntax;
        while (processingExpr is InvocationExpressionSyntax invocation)
        {
            string? currentMethodName = null;
            ExpressionSyntax? nextExpr = null;

            if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
            {
                currentMethodName = memberAccess.Name.Identifier.Text;
                nextExpr = memberAccess.Expression;
            }
            else if (invocation.Expression is MemberBindingExpressionSyntax memberBinding)
            {
                currentMethodName = memberBinding.Name.Identifier.Text;
                // For member binding, there's no further expression to walk
                nextExpr = null;
            }

            if (currentMethodName == methodName)
            {
                // Found the target LINQ method
                linqInvocation = invocation;
                break;
            }

            if (currentMethodName is not null)
            {
                // This is a chained method after the LINQ method (e.g., .ToList(), .FirstOrDefault())
                chainedMethodsList.Insert(0, $".{currentMethodName}{invocation.ArgumentList}");
                chainedInvocationsList.Insert(0, invocation);
            }

            if (nextExpr is null)
                break;

            processingExpr = nextExpr;
        }

        if (linqInvocation is null)
            return null;

        // Extract lambda parameter name using Roslyn
        var lambda = LambdaHelper.FindLambdaInArguments(linqInvocation.ArgumentList);
        string paramName = lambda is not null ? LambdaHelper.GetLambdaParameterName(lambda) : "x"; // Default

        // Combine chained methods into a single string
        var chainedMethods = string.Concat(chainedMethodsList);

        // Extract base expression (the collection being operated on)
        // and null check expression (the part before ?. that needs null check)
        string baseExpression;
        string? nullCheckExpression = null;
        var baseInvocationsList = new List<InvocationExpressionSyntax>();

        if (linqInvocation.Expression is MemberAccessExpressionSyntax linqMember)
        {
            // For direct invocation: base.Select(...)
            if (hasNullableAccess && conditionalAccess is not null)
            {
                // For ?.XXX.Select or ?.XXX.OrderBy(...).Select, we need:
                // 1. nullCheckExpression: The expression to check for null (part before ?.)
                // 2. baseExpression: The full path to the collection that Select operates on
                // e.g., c.Child2.Child3?.Child4s.OrderBy(...).Select(...)
                // nullCheckExpression = c.Child2.Child3
                // baseExpression = c.Child2.Child3.Child4s.OrderBy(...)
                nullCheckExpression = conditionalAccess.Expression.ToString();

                var beforeSelect = linqMember.Expression.ToString();
                // Remove the leading dot if it's a member binding
                if (beforeSelect.StartsWith("."))
                {
                    beforeSelect = beforeSelect[1..];
                }
                baseExpression = $"{conditionalAccess.Expression}.{beforeSelect}";
            }
            else
            {
                baseExpression = linqMember.Expression.ToString();
            }

            // Collect invocations in the base expression (before Select)
            // e.g., s.Children.Where(...).OrderBy(...) → [Where invocation, OrderBy invocation]
            CollectBaseInvocations(linqMember.Expression, baseInvocationsList);
        }
        else if (linqInvocation.Expression is MemberBindingExpressionSyntax)
        {
            // For ?.Select case (directly after the ?.)
            // e.g., c.Items?.Select(...)
            // nullCheckExpression = c.Items
            // baseExpression = c.Items
            baseExpression = conditionalAccess?.Expression.ToString() ?? "";
            nullCheckExpression = baseExpression;
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
            nullCheckExpression,
            chainedInvocationsList.Count > 0 ? chainedInvocationsList : null,
            baseInvocationsList.Count > 0 ? baseInvocationsList : null
        );
    }

    /// <summary>
    /// Collects all invocation expressions within an expression, ordered from innermost to outermost.
    /// This is used to find invocations like .Where(), .OrderBy() that appear before the target LINQ method.
    /// </summary>
    private static void CollectBaseInvocations(
        ExpressionSyntax expression,
        List<InvocationExpressionSyntax> invocations
    )
    {
        if (expression is InvocationExpressionSyntax invocation)
        {
            // First, recursively collect from the inner expression
            if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
            {
                CollectBaseInvocations(memberAccess.Expression, invocations);
            }
            // Then add this invocation
            invocations.Add(invocation);
        }
        else if (expression is MemberAccessExpressionSyntax memberAccess)
        {
            // Continue searching in the inner expression
            CollectBaseInvocations(memberAccess.Expression, invocations);
        }
        // For other expressions (IdentifierNameSyntax, etc.), stop recursion
    }
}
