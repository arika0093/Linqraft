using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Linqraft.Core.AnalyzerHelpers;

/// <summary>
/// Provides helper methods for analyzing whether an anonymous object creation expression is located within a SelectExpr
/// method call with type arguments in a C# syntax tree.
/// </summary>
public class SelectExprContextHelper
{
    /// <summary>
    /// Determines whether the specified syntax node is located within a call to a SelectExpr method that has type
    /// arguments.
    /// </summary>
    public static bool IsInsideSelectExprCall(SyntaxNode? current)
    {
        while (current != null)
        {
            // Check if we're inside an invocation expression
            if (current is InvocationExpressionSyntax invocation)
            {
                // Check if it's a SelectExpr call with type arguments
                if (IsSelectExprWithTypeArguments(invocation.Expression))
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

    private static bool IsSelectExprWithTypeArguments(ExpressionSyntax expression)
    {
        // Get the method name and check for type arguments
        switch (expression)
        {
            // obj.SelectExpr<T, TDto>(...)
            case MemberAccessExpressionSyntax memberAccess:
                if (
                    memberAccess.Name.Identifier.Text == SelectExprHelper.MethodName
                    && memberAccess.Name is GenericNameSyntax genericName
                    && genericName.TypeArgumentList.Arguments.Count >= 2
                )
                {
                    return true;
                }
                break;

            // SelectExpr<T, TDto>(...) - unlikely but handle it
            case GenericNameSyntax genericIdentifier:
                if (
                    genericIdentifier.Identifier.Text == SelectExprHelper.MethodName
                    && genericIdentifier.TypeArgumentList.Arguments.Count >= 2
                )
                {
                    return true;
                }
                break;
        }

        return false;
    }
}
