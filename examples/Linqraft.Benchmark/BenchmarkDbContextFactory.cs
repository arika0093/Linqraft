using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Linqraft.Benchmark;

/// <summary>
/// Design-time factory for BenchmarkDbContext.
/// Used by EF Core tools for migrations and model optimization.
/// </summary>
public class BenchmarkDbContextFactory : IDesignTimeDbContextFactory<BenchmarkDbContext>
{
    public BenchmarkDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<BenchmarkDbContext>();
        optionsBuilder.UseSqlite("Data Source=benchmark.db");

        return new BenchmarkDbContext(optionsBuilder.Options);
    }
}
