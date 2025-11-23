using Microsoft.EntityFrameworkCore;

namespace Linqraft.Sample;

public class SampleDbContext(DbContextOptions<SampleDbContext> options) : DbContext(options)
{
    public DbSet<SampleClass> SampleClasses { get; set; }
    public DbSet<SampleChildClass> SampleChildClasses { get; set; }
    public DbSet<SampleChildChildClass> SampleChildChildClasses { get; set; }
    public DbSet<SampleChildClass2> SampleChildClass2s { get; set; }
}
