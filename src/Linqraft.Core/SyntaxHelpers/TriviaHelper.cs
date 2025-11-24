using Linqraft.Core.Formatting;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Linqraft.Core.SyntaxHelpers;

/// <summary>
/// Helper methods for working with syntax trivia (whitespace, comments, etc.)
/// </summary>
public static class TriviaHelper
{
    /// <summary>
    /// Preserves both leading and trailing trivia from the original node to the updated node
    /// </summary>
    /// <typeparam name="T">The type of syntax node</typeparam>
    /// <param name="original">The original node with trivia to preserve</param>
    /// <param name="updated">The updated node to receive the trivia</param>
    /// <returns>The updated node with preserved trivia</returns>
    public static T PreserveTrivia<T>(T original, T updated)
        where T : SyntaxNode
    {
        return updated
            .WithLeadingTrivia(original.GetLeadingTrivia())
            .WithTrailingTrivia(original.GetTrailingTrivia());
    }

    /// <summary>
    /// Preserves only the leading trivia from the original token to the updated token
    /// </summary>
    /// <param name="original">The original token with trivia to preserve</param>
    /// <param name="updated">The updated token to receive the trivia</param>
    /// <returns>The updated token with preserved leading trivia</returns>
    public static SyntaxToken PreserveLeadingTrivia(SyntaxToken original, SyntaxToken updated)
    {
        return updated.WithLeadingTrivia(original.LeadingTrivia);
    }

    /// <summary>
    /// Preserves only the trailing trivia from the original token to the updated token
    /// </summary>
    /// <param name="original">The original token with trivia to preserve</param>
    /// <param name="updated">The updated token to receive the trivia</param>
    /// <returns>The updated token with preserved trailing trivia</returns>
    public static SyntaxToken PreserveTrailingTrivia(SyntaxToken original, SyntaxToken updated)
    {
        return updated.WithTrailingTrivia(original.TrailingTrivia);
    }

    /// <summary>
    /// Preserves both leading and trailing trivia from the original token to the updated token
    /// </summary>
    /// <param name="original">The original token with trivia to preserve</param>
    /// <param name="updated">The updated token to receive the trivia</param>
    /// <returns>The updated token with preserved trivia</returns>
    public static SyntaxToken PreserveTriviaToken(SyntaxToken original, SyntaxToken updated)
    {
        return updated
            .WithLeadingTrivia(original.LeadingTrivia)
            .WithTrailingTrivia(original.TrailingTrivia);
    }

    /// <summary>
    /// Normalizes whitespace in a syntax node using the standard end-of-line character
    /// </summary>
    /// <typeparam name="T">The type of syntax node</typeparam>
    /// <param name="node">The node to normalize</param>
    /// <returns>The normalized node</returns>
    public static T NormalizeWhitespace<T>(T node)
        where T : SyntaxNode
    {
        return node.NormalizeWhitespace(eol: CodeFormatter.DefaultNewLine);
    }

    /// <summary>
    /// Normalizes whitespace in a syntax node with a custom indentation string
    /// </summary>
    /// <typeparam name="T">The type of syntax node</typeparam>
    /// <param name="node">The node to normalize</param>
    /// <param name="indentation">The indentation string to use (e.g., "    " for 4 spaces)</param>
    /// <returns>The normalized node</returns>
    public static T NormalizeWhitespace<T>(T node, string indentation)
        where T : SyntaxNode
    {
        return node.NormalizeWhitespace(
            indentation: indentation,
            eol: CodeFormatter.DefaultNewLine,
            elasticTrivia: false
        );
    }

    /// <summary>
    /// Creates an end-of-line trivia using the standard newline character
    /// </summary>
    /// <returns>A syntax trivia representing an end-of-line</returns>
    public static SyntaxTrivia EndOfLine()
    {
        return SyntaxFactory.EndOfLine(CodeFormatter.DefaultNewLine);
    }

    /// <summary>
    /// Creates an end-of-line trivia using the line ending detected from the given syntax node
    /// </summary>
    /// <param name="node">The syntax node to detect line endings from</param>
    /// <returns>A syntax trivia representing an end-of-line</returns>
    public static SyntaxTrivia EndOfLine(SyntaxNode node)
    {
        var eol = DetectLineEnding(node);
        return SyntaxFactory.EndOfLine(eol);
    }

    /// <summary>
    /// Detects the line ending used in the given syntax node by examining its trivia
    /// </summary>
    /// <param name="node">The syntax node to examine</param>
    /// <returns>The detected line ending string ("\r\n" or "\n")</returns>
    public static string DetectLineEnding(SyntaxNode node)
    {
        // Search through all trivia in the tree to find the first end-of-line
        var root = node.SyntaxTree?.GetRoot() ?? node;
        foreach (var trivia in root.DescendantTrivia())
        {
            if (trivia.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.EndOfLineTrivia))
            {
                var text = trivia.ToFullString();
                if (text == "\r\n")
                    return "\r\n";
                if (text == "\n")
                    return "\n";
            }
        }

        // Default to LF if no line ending found
        return CodeFormatter.DefaultNewLine;
    }

    /// <summary>
    /// Creates a whitespace trivia with the specified number of spaces
    /// </summary>
    /// <param name="count">The number of spaces</param>
    /// <returns>A syntax trivia representing whitespace</returns>
    public static SyntaxTrivia Whitespace(int count)
    {
        return SyntaxFactory.Whitespace(new string(' ', count));
    }

    /// <summary>
    /// Creates an indentation trivia using the standard indentation size
    /// </summary>
    /// <param name="level">The indentation level (multiplied by IndentSize)</param>
    /// <returns>A syntax trivia representing indentation</returns>
    public static SyntaxTrivia Indentation(int level = 1)
    {
        return SyntaxFactory.Whitespace(new string(' ', CodeFormatter.IndentSize * level));
    }
}
