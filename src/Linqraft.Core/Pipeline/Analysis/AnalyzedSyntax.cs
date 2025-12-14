using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace Linqraft.Core.Pipeline.Analysis;

/// <summary>
/// Represents the result of semantic analysis of parsed syntax.
/// Contains type information, nullability analysis, and captured variable information.
/// </summary>
internal record AnalyzedSyntax
{
    /// <summary>
    /// The original parsed syntax that was analyzed.
    /// </summary>
    public required Parsing.ParsedSyntax ParsedSyntax { get; init; }

    /// <summary>
    /// The source type symbol (e.g., the entity type in a Select expression).
    /// </summary>
    public ITypeSymbol? SourceType { get; init; }

    /// <summary>
    /// The target type symbol (e.g., the DTO type in a Select expression).
    /// </summary>
    public ITypeSymbol? TargetType { get; init; }

    /// <summary>
    /// Variables that need to be captured in the expression.
    /// </summary>
    public IReadOnlyCollection<string> CapturedVariables { get; init; } = new HashSet<string>();

    /// <summary>
    /// Additional analyzed data.
    /// </summary>
    public Dictionary<string, object> AnalyzedData { get; init; } = new();
}
