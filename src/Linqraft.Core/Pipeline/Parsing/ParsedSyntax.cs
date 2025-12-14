using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Linqraft.Core.Pipeline.Parsing;

/// <summary>
/// Represents the result of parsing a syntax node.
/// Contains extracted lambda information, object creation expressions, and additional parsed data.
/// </summary>
internal record ParsedSyntax
{
    /// <summary>
    /// The original syntax node that was parsed.
    /// </summary>
    public required SyntaxNode OriginalNode { get; init; }

    /// <summary>
    /// The lambda parameter name, if a lambda expression was found.
    /// </summary>
    public string? LambdaParameterName { get; init; }

    /// <summary>
    /// The lambda body expression, if a lambda expression was found.
    /// </summary>
    public ExpressionSyntax? LambdaBody { get; init; }

    /// <summary>
    /// The object creation expression, if found (e.g., for anonymous types or explicit DTOs).
    /// </summary>
    public ObjectCreationExpressionSyntax? ObjectCreation { get; init; }

    /// <summary>
    /// Additional parsed data that can be stored by specific parsers.
    /// </summary>
    public Dictionary<string, object> ParsedData { get; init; } = new();
}
