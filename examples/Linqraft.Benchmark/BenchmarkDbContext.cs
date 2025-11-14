using Microsoft.EntityFrameworkCore;

namespace Linqraft.Benchmark;

public class BenchmarkDbContext(DbContextOptions<BenchmarkDbContext> options) : DbContext(options)
{
    public DbSet<SampleClass> SampleClasses { get; set; }
    public DbSet<SampleChildClass> SampleChildClasses { get; set; }
    public DbSet<SampleChildChildClass> SampleChildChildClasses { get; set; }
    public DbSet<SampleChildClass2> SampleChildClass2s { get; set; }
    public DbSet<SampleChildClass3> SampleChildClass3s { get; set; }
    public DbSet<SampleChildChildClass2> SampleChildChildClass2s { get; set; }
}
