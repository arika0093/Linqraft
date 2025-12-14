using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace Linqraft.Core.Pipeline;

/// <summary>
/// Context object for passing data between pipeline stages.
/// Contains the target syntax node, semantic model, and metadata dictionary.
/// </summary>
internal record PipelineContext
{
    /// <summary>
    /// The target syntax node to be processed by the pipeline.
    /// </summary>
    public required SyntaxNode TargetNode { get; init; }

    /// <summary>
    /// The semantic model for type resolution and symbol analysis.
    /// </summary>
    public required SemanticModel SemanticModel { get; init; }

    /// <summary>
    /// Additional metadata that can be passed between pipeline stages.
    /// </summary>
    public Dictionary<string, object> Metadata { get; init; } = new();
}
