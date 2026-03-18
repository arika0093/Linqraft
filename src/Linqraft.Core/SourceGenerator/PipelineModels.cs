using Linqraft.Core.Collections;
using Linqraft.Core.Configuration;

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

internal abstract record OwnedGeneratedSourceModel
{
    public required string HintName { get; init; }

    public required string OwnerHintName { get; init; }

    public required EquatableArray<GeneratedDtoModel> GeneratedDtos { get; init; }
}

internal sealed record ProjectionOwnedGeneratedSourceModel : OwnedGeneratedSourceModel
{
    public required ProjectionRequest Request { get; init; }
}

internal sealed record MappingOwnedGeneratedSourceModel : OwnedGeneratedSourceModel
{
    public required MappingRequest Request { get; init; }
}

internal sealed record ObjectGenerationOwnedGeneratedSourceModel : OwnedGeneratedSourceModel
{
    public required ObjectGenerationRequest Request { get; init; }
}

internal sealed record GeneratedSourceBuildContextModel
{
    public required EquatableArray<OwnedGeneratedSourceModel> OwnedSources { get; init; }

    public required LinqraftConfiguration Configuration { get; init; }
}
