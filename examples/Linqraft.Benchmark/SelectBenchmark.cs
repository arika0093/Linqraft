using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using Microsoft.EntityFrameworkCore;

namespace Linqraft.Benchmark;

[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
public class SelectBenchmark
{
    private BenchmarkDbContext _dbContext = null!;
    private const int DataCount = 100;

    [GlobalSetup]
    public async Task Setup()
    {
        var options = new DbContextOptionsBuilder<BenchmarkDbContext>()
            .UseSqlite("Data Source=benchmark.db")
            .Options;

        _dbContext = new BenchmarkDbContext(options);

        // Recreate database
        await _dbContext.Database.EnsureDeletedAsync();
        await _dbContext.Database.EnsureCreatedAsync();

        // Seed test data
        for (int i = 0; i < DataCount; i++)
        {
            var sampleEntity = new SampleClass
            {
                Foo = $"FooValue{i}",
                Bar = $"BarValue{i}",
                Childs =
                [
                    new()
                    {
                        Baz = $"BazValue{i}-1",
                        Child = new() { Qux = $"QuxValue{i}-1" },
                    },
                    new()
                    {
                        Baz = $"BazValue{i}-2",
                        Child = new() { Qux = $"QuxValue{i}-2" },
                    },
                ],
                Child2 = i % 2 == 0 ? new() { Quux = $"QuuxValue{i}" } : null,
                Child3 = new()
                {
                    Corge = $"CorgeValue{i}",
                    Child = i % 3 == 0 ? new() { Grault = $"GraultValue{i}" } : null,
                },
            };
            _dbContext.Add(sampleEntity);
        }
        await _dbContext.SaveChangesAsync();
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        await _dbContext.Database.EnsureDeletedAsync();
        await _dbContext.DisposeAsync();
    }

    // ============================================================
    // Pattern 1: Traditional Select with Anonymous Type
    // (Baseline)
    // ============================================================
    [Benchmark(Baseline = true, Description = "Traditional Anonymous")]
    public async Task<int> Traditional_Anonymous()
    {
        var results = await _dbContext
            .SampleClasses.Select(s => new
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
            .ToListAsync();
        return results.Count;
    }

    // ============================================================
    // Pattern 2: Traditional Select with Manual DTO
    // (Manual DTO definition)
    // ============================================================
    [Benchmark(Description = "Traditional Manual DTO")]
    public async Task<int> Traditional_ManualDto()
    {
        var results = await _dbContext
            .SampleClasses.Select(s => new ManualSampleClassDto
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
            .ToListAsync();
        return results.Count;
    }

    // ============================================================
    // Pattern 3: Linqraft SelectExpr with Anonymous Type
    // (Using Linqraft - Anonymous Type)
    // ============================================================
    [Benchmark(Description = "Linqraft Anonymous")]
    public async Task<int> Linqraft_Anonymous()
    {
        var results = await _dbContext
            .SampleClasses.SelectExpr(s => new
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
            .ToListAsync();
        return results.Count;
    }

    // ============================================================
    // Pattern 4: Linqraft SelectExpr with Auto-Generated DTO
    // (Using Linqraft - Auto-Generated DTO)
    // ============================================================
    [Benchmark(Description = "Linqraft Auto-Generated DTO")]
    public async Task<int> Linqraft_AutoGeneratedDto()
    {
        var results = await _dbContext
            .SampleClasses.SelectExpr<SampleClass, LinqraftSampleClassDto>(s => new
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
            .ToListAsync();
        return results.Count;
    }
}
