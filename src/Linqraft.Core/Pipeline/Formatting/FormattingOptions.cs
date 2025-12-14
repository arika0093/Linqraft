namespace Linqraft.Core.Pipeline.Formatting;

/// <summary>
/// Options for code formatting.
/// </summary>
internal record FormattingOptions
{
    /// <summary>
    /// The number of spaces to use for indentation. Default is 4.
    /// </summary>
    public int IndentSize { get; init; } = 4;

    /// <summary>
    /// The newline character(s) to use. Default is LF ("\n").
    /// </summary>
    public string NewLine { get; init; } = "\n";

    /// <summary>
    /// Whether to normalize whitespace using Roslyn's NormalizeWhitespace. Default is true.
    /// </summary>
    public bool NormalizeWhitespace { get; init; } = true;

    /// <summary>
    /// Whether to remove trailing whitespace from each line. Default is true.
    /// </summary>
    public bool RemoveTrailingWhitespace { get; init; } = true;

    /// <summary>
    /// Gets the default formatting options.
    /// </summary>
    public static FormattingOptions Default { get; } = new();
}
