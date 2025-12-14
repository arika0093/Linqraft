using Microsoft.CodeAnalysis.CSharp.Syntax;

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
        var lambda = LambdaParsingHelper.FindLambdaInArguments(context.TargetNode);
        if (lambda == null)
        {
            return new ParsedSyntax { OriginalNode = context.TargetNode };
        }

        var paramName = LambdaParsingHelper.GetLambdaParameterName(lambda);
        var body = LambdaParsingHelper.GetLambdaBody(lambda);
        var anonymousType = FindAnonymousObjectCreation(body);

        return new ParsedSyntax
        {
            OriginalNode = context.TargetNode,
            LambdaParameterName = paramName,
            LambdaBody = body,
            ObjectCreation = null // Anonymous types use AnonymousObjectCreationExpressionSyntax not ObjectCreationExpressionSyntax
        };
    }

    private static AnonymousObjectCreationExpressionSyntax? FindAnonymousObjectCreation(ExpressionSyntax? expression)
    {
        return expression as AnonymousObjectCreationExpressionSyntax;
    }
}
