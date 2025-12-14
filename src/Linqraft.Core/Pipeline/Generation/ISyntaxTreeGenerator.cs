using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace Linqraft.Core.Pipeline.Generation;

/// <summary>
/// Interface for syntax tree generators that produce syntax nodes from analyzed data.
/// Used in the Generation phase of the pipeline.
/// Generates SyntaxNode instead of string to preserve semantic information.
/// </summary>
internal interface ISyntaxTreeGenerator
{
    /// <summary>
    /// Generates a syntax tree from the generation context.
    /// </summary>
    /// <param name="context">The generation context containing all necessary information</param>
    /// <returns>The generated syntax node</returns>
    SyntaxNode Generate(GenerationContext context);
}

/// <summary>
/// Context for code generation operations.
/// </summary>
internal record GenerationContext
{
    /// <summary>
    /// The semantic model for type resolution.
    /// </summary>
    public required SemanticModel SemanticModel { get; init; }

    /// <summary>
    /// The DTO structure to generate code for.
    /// </summary>
    public required DtoStructure Structure { get; init; }

    /// <summary>
    /// The Linqraft configuration.
    /// </summary>
    public required LinqraftConfiguration Configuration { get; init; }

    /// <summary>
    /// Additional metadata for the generation.
    /// </summary>
    public Dictionary<string, object> Metadata { get; init; } = new();
}
