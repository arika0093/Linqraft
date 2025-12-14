using System.Linq;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Linqraft.Core.Pipeline.Parsing;

/// <summary>
/// Parser for object creation expressions in lambda bodies.
/// Extracts explicit DTO instantiation patterns like new UserDto { ... }.
/// </summary>
internal class ObjectCreationParser : ISyntaxParser
{
    /// <inheritdoc/>
    public ParsedSyntax Parse(PipelineContext context)
    {
        var lambda = FindLambdaInArguments(context.TargetNode);
        if (lambda == null)
        {
            return new ParsedSyntax { OriginalNode = context.TargetNode };
        }

        var body = GetLambdaBody(lambda);
        var objectCreation = FindObjectCreation(body);

        return new ParsedSyntax
        {
            OriginalNode = context.TargetNode,
            LambdaParameterName = GetLambdaParameterName(lambda),
            LambdaBody = body,
            ObjectCreation = objectCreation
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

    private static ObjectCreationExpressionSyntax? FindObjectCreation(ExpressionSyntax? expression)
    {
        if (expression is ObjectCreationExpressionSyntax objectCreation)
            return objectCreation;

        // Also check for implicit object creation (new ClassName { ... })
        return expression?.DescendantNodesAndSelf()
            .OfType<ObjectCreationExpressionSyntax>()
            .FirstOrDefault();
    }
}
