using Mapster;

namespace Linqraft.Benchmark;

/// <summary>
/// Mapster configuration for benchmark.
/// Configures type mappings for entity to DTO projections.
/// </summary>
public static class MapsterConfig
{
    private static bool _configured;

    /// <summary>
    /// Configures Mapster type mappings.
    /// Call this once before using Mapster projections.
    /// </summary>
    public static void Configure()
    {
        if (_configured)
            return;

        // Configure SampleClass to ManualSampleClassDto mapping
        TypeAdapterConfig<SampleClass, ManualSampleClassDto>
            .NewConfig()
            .Map(dest => dest.Child2Id, src => src.Child2 != null ? src.Child2.Id : (int?)null)
            .Map(dest => dest.Child2Quux, src => src.Child2 != null ? src.Child2.Quux : null)
            .Map(dest => dest.Child3Id, src => src.Child3.Id)
            .Map(dest => dest.Child3Corge, src => src.Child3.Corge)
            .Map(
                dest => dest.Child3ChildId,
                src =>
                    src.Child3 != null && src.Child3.Child != null
                        ? src.Child3.Child.Id
                        : (int?)null
            )
            .Map(
                dest => dest.Child3ChildGrault,
                src =>
                    src.Child3 != null && src.Child3.Child != null ? src.Child3.Child.Grault : null
            );

        // Configure SampleChildClass to ManualSampleChildDto mapping
        TypeAdapterConfig<SampleChildClass, ManualSampleChildDto>
            .NewConfig()
            .Map(dest => dest.ChildId, src => src.Child != null ? src.Child.Id : (int?)null)
            .Map(dest => dest.ChildQux, src => src.Child != null ? src.Child.Qux : null);

        _configured = true;
    }
}
