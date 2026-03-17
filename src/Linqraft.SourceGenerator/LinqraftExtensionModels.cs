namespace Linqraft.SourceGenerator;

/// <summary>
/// Mirrors the embedded <c>LinqraftExtensionBehavior</c> enum for use within the source generator.
/// </summary>
internal enum LinqraftExtensionBehaviorKind
{
    PassThrough = 0,
    NullConditionalNavigation = 1,
    CastToFirstTypeArgument = 2,
}

/// <summary>
/// Represents a collected Linqraft extension definition from user or library code.
/// </summary>
internal sealed record LinqraftExtensionMethodInfo
{
    public required string MethodName { get; init; }

    public string? GenerateNamespace { get; init; }

    public required LinqraftExtensionBehaviorKind Behavior { get; init; }

    /// <summary>
    /// When <c>true</c>, the stub extension method is already provided by the declaring library
    /// and the source generator should NOT emit a duplicate stub.
    /// </summary>
    public bool StubAlreadyProvided { get; init; }
}
