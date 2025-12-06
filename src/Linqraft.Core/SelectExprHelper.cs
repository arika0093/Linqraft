using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Linqraft.Core;

/// <summary>
/// Helper class for identifying SelectExpr method invocations
/// </summary>
public static class SelectExprHelper
{
    /// <summary>
    /// The method name for SelectExpr
    /// </summary>
    public const string MethodName = "SelectExpr";

    /// <summary>
    /// Checks if the given symbol represents a SelectExpr method from Linqraft
    /// </summary>
    /// <param name="symbol">The symbol to check</param>
    /// <returns>True if the symbol is a SelectExpr method from Linqraft</returns>
    public static bool IsSelectExprMethod(ISymbol? symbol)
    {
        if (symbol is not IMethodSymbol methodSymbol)
            return false;

        // Check method name
        if (methodSymbol.Name != MethodName)
            return false;

        // Check if it's an extension method
        if (!methodSymbol.IsExtensionMethod)
            return false;

        // Check if the containing namespace starts with "Linqraft"
        var containingNamespace = methodSymbol.ContainingNamespace?.ToDisplayString();
        if (containingNamespace == null || !containingNamespace.StartsWith("Linqraft"))
            return false;

        return true;
    }

    /// <summary>
    /// Checks if the given invocation expression is a SelectExpr call (syntax-level check)
    /// </summary>
    /// <param name="expression">The expression to check</param>
    /// <returns>True if the expression appears to be a SelectExpr call</returns>
    /// <remarks>
    /// This is a syntax-level check only and should be followed up with semantic validation
    /// using <see cref="IsSelectExprMethod"/> when SemanticModel is available.
    /// </remarks>
    public static bool IsSelectExprInvocationSyntax(ExpressionSyntax expression)
    {
        switch (expression)
        {
            case MemberAccessExpressionSyntax memberAccess:
                return memberAccess.Name.Identifier.Text == MethodName;

            case IdentifierNameSyntax identifier:
                return identifier.Identifier.Text == MethodName;

            default:
                return false;
        }
    }

    /// <summary>
    /// Checks if the given invocation is a SelectExpr call using semantic analysis
    /// </summary>
    /// <param name="invocation">The invocation expression to check</param>
    /// <param name="semanticModel">The semantic model for analysis</param>
    /// <returns>True if the invocation is a SelectExpr method from Linqraft</returns>
    public static bool IsSelectExprInvocation(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel
    )
    {
        // First check syntax level
        if (!IsSelectExprInvocationSyntax(invocation.Expression))
            return false;

        // Then validate with semantic model
        var symbolInfo = semanticModel.GetSymbolInfo(invocation);
        return IsSelectExprMethod(symbolInfo.Symbol);
    }

    /// <summary>
    /// Checks if the given SelectExpr invocation is nested inside another SelectExpr invocation.
    /// When SelectExpr is used inside another SelectExpr (nested SelectExpr),
    /// only the outermost SelectExpr should generate an interceptor.
    /// The inner SelectExpr will be converted to a regular Select call by the outer one.
    /// </summary>
    /// <param name="invocation">The invocation expression to check</param>
    /// <returns>True if the invocation is nested inside another SelectExpr</returns>
    public static bool IsNestedInsideAnotherSelectExpr(InvocationExpressionSyntax invocation)
    {
        // Walk up the syntax tree to find any ancestor that is also a SelectExpr invocation
        var current = invocation.Parent;
        while (current is not null)
        {
            // If we find a parent InvocationExpression that is also a SelectExpr, we are nested
            if (current is InvocationExpressionSyntax parentInvocation)
            {
                if (IsSelectExprInvocationSyntax(parentInvocation.Expression))
                {
                    return true;
                }
            }
            current = current.Parent;
        }
        return false;
    }
}
