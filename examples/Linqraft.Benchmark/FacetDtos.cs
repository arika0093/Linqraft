using Facet;

namespace Linqraft.Benchmark;

/// <summary>
/// Facet-generated DTO for SampleChildChildClass (grandchild).
/// </summary>
[Facet(typeof(SampleChildChildClass), exclude: ["SampleChildClassId", "SampleChildClass"])]
public partial record FacetSampleChildChildDto;

/// <summary>
/// Facet-generated DTO for SampleChildClass.
/// Maps the child entity with nested grandchild.
/// </summary>
[Facet(
    typeof(SampleChildClass),
    exclude: ["SampleClassId", "SampleClass"],
    NestedFacets = [typeof(FacetSampleChildChildDto)]
)]
public partial record FacetSampleChildDto;

/// <summary>
/// Facet-generated DTO for SampleChildClass2 (optional second child).
/// </summary>
[Facet(typeof(SampleChildClass2), exclude: ["SampleClassId", "SampleClass"])]
public partial record FacetSampleChildClass2Dto;

/// <summary>
/// Facet-generated DTO for SampleChildChildClass2 (grandchild of Child3).
/// </summary>
[Facet(typeof(SampleChildChildClass2), exclude: ["SampleChildClass3Id", "SampleChildClass3"])]
public partial record FacetSampleChildChildClass2Dto;

/// <summary>
/// Facet-generated DTO for SampleChildClass3 (third child).
/// </summary>
[Facet(
    typeof(SampleChildClass3),
    exclude: ["SampleClassId", "SampleClass"],
    NestedFacets = [typeof(FacetSampleChildChildClass2Dto)]
)]
public partial record FacetSampleChildClass3Dto;

/// <summary>
/// Facet-generated DTO for SampleClass.
/// The Facet source generator creates the mapping logic at compile time.
/// Uses standard Facet NestedFacets for nested object mapping.
/// </summary>
[Facet(
    typeof(SampleClass),
    NestedFacets = [
        typeof(FacetSampleChildDto),
        typeof(FacetSampleChildClass2Dto),
        typeof(FacetSampleChildClass3Dto),
    ]
)]
public partial record FacetSampleClassDto;
