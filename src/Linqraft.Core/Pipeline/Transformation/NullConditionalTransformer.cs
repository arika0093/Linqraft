using Microsoft.CodeAnalysis.CSharp.Syntax;
using Linqraft.Core.SyntaxHelpers;

namespace Linqraft.Core.Pipeline.Transformation;

/// <summary>
/// Transformer that detects null-conditional access expressions.
/// This transformer marks expressions that contain null-conditional operators (?.),
/// allowing downstream processing to handle them appropriately.
/// Note: The actual transformation to ternary expressions is handled by existing
/// code generation logic in SelectExprInfo to maintain compatibility.
/// </summary>
internal class NullConditionalTransformer : IExpressionTransformer
{
    /// <summary>
    /// Metadata key indicating the expression has null-conditional access.
    /// </summary>
    public const string HasNullConditionalKey = "HasNullConditional";

    /// <inheritdoc/>
    public int Priority => 100;

    /// <inheritdoc/>
    public bool CanTransform(TransformContext context)
    {
        return NullConditionalHelper.HasNullConditionalAccess(context.Expression);
    }

    /// <inheritdoc/>
    public ExpressionSyntax Transform(TransformContext context)
    {
        // Mark the expression as having null-conditional access via metadata
        // The actual transformation is deferred to the code generation phase
        // where the full semantic context is available
        if (!context.Metadata.ContainsKey(HasNullConditionalKey))
        {
            context.Metadata[HasNullConditionalKey] = true;
        }
        return context.Expression;
    }
}
