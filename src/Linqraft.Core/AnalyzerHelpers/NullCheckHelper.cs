using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Linqraft.Core.AnalyzerHelpers;

/// <summary>
/// Helper methods for null checking and null literal detection
/// </summary>
public static class NullCheckHelper
{
    /// <summary>
    /// Determines if an expression is a null literal expression.
    /// </summary>
    /// <param name="expr">The expression to check</param>
    /// <returns>True if the expression is a null literal</returns>
    public static bool IsNullLiteral(ExpressionSyntax expr)
    {
        return expr is LiteralExpressionSyntax literal
            && literal.Kind() == SyntaxKind.NullLiteralExpression;
    }

    /// <summary>
    /// Determines if an expression is null or a nullable cast to null (e.g., (Type?)null).
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
    /// Removes nullable cast from an expression if present (e.g., (Type?)expr becomes expr).
    /// </summary>
    /// <param name="expr">The expression to process</param>
    /// <returns>The expression with nullable cast removed, or the original expression if no cast present</returns>
    public static ExpressionSyntax RemoveNullableCast(ExpressionSyntax expr)
    {
        // Remove (Type?) cast if present
        if (expr is CastExpressionSyntax cast && cast.Type is NullableTypeSyntax)
        {
            return cast.Expression;
        }

        return expr;
    }
}
