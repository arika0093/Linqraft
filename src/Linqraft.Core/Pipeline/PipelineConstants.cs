namespace Linqraft.Core.Pipeline;

/// <summary>
/// Contains constants used throughout the pipeline generation process.
/// </summary>
internal static class PipelineConstants
{
    /// <summary>
    /// Prefix for hash-generated namespaces.
    /// DTOs in namespaces starting with this prefix are placed in a shared file
    /// to enable deduplication across multiple generation groups.
    /// </summary>
    public const string HashNamespacePrefix = "LinqraftGenerated_";
}
