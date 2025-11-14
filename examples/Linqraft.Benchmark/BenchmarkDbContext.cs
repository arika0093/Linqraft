using Microsoft.EntityFrameworkCore;

namespace Linqraft.Benchmark;

/// <summary>
/// Benchmark database context for performance testing.
/// </summary>
public class BenchmarkDbContext(DbContextOptions<BenchmarkDbContext> options) : DbContext(options)
{
    /// <summary>
    /// Main sample entity table.
    /// </summary>
    public DbSet<SampleClass> SampleClasses { get; set; }

    /// <summary>
    /// Child entity table.
    /// </summary>
    public DbSet<SampleChildClass> SampleChildClasses { get; set; }

    /// <summary>
    /// Grandchild entity table.
    /// </summary>
    public DbSet<SampleChildChildClass> SampleChildChildClasses { get; set; }

    /// <summary>
    /// Second child entity table.
    /// </summary>
    public DbSet<SampleChildClass2> SampleChildClass2s { get; set; }

    /// <summary>
    /// Third child entity table.
    /// </summary>
    public DbSet<SampleChildClass3> SampleChildClass3s { get; set; }

    /// <summary>
    /// Grandchild entity table for third child.
    /// </summary>
    public DbSet<SampleChildChildClass2> SampleChildChildClass2s { get; set; }
}
