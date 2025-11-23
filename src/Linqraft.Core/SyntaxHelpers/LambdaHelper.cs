using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Linqraft.Core.SyntaxHelpers;

/// <summary>
/// Helper methods for working with lambda expressions
/// </summary>
public static class LambdaHelper
{
    /// <summary>
    /// Gets the first parameter name from a lambda expression
    /// </summary>
    /// <param name="lambda">The lambda expression</param>
    /// <returns>The name of the first parameter, or "s" as fallback</returns>
    public static string GetLambdaParameterName(LambdaExpressionSyntax lambda)
    {
        return lambda switch
        {
            SimpleLambdaExpressionSyntax simple => simple.Parameter.Identifier.Text,
            ParenthesizedLambdaExpressionSyntax paren
                when paren.ParameterList.Parameters.Count > 0 => paren
                .ParameterList
                .Parameters[0]
                .Identifier
                .Text,
            _ => "s", // fallback to default
        };
    }

    /// <summary>
    /// Gets all parameter names from a lambda expression
    /// </summary>
    /// <param name="lambda">The lambda expression</param>
    /// <returns>An immutable set of parameter names</returns>
    public static ImmutableHashSet<string> GetLambdaParameterNames(LambdaExpressionSyntax lambda)
    {
        return lambda switch
        {
            SimpleLambdaExpressionSyntax simple => ImmutableHashSet.Create(
                simple.Parameter.Identifier.Text
            ),
            ParenthesizedLambdaExpressionSyntax paren => paren
                .ParameterList.Parameters.Select(p => p.Identifier.Text)
                .ToImmutableHashSet(),
            _ => ImmutableHashSet<string>.Empty,
        };
    }

    /// <summary>
    /// Gets the body expression from a lambda expression
    /// </summary>
    /// <param name="lambda">The lambda expression</param>
    /// <returns>The body expression, or null if not found</returns>
    public static ExpressionSyntax? GetLambdaBody(LambdaExpressionSyntax lambda)
    {
        return lambda switch
        {
            SimpleLambdaExpressionSyntax simple => simple.Body as ExpressionSyntax,
            ParenthesizedLambdaExpressionSyntax paren => paren.Body as ExpressionSyntax,
            _ => null,
        };
    }

    /// <summary>
    /// Finds a lambda expression in an argument list
    /// </summary>
    /// <param name="argumentList">The argument list to search</param>
    /// <returns>The lambda expression, or null if not found</returns>
    public static LambdaExpressionSyntax? FindLambdaInArguments(ArgumentListSyntax argumentList)
    {
        foreach (var arg in argumentList.Arguments)
        {
            if (arg.Expression is LambdaExpressionSyntax lambda)
            {
                return lambda;
            }
        }

        return null;
    }

    /// <summary>
    /// Finds an anonymous object creation expression in an argument list
    /// </summary>
    /// <param name="argumentList">The argument list to search</param>
    /// <returns>The anonymous object creation expression, or null if not found</returns>
    public static AnonymousObjectCreationExpressionSyntax? FindAnonymousTypeInArguments(
        ArgumentListSyntax argumentList
    )
    {
        foreach (var arg in argumentList.Arguments)
        {
            if (arg.Expression is LambdaExpressionSyntax lambda)
            {
                var body = GetLambdaBody(lambda);
                if (body is AnonymousObjectCreationExpressionSyntax anonymousObject)
                {
                    return anonymousObject;
                }
            }
        }

        return null;
    }
}
