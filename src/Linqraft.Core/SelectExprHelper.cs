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
        // or is in the global namespace (for test scenarios)
        var containingNamespace = methodSymbol.ContainingNamespace;
        if (containingNamespace == null)
            return false;
            
        // Accept if in Linqraft namespace or global namespace
        if (!containingNamespace.ToDisplayString().StartsWith("Linqraft") 
            && !containingNamespace.IsGlobalNamespace)
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
}
