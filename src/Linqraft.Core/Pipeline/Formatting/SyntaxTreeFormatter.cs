using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Linqraft.Core.Pipeline.Formatting;

/// <summary>
/// Formatter that uses Roslyn's NormalizeWhitespace for syntax tree-based formatting.
/// This is the preferred formatter as it works directly with syntax nodes.
/// </summary>
internal class SyntaxTreeFormatter : ICodeFormatter
{
    /// <inheritdoc/>
    public string Format(string code, FormattingOptions? options = null)
    {
        // For backward compatibility, parse the string and format as syntax node
        var tree = CSharpSyntaxTree.ParseText(code);
        var root = tree.GetRoot();
        return FormatSyntaxNode(root, options);
    }

    /// <inheritdoc/>
    public string FormatSyntaxNode(SyntaxNode node, FormattingOptions? options = null)
    {
        options ??= FormattingOptions.Default;

        // Use Roslyn's NormalizeWhitespace for consistent formatting
        var normalized = node.NormalizeWhitespace(
            indentation: new string(' ', options.IndentSize),
            eol: options.NewLine,
            elasticTrivia: false
        );

        var result = normalized.ToFullString();

        // Remove trailing whitespace if requested
        if (options.RemoveTrailingWhitespace)
        {
            result = RemoveTrailingWhitespaceFromLines(result);
        }

        return result;
    }

    private static string RemoveTrailingWhitespaceFromLines(string code)
    {
        var lines = code.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            lines[i] = lines[i].TrimEnd();
        }
        return string.Join("\n", lines);
    }
}
