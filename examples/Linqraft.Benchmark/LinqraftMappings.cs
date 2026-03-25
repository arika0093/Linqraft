namespace Linqraft.Benchmark;

internal static partial class LinqraftMappingSampleClassDto
{
    [LinqraftMapping]
    internal static IQueryable<LinqraftDeclareSampleClassDto> ProjectToLinqraftDeclareSampleClassDto(
        this LinqraftMapper<SampleClass> source
    ) =>
        source.Select<LinqraftDeclareSampleClassDto>(s => new
        {
            s.Id,
            s.Foo,
            s.Bar,
            Childs = s.Childs.Select(c => new
            {
                c.Id,
                c.Baz,
                ChildId = c.Child?.Id,
                ChildQux = c.Child?.Qux,
            }),
            Child2Id = s.Child2?.Id,
            Child2Quux = s.Child2?.Quux,
            Child3Id = s.Child3.Id,
            Child3Corge = s.Child3.Corge,
            Child3ChildId = s.Child3?.Child?.Id,
            Child3ChildGrault = s.Child3?.Child?.Grault,
        });
}
