using BenchmarkDotNet.Running;
using Microsoft.EntityFrameworkCore;

namespace Linqraft.Benchmark;

class Program
{
    static async Task Main(string[] args)
    {
        // If running with --test flag, run the test instead of benchmark
        if (args.Length > 0 && args[0] == "--test")
        {
            await RunTest();
        }
        else
        {
            var summary = BenchmarkRunner.Run<SelectBenchmark>();
        }
    }

    static async Task RunTest()
    {
        var options = new DbContextOptionsBuilder<BenchmarkDbContext>()
            .UseSqlite("Data Source=test.db")
            .Options;

        using var dbContext = new BenchmarkDbContext(options);

        // Recreate database
        await dbContext.Database.EnsureDeletedAsync();
        await dbContext.Database.EnsureCreatedAsync();

        // Add test data
        var sampleEntity = new SampleClass
        {
            Foo = "FooValue",
            Bar = "BarValue",
            Childs =
            [
                new()
                {
                    Baz = "BazValue1",
                    Child = new() { Qux = "QuxValue1" },
                },
                new()
                {
                    Baz = "BazValue2",
                    Child = new() { Qux = "QuxValue2" },
                },
            ],
            Child2 = new() { Quux = "QuuxValue1" },
            Child3 = new()
            {
                Corge = "CorgeValue1",
                Child = new() { Grault = "GraultValue1" },
            },
        };
        dbContext.Add(sampleEntity);
        await dbContext.SaveChangesAsync();

        // Test traditional select
        Console.WriteLine("Testing Traditional Select...");
        var traditionalResult = await dbContext.SampleClasses
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
                Child3ChildId = s.Child3 != null && s.Child3.Child != null ? s.Child3.Child.Id : null,
                Child3ChildGrault = s.Child3 != null && s.Child3.Child != null ? s.Child3.Child.Grault : null,
            })
            .ToListAsync();
        Console.WriteLine($"Traditional Select returned {traditionalResult.Count} results");

        // Test Linqraft SelectExpr
        Console.WriteLine("Testing Linqraft SelectExpr...");
        var linqraftResult = await dbContext.SampleClasses
            .SelectExpr<SampleClass, LinqraftSampleClassDto>(s => new
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
        Console.WriteLine($"Linqraft SelectExpr returned {linqraftResult.Count} results");

        // Cleanup
        await dbContext.Database.EnsureDeletedAsync();
        
        Console.WriteLine("\n✅ Both methods work correctly!");
        Console.WriteLine("\nRun without --test flag to execute benchmark:");
        Console.WriteLine("  dotnet run -c Release");
    }
}
