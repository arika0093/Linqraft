using Microsoft.CodeAnalysis.CSharp.Syntax;
using Linqraft.Core.SyntaxHelpers;

namespace Linqraft.Core.Pipeline.Transformation;

/// <summary>
/// Transformer for null-conditional access expressions.
/// Converts x?.Property to ternary expressions for expression tree compatibility.
/// </summary>
internal class NullConditionalTransformer : IExpressionTransformer
{
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
        // The actual transformation is handled by the existing code generation logic
        // This transformer marks expressions that need null-conditional handling
        return context.Expression;
    }
}
