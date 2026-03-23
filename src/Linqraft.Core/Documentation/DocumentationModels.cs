namespace Linqraft.Core.Documentation;

/// <summary>
/// Represents documentation.
/// </summary>
internal sealed record DocumentationInfo
{
    public string? Summary { get; init; }

    public string? Remarks { get; init; }

    public bool HasContent =>
        !string.IsNullOrWhiteSpace(Summary) || !string.IsNullOrWhiteSpace(Remarks);
}
