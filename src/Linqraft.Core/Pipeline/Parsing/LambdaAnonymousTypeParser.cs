using System.Linq;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Linqraft.Core.SyntaxHelpers;

namespace Linqraft.Core.Pipeline.Parsing;

/// <summary>
/// Parser for lambda expressions with anonymous type bodies.
/// Extracts lambda parameter names and anonymous object creation expressions.
/// </summary>
internal class LambdaAnonymousTypeParser : ISyntaxParser
{
    /// <inheritdoc/>
    public ParsedSyntax Parse(PipelineContext context)
    {
        var lambda = FindLambdaInArguments(context.TargetNode);
        if (lambda == null)
        {
            return new ParsedSyntax { OriginalNode = context.TargetNode };
        }

        var paramName = GetLambdaParameterName(lambda);
        var body = GetLambdaBody(lambda);
        var anonymousType = FindAnonymousObjectCreation(body);

        return new ParsedSyntax
        {
            OriginalNode = context.TargetNode,
            LambdaParameterName = paramName,
            LambdaBody = body,
            ObjectCreation = null // Anonymous types use AnonymousObjectCreationExpressionSyntax not ObjectCreationExpressionSyntax
        };
    }

    private static LambdaExpressionSyntax? FindLambdaInArguments(Microsoft.CodeAnalysis.SyntaxNode node)
    {
        if (node is not InvocationExpressionSyntax invocation)
            return null;

        return invocation.ArgumentList.Arguments
            .Select(arg => arg.Expression)
            .OfType<LambdaExpressionSyntax>()
            .FirstOrDefault();
    }

    private static string? GetLambdaParameterName(LambdaExpressionSyntax lambda)
    {
        return lambda switch
        {
            SimpleLambdaExpressionSyntax simple => simple.Parameter.Identifier.Text,
            ParenthesizedLambdaExpressionSyntax paren when paren.ParameterList.Parameters.Count > 0
                => paren.ParameterList.Parameters[0].Identifier.Text,
            _ => null
        };
    }

    private static ExpressionSyntax? GetLambdaBody(LambdaExpressionSyntax lambda)
    {
        return lambda switch
        {
            SimpleLambdaExpressionSyntax simple => simple.Body as ExpressionSyntax,
            ParenthesizedLambdaExpressionSyntax paren => paren.Body as ExpressionSyntax,
            _ => null
        };
    }

    private static AnonymousObjectCreationExpressionSyntax? FindAnonymousObjectCreation(ExpressionSyntax? expression)
    {
        return expression as AnonymousObjectCreationExpressionSyntax;
    }
}
