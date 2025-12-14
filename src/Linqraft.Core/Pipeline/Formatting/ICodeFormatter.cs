using Microsoft.CodeAnalysis;

namespace Linqraft.Core.Pipeline.Formatting;

/// <summary>
/// Interface for code formatters that format generated code.
/// Used in the Formatting phase of the pipeline.
/// </summary>
internal interface ICodeFormatter
{
    /// <summary>
    /// Formats the given code string.
    /// </summary>
    /// <param name="code">The code to format</param>
    /// <param name="options">Formatting options</param>
    /// <returns>The formatted code</returns>
    string Format(string code, FormattingOptions? options = null);

    /// <summary>
    /// Formats the given syntax node directly.
    /// This is preferred over string formatting as it preserves semantic information.
    /// </summary>
    /// <param name="node">The syntax node to format</param>
    /// <param name="options">Formatting options</param>
    /// <returns>The formatted code as a string</returns>
    string FormatSyntaxNode(SyntaxNode node, FormattingOptions? options = null);
}
