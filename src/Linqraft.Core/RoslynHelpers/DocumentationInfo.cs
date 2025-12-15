using System.Collections.Generic;

namespace Linqraft.Core.RoslynHelpers;

/// <summary>
/// Represents documentation information extracted from a symbol
/// </summary>
public record DocumentationInfo
{
    /// <summary>
    /// The summary text extracted from XML documentation, Comment attribute, or Display attribute
    /// </summary>
    public string? Summary { get; init; }

    /// <summary>
    /// The source expression reference (e.g., "TestData.Id")
    /// </summary>
    public string? SourceReference
    {
        get => field;
        set => field = value?.Replace(" ", "");
    }

    /// <summary>
    /// List of attribute names (e.g., ["Key", "Required", "StringLength(100)"])
    /// </summary>
    public List<string> Attributes { get; init; } = [];

    /// <summary>
    /// Returns true if there is any documentation to output
    /// </summary>
    public bool HasDocumentation =>
        !string.IsNullOrEmpty(Summary)
        || !string.IsNullOrEmpty(SourceReference)
        || Attributes.Count > 0;
}
