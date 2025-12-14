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
        var lambda = LambdaParsingHelper.FindLambdaInArguments(context.TargetNode);
        if (lambda == null)
        {
            return new ParsedSyntax { OriginalNode = context.TargetNode };
        }

        var body = LambdaParsingHelper.GetLambdaBody(lambda);
        var objectCreation = FindObjectCreation(body);

        return new ParsedSyntax
        {
            OriginalNode = context.TargetNode,
            LambdaParameterName = LambdaParsingHelper.GetLambdaParameterName(lambda),
            LambdaBody = body,
            ObjectCreation = objectCreation
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
