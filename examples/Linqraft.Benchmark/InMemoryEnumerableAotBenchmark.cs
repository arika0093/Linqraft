using AutoMapper;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Attributes.Filters;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Order;
using Mapster;

namespace Linqraft.Benchmark;

[MemoryDiagnoser]
[ShortRunJob(RuntimeMoniker.NativeAot10_0)]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
public partial class InMemoryEnumerableAotBenchmark
{
    private List<SampleClass> _data = null!;

    [Params(100)]
    public int DataCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        // Create in-memory test data
        _data = new List<SampleClass>();
        for (int i = 0; i < DataCount; i++)
        {
            var sampleEntity = new SampleClass
            {
                Id = i + 1,
                Foo = $"FooValue{i}",
                Bar = $"BarValue{i}",
                Childs =
                [
                    new()
                    {
                        Id = i * 2 + 1,
                        Baz = $"BazValue{i}-1",
                        Child = new() { Id = i * 2 + 1, Qux = $"QuxValue{i}-1" },
                    },
                    new()
                    {
                        Id = i * 2 + 2,
                        Baz = $"BazValue{i}-2",
                        Child = new() { Id = i * 2 + 2, Qux = $"QuxValue{i}-2" },
                    },
                ],
                Child2 = i % 2 == 0 ? new() { Id = i + 1, Quux = $"QuuxValue{i}" } : null,
                Child3 = new()
                {
                    Id = i + 1,
                    Corge = $"CorgeValue{i}",
                    Child = i % 3 == 0 ? new() { Id = i + 1, Grault = $"GraultValue{i}" } : null,
                },
            };
            _data.Add(sampleEntity);
        }
    }

    // ============================================================
    // Pattern 1: Traditional Select with Anonymous Type
    // ============================================================
    [Benchmark(Description = "Traditional Anonymous")]
    public int Traditional_Anonymous()
    {
        var results = _data
            .Select(s => new
            {
                s.Id,
                s.Foo,
                s.Bar,
                Childs = s.Childs.Select(c => new
                {
                    c.Id,
                    c.Baz,
                    ChildId = c.Child != null ? c.Child.Id : (int?)null,
                    ChildQux = c.Child != null ? c.Child.Qux : null,
                }),
                Child2Id = s.Child2 != null ? (int?)s.Child2.Id : null,
                Child2Quux = s.Child2 != null ? s.Child2.Quux : null,
                Child3Id = s.Child3.Id,
                Child3Corge = s.Child3.Corge,
                Child3ChildId = s.Child3 != null && s.Child3.Child != null
                    ? (int?)s.Child3.Child.Id
                    : null,
                Child3ChildGrault = s.Child3 != null && s.Child3.Child != null
                    ? s.Child3.Child.Grault
                    : null,
            })
            .ToList();
        return results.Count;
    }

    // ============================================================
    // Pattern 2: Traditional Select with Manual DTO
    // (Manual DTO definition)
    // ============================================================
    [Benchmark(Description = "Traditional Manual DTO")]
    public int Traditional_ManualDto()
    {
        var results = _data
            .Select(s => new ManualSampleClassDto
            {
                Id = s.Id,
                Foo = s.Foo,
                Bar = s.Bar,
                Childs = s.Childs.Select(c => new ManualSampleChildDto
                {
                    Id = c.Id,
                    Baz = c.Baz,
                    ChildId = c.Child != null ? c.Child.Id : null,
                    ChildQux = c.Child != null ? c.Child.Qux : null,
                }),
                Child2Id = s.Child2 != null ? s.Child2.Id : null,
                Child2Quux = s.Child2 != null ? s.Child2.Quux : null,
                Child3Id = s.Child3.Id,
                Child3Corge = s.Child3.Corge,
                Child3ChildId =
                    s.Child3 != null && s.Child3.Child != null ? s.Child3.Child.Id : null,
                Child3ChildGrault =
                    s.Child3 != null && s.Child3.Child != null ? s.Child3.Child.Grault : null,
            })
            .ToList();
        return results.Count;
    }

    // ============================================================
    // Pattern 3: Linqraft SelectExpr with Anonymous Type
    // (Using Linqraft - Anonymous Type)
    // ============================================================
    [Benchmark(Description = "Linqraft Anonymous")]
    public int Linqraft_Anonymous()
    {
        var results = _data
            .SelectExpr(s => new
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
            })
            .ToList();
        return results.Count;
    }

    // ============================================================
    // Pattern 4: Linqraft SelectExpr with Auto-Generated DTO
    // (Using Linqraft - Auto-Generated DTO)
    // ============================================================
    [Benchmark(Baseline = true, Description = "Linqraft Auto-Generated DTO")]
    public int Linqraft_AutoGeneratedDto()
    {
        var results = _data
            .SelectExpr<SampleClass, InMemoryEnumerableAotLinqraftSampleClassDto>(s => new
            {
                s.Id,
                s.Foo,
                s.Bar,
                Childs = s.Childs.SelectExpr<
                    SampleChildClass,
                    InMemoryEnumerableAotLinqraftSampleChildClassDto
                >(c => new
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
            })
            .ToList();
        return results.Count;
    }

    // ============================================================
    // Pattern 5: Linqraft SelectExpr with Manual DTO
    // (Using Linqraft - Manual DTO definition)
    // ============================================================
    [Benchmark(Description = "Linqraft Manual DTO")]
    public int Linqraft_ManualDto()
    {
        var results = _data
            .SelectExpr(s => new ManualSampleClassDto
            {
                Id = s.Id,
                Foo = s.Foo,
                Bar = s.Bar,
                Childs = s.Childs.Select(c => new ManualSampleChildDto
                {
                    Id = c.Id,
                    Baz = c.Baz,
                    ChildId = c.Child?.Id,
                    ChildQux = c.Child?.Qux,
                }),
                Child2Id = s.Child2?.Id,
                Child2Quux = s.Child2?.Quux,
                Child3Id = s.Child3.Id,
                Child3Corge = s.Child3.Corge,
                Child3ChildId = s.Child3?.Child?.Id,
                Child3ChildGrault = s.Child3?.Child?.Grault,
            })
            .ToList();
        return results.Count;
    }

    // ============================================================
    // Pattern 7: Mapperly with Map
    // (Using Mapperly's source-generated mapping)
    // ============================================================
    [Benchmark(Description = "Mapperly Map")]
    public int Mapperly_Map()
    {
        var results = _data.Select(MapperlyMapper.MapSampleClass).ToList();
        return results.Count;
    }
}
