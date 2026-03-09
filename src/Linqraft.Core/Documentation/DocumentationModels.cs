namespace Linqraft.Core.Documentation;

internal sealed record DocumentationInfo
{
    public string? Summary { get; init; }

    public string? Remarks { get; init; }

    public bool HasContent =>
        !string.IsNullOrWhiteSpace(Summary) || !string.IsNullOrWhiteSpace(Remarks);
}
