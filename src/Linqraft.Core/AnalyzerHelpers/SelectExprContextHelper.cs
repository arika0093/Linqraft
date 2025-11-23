using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Linqraft.Core.AnalyzerHelpers;

/// <summary>
/// Provides helper methods for analyzing whether a syntax node is located within a SelectExpr
/// method call in a C# syntax tree.
/// </summary>
public class SelectExprContextHelper
{
    /// <summary>
    /// Determines whether the specified syntax node is located within a call to a SelectExpr method.
    /// </summary>
    /// <param name="context">The syntax node analysis context containing the node and semantic model</param>
    /// <returns>True if the node is inside a SelectExpr call, false otherwise</returns>
    public static bool IsInsideSelectExprCall(SyntaxNodeAnalysisContext context)
    {
        return IsInsideSelectExprCall(context.Node, context.SemanticModel);
    }

    /// <summary>
    /// Determines whether the specified syntax node is located within a call to a SelectExpr method.
    /// </summary>
    /// <param name="node">The syntax node to check</param>
    /// <param name="semanticModel">The semantic model for semantic analysis</param>
    /// <returns>True if the node is inside a SelectExpr call, false otherwise</returns>
    public static bool IsInsideSelectExprCall(SyntaxNode? node, SemanticModel semanticModel)
    {
        var current = node;
        while (current != null)
        {
            // Check if we're inside an invocation expression
            if (current is InvocationExpressionSyntax invocation)
            {
                // Use semantic analysis to check if it's a SelectExpr call
                if (SelectExprHelper.IsSelectExprInvocation(invocation, semanticModel))
                {
                    return true;
                }
            }

            // Stop at method/property declarations
            if (current is MemberDeclarationSyntax)
            {
                break;
            }
            current = current.Parent;
        }
        return false;
    }

    /// <summary>
    /// Determines whether the specified syntax node is located within a call to a SelectExpr method
    /// that has explicit type arguments (e.g., SelectExpr&lt;T, TDto&gt;).
    /// </summary>
    /// <param name="node">The syntax node to check</param>
    /// <param name="semanticModel">The semantic model for semantic analysis</param>
    /// <returns>True if the node is inside a SelectExpr call with type arguments, false otherwise</returns>
    public static bool IsInsideSelectExprCallWithTypeArgs(SyntaxNode? node, SemanticModel semanticModel)
    {
        var current = node;
        while (current != null)
        {
            // Check if we're inside an invocation expression
            if (current is InvocationExpressionSyntax invocation)
            {
                // First check if it's a SelectExpr call using semantic analysis
                if (SelectExprHelper.IsSelectExprInvocation(invocation, semanticModel))
                {
                    // Then check for type arguments in the syntax
                    if (HasTypeArguments(invocation.Expression))
                    {
                        return true;
                    }
                }
            }

            // Stop at method/property declarations
            if (current is MemberDeclarationSyntax)
            {
                break;
            }
            current = current.Parent;
        }
        return false;
    }

    private static bool HasTypeArguments(ExpressionSyntax expression)
    {
        switch (expression)
        {
            // obj.SelectExpr<T, TDto>(...)
            case MemberAccessExpressionSyntax memberAccess:
                return memberAccess.Name is GenericNameSyntax;

            // SelectExpr<T, TDto>(...) - unlikely but handle it
            case GenericNameSyntax:
                return true;

            default:
                return false;
        }
    }
}
