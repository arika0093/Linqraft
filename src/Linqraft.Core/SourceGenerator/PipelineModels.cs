using Linqraft.Core.Collections;
using Linqraft.Core.Configuration;

namespace Linqraft.SourceGenerator;

/// <summary>
/// Represents generated source file.
/// </summary>
internal sealed record GeneratedSourceFileModel
{
    public required string HintName { get; init; }

    public required string SourceText { get; init; }
}

/// <summary>
/// Represents generated source set.
/// </summary>
internal sealed record GeneratedSourceSetModel
{
    public required EquatableArray<GeneratedSourceFileModel> Sources { get; init; }
}

/// <summary>
/// Represents owned generated source.
/// </summary>
internal abstract record OwnedGeneratedSourceModel
{
    public required string HintName { get; init; }

    public required string OwnerHintName { get; init; }

    public required EquatableArray<GeneratedDtoModel> GeneratedDtos { get; init; }
}

/// <summary>
/// Represents projection owned generated source.
/// </summary>
internal sealed record ProjectionOwnedGeneratedSourceModel : OwnedGeneratedSourceModel
{
    public required ProjectionRequest Request { get; init; }
}

/// <summary>
/// Represents mapping owned generated source.
/// </summary>
internal sealed record MappingOwnedGeneratedSourceModel : OwnedGeneratedSourceModel
{
    public required MappingRequest Request { get; init; }
}

/// <summary>
/// Represents object generation owned generated source.
/// </summary>
internal sealed record ObjectGenerationOwnedGeneratedSourceModel : OwnedGeneratedSourceModel
{
    public required ObjectGenerationRequest Request { get; init; }
}

/// <summary>
/// Represents generated source build context.
/// </summary>
internal sealed record GeneratedSourceBuildContextModel
{
    public required EquatableArray<OwnedGeneratedSourceModel> OwnedSources { get; init; }

    public required LinqraftConfiguration Configuration { get; init; }
}
