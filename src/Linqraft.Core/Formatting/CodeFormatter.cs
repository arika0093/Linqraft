using System.Linq;

namespace Linqraft.Core.Formatting;

/// <summary>
/// Provides consistent code formatting utilities for generated code
/// </summary>
public static class CodeFormatter
{
    /// <summary>
    /// The default newline character to use in generated code
    /// Using \n for consistency across platforms
    /// </summary>
    public const string DefaultNewLine = "\n";

    /// <summary>
    /// The number of spaces per indentation level
    /// </summary>
    public const int IndentSize = 4;

    /// <summary>
    /// Generates an indentation string for the specified level
    /// </summary>
    /// <param name="level">The indentation level (0, 1, 2, ...)</param>
    /// <returns>A string containing the appropriate number of spaces</returns>
    public static string Indent(int level)
    {
        return new string(' ', level * IndentSize);
    }

    /// <summary>
    /// Generates an indentation string with a specific number of spaces
    /// </summary>
    /// <param name="spaces">The number of spaces</param>
    /// <returns>A string containing the specified number of spaces</returns>
    public static string IndentSpaces(int spaces)
    {
        return new string(' ', spaces);
    }

    /// <summary>
    /// Indents each line of the given code by the specified number of spaces
    /// </summary>
    /// <param name="code">The code to indent</param>
    /// <param name="indentSpaces">The number of spaces to add to each line</param>
    /// <returns>The indented code</returns>
    public static string IndentCode(string code, int indentSpaces)
    {
        if (string.IsNullOrEmpty(code))
            return code;

        var indent = new string(' ', indentSpaces);
        var indentedLines = code.Split('\n')
            .Select(line => string.IsNullOrWhiteSpace(line) ? line : indent + line);
        return string.Join(DefaultNewLine, indentedLines);
    }

    /// <summary>
    /// Indents each line of the given code by the specified indentation level
    /// </summary>
    /// <param name="code">The code to indent</param>
    /// <param name="level">The indentation level</param>
    /// <returns>The indented code</returns>
    public static string IndentCodeByLevel(string code, int level)
    {
        return IndentCode(code, level * IndentSize);
    }

    /// <summary>
    /// Joins multiple strings with newlines
    /// </summary>
    /// <param name="lines">The lines to join</param>
    /// <returns>A string with lines joined by newlines</returns>
    public static string JoinLines(params string[] lines)
    {
        return string.Join(DefaultNewLine, lines);
    }

    /// <summary>
    /// Normalizes newlines in the given code to use the default newline character
    /// </summary>
    /// <param name="code">The code to normalize</param>
    /// <returns>The code with normalized newlines</returns>
    public static string NormalizeNewLines(string code)
    {
        if (string.IsNullOrEmpty(code))
            return code;

        // Replace \r\n with \n, then any remaining \r with \n
        return code.Replace("\r\n", DefaultNewLine).Replace("\r", DefaultNewLine);
    }
}
