using AutoMapper;
using Microsoft.Extensions.Logging.Abstractions;

namespace Linqraft.Benchmark;

/// <summary>
/// AutoMapper configuration for benchmark.
/// Maps entities to DTOs using AutoMapper's standard profile-based configuration.
/// </summary>
public class AutoMapperProfile : Profile
{
    public AutoMapperProfile()
    {
        // Map SampleClass to ManualSampleClassDto
        CreateMap<SampleClass, ManualSampleClassDto>()
            .ForMember(
                dest => dest.Child2Id,
                opt => opt.MapFrom(src => src.Child2 != null ? src.Child2.Id : (int?)null)
            )
            .ForMember(
                dest => dest.Child2Quux,
                opt => opt.MapFrom(src => src.Child2 != null ? src.Child2.Quux : null)
            )
            .ForMember(dest => dest.Child3Id, opt => opt.MapFrom(src => src.Child3.Id))
            .ForMember(dest => dest.Child3Corge, opt => opt.MapFrom(src => src.Child3.Corge))
            .ForMember(
                dest => dest.Child3ChildId,
                opt =>
                    opt.MapFrom(src =>
                        src.Child3 != null && src.Child3.Child != null
                            ? src.Child3.Child.Id
                            : (int?)null
                    )
            )
            .ForMember(
                dest => dest.Child3ChildGrault,
                opt =>
                    opt.MapFrom(src =>
                        src.Child3 != null && src.Child3.Child != null
                            ? src.Child3.Child.Grault
                            : null
                    )
            );

        // Map SampleChildClass to ManualSampleChildDto
        CreateMap<SampleChildClass, ManualSampleChildDto>()
            .ForMember(
                dest => dest.ChildId,
                opt => opt.MapFrom(src => src.Child != null ? src.Child.Id : (int?)null)
            )
            .ForMember(
                dest => dest.ChildQux,
                opt => opt.MapFrom(src => src.Child != null ? src.Child.Qux : null)
            );
    }
}

/// <summary>
/// AutoMapper configuration provider for benchmark usage.
/// </summary>
public static class AutoMapperConfig
{
    private static IMapper? _mapper;

    /// <summary>
    /// Gets the configured mapper instance.
    /// </summary>
    public static IMapper Mapper => _mapper ??= CreateMapper();

    /// <summary>
    /// Creates a new AutoMapper instance with the benchmark profile.
    /// </summary>
    public static IMapper CreateMapper()
    {
        var config = new MapperConfiguration(
            cfg => cfg.AddProfile<AutoMapperProfile>(),
            NullLoggerFactory.Instance
        );
        return config.CreateMapper();
    }

    /// <summary>
    /// Gets the mapper configuration for ProjectTo operations.
    /// </summary>
    public static IConfigurationProvider Configuration => Mapper.ConfigurationProvider;
}
