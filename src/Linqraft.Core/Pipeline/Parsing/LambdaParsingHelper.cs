using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Linqraft.Core.Pipeline.Parsing;

/// <summary>
/// Shared utility methods for lambda expression parsing.
/// </summary>
internal static class LambdaParsingHelper
{
    /// <summary>
    /// Finds a lambda expression in the arguments of an invocation.
    /// </summary>
    /// <param name="node">The syntax node (expected to be an InvocationExpressionSyntax)</param>
    /// <returns>The first lambda expression found, or null</returns>
    public static LambdaExpressionSyntax? FindLambdaInArguments(SyntaxNode node)
    {
        if (node is not InvocationExpressionSyntax invocation)
            return null;

        return invocation.ArgumentList.Arguments
            .Select(arg => arg.Expression)
            .OfType<LambdaExpressionSyntax>()
            .FirstOrDefault();
    }

    /// <summary>
    /// Gets the parameter name from a lambda expression.
    /// </summary>
    /// <param name="lambda">The lambda expression</param>
    /// <returns>The parameter name, or null if not found</returns>
    public static string? GetLambdaParameterName(LambdaExpressionSyntax? lambda)
    {
        return lambda switch
        {
            SimpleLambdaExpressionSyntax simple => simple.Parameter.Identifier.Text,
            ParenthesizedLambdaExpressionSyntax paren when paren.ParameterList.Parameters.Count > 0
                => paren.ParameterList.Parameters[0].Identifier.Text,
            _ => null
        };
    }

    /// <summary>
    /// Gets the body expression from a lambda expression.
    /// </summary>
    /// <param name="lambda">The lambda expression</param>
    /// <returns>The body expression, or null if not an expression body</returns>
    public static ExpressionSyntax? GetLambdaBody(LambdaExpressionSyntax? lambda)
    {
        return lambda switch
        {
            SimpleLambdaExpressionSyntax simple => simple.Body as ExpressionSyntax,
            ParenthesizedLambdaExpressionSyntax paren => paren.Body as ExpressionSyntax,
            _ => null
        };
    }
}
