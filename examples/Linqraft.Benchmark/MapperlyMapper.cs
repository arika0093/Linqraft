using Riok.Mapperly.Abstractions;

namespace Linqraft.Benchmark;

/// <summary>
/// Mapperly source-generated mapper for benchmark.
/// Mapperly generates mapping code at compile time, providing near hand-written performance.
/// </summary>
[Mapper]
public static partial class MapperlyMapper
{
    /// <summary>
    /// Projects IQueryable of SampleClass to ManualSampleClassDto.
    /// Mapperly generates an expression tree for efficient database projection.
    /// </summary>
    public static partial IQueryable<ManualSampleClassDto> ProjectToDto(this IQueryable<SampleClass> query);

    /// <summary>
    /// Maps a single SampleClass to ManualSampleClassDto.
    /// Used internally by the IQueryable projection.
    /// </summary>
    [MapProperty(nameof(SampleClass.Child2) + "." + nameof(SampleChildClass2.Id), nameof(ManualSampleClassDto.Child2Id))]
    [MapProperty(nameof(SampleClass.Child2) + "." + nameof(SampleChildClass2.Quux), nameof(ManualSampleClassDto.Child2Quux))]
    [MapProperty(nameof(SampleClass.Child3) + "." + nameof(SampleChildClass3.Id), nameof(ManualSampleClassDto.Child3Id))]
    [MapProperty(nameof(SampleClass.Child3) + "." + nameof(SampleChildClass3.Corge), nameof(ManualSampleClassDto.Child3Corge))]
    [MapProperty(nameof(SampleClass.Child3) + "." + nameof(SampleChildClass3.Child) + "." + nameof(SampleChildChildClass2.Id), nameof(ManualSampleClassDto.Child3ChildId))]
    [MapProperty(nameof(SampleClass.Child3) + "." + nameof(SampleChildClass3.Child) + "." + nameof(SampleChildChildClass2.Grault), nameof(ManualSampleClassDto.Child3ChildGrault))]
    private static partial ManualSampleClassDto MapSampleClass(SampleClass source);

    /// <summary>
    /// Maps a single SampleChildClass to ManualSampleChildDto.
    /// </summary>
    [MapperIgnoreSource(nameof(SampleChildClass.SampleClassId))]
    [MapperIgnoreSource(nameof(SampleChildClass.SampleClass))]
    [MapProperty(nameof(SampleChildClass.Child) + "." + nameof(SampleChildChildClass.Id), nameof(ManualSampleChildDto.ChildId))]
    [MapProperty(nameof(SampleChildClass.Child) + "." + nameof(SampleChildChildClass.Qux), nameof(ManualSampleChildDto.ChildQux))]
    private static partial ManualSampleChildDto MapSampleChild(SampleChildClass source);
}
