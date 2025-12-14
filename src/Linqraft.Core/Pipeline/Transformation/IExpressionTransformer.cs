using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Linqraft.Core.Pipeline.Transformation;

/// <summary>
/// Interface for expression transformers that convert expressions from one form to another.
/// Used in the Transformation phase of the pipeline.
/// </summary>
internal interface IExpressionTransformer
{
    /// <summary>
    /// Priority of this transformer (higher priority runs first).
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// Checks if this transformer can handle the given context.
    /// </summary>
    /// <param name="context">The transformation context</param>
    /// <returns>True if this transformer can handle the context</returns>
    bool CanTransform(TransformContext context);

    /// <summary>
    /// Transforms the expression in the context.
    /// </summary>
    /// <param name="context">The transformation context</param>
    /// <returns>The transformed expression</returns>
    ExpressionSyntax Transform(TransformContext context);
}

/// <summary>
/// Context for expression transformation operations.
/// </summary>
internal record TransformContext
{
    /// <summary>
    /// The expression to transform.
    /// </summary>
    public required ExpressionSyntax Expression { get; init; }

    /// <summary>
    /// The semantic model for type resolution.
    /// </summary>
    public required SemanticModel SemanticModel { get; init; }

    /// <summary>
    /// The expected type of the resulting expression.
    /// </summary>
    public required ITypeSymbol ExpectedType { get; init; }

    /// <summary>
    /// Additional metadata for the transformation.
    /// </summary>
    public Dictionary<string, object> Metadata { get; init; } = new();
}
