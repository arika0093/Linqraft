using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Linqraft.Core.Pipeline.Transformation;

/// <summary>
/// Pipeline for applying multiple expression transformers in priority order.
/// Implements Chain of Responsibility pattern for expression transformation.
/// </summary>
internal class TransformationPipeline
{
    private readonly List<IExpressionTransformer> _transformers;

    /// <summary>
    /// Creates a new transformation pipeline with the specified transformers.
    /// </summary>
    /// <param name="transformers">The transformers to apply, ordered by priority</param>
    public TransformationPipeline(IEnumerable<IExpressionTransformer> transformers)
    {
        _transformers = transformers.OrderByDescending(t => t.Priority).ToList();
    }

    /// <summary>
    /// Creates a new transformation pipeline with the specified transformers.
    /// </summary>
    /// <param name="transformers">The transformers to apply, ordered by priority</param>
    public TransformationPipeline(params IExpressionTransformer[] transformers)
        : this((IEnumerable<IExpressionTransformer>)transformers)
    {
    }

    /// <summary>
    /// Transforms the expression by applying all applicable transformers.
    /// </summary>
    /// <param name="context">The transformation context</param>
    /// <returns>The transformed expression</returns>
    public ExpressionSyntax Transform(TransformContext context)
    {
        var current = context.Expression;

        foreach (var transformer in _transformers)
        {
            var updatedContext = context with { Expression = current };
            if (transformer.CanTransform(updatedContext))
            {
                current = transformer.Transform(updatedContext);
            }
        }

        return current;
    }
}
