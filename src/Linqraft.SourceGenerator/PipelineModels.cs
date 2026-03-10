using Linqraft.Core.Collections;

namespace Linqraft.SourceGenerator;

internal sealed record GeneratedSourceFileModel
{
    public required string HintName { get; init; }

    public required string SourceText { get; init; }
}

internal sealed record GeneratedSourceSetModel
{
    public required EquatableArray<GeneratedSourceFileModel> Sources { get; init; }
}
