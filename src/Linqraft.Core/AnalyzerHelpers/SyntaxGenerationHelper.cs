using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Linqraft.Core.AnalyzerHelpers;

/// <summary>
/// Helper methods for generating syntax nodes in code fixes
/// </summary>
public static class SyntaxGenerationHelper
{
    /// <summary>
    /// Creates a typed SelectExpr call with type arguments from an existing Select expression.
    /// Converts "obj.Select(...)" to "obj.SelectExpr&lt;TSource, TDto&gt;(...)"
    /// </summary>
    /// <param name="expression">The original Select expression</param>
    /// <param name="sourceTypeName">The source type name (TSource)</param>
    /// <param name="dtoName">The DTO type name (TDto)</param>
    /// <returns>The new SelectExpr expression with type arguments, or null if the expression is not a member access</returns>
    public static ExpressionSyntax? CreateTypedSelectExpr(
        ExpressionSyntax expression,
        string sourceTypeName,
        string dtoName
    )
    {
        if (expression is not MemberAccessExpressionSyntax memberAccess)
            return null;

        // Create type arguments
        var typeArguments = SyntaxFactory.TypeArgumentList(
            SyntaxFactory.SeparatedList<TypeSyntax>(
                new[]
                {
                    SyntaxFactory.ParseTypeName(sourceTypeName),
                    SyntaxFactory.ParseTypeName(dtoName),
                }
            )
        );

        // Create the generic name
        var genericName = SyntaxFactory.GenericName(
            SyntaxFactory.Identifier("SelectExpr"),
            typeArguments
        );

        // Create the new member access expression, preserving the original dot token's trivia
        return SyntaxFactory.MemberAccessExpression(
            SyntaxKind.SimpleMemberAccessExpression,
            memberAccess.Expression,
            memberAccess.OperatorToken,
            genericName
        );
    }
}
