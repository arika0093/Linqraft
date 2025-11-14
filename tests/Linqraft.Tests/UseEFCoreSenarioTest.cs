using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Linqraft.Tests;

public sealed class UseEFCoreSenarioTest : IDisposable
{
    private const string DbFileName = "efcore-senario.db";
    private readonly SampleDbContext dbContext;

    public UseEFCoreSenarioTest()
    {
        dbContext = new SampleDbContext();
        // ensure database is created and seeded
        dbContext.Database.EnsureDeleted();
        dbContext.Database.EnsureCreated();
    }

    public void Dispose()
    {
        dbContext.Dispose();
    }

    [Fact]
    public async Task FetchTryCase1()
    {
        var sample = await dbContext
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
            .FirstOrDefaultAsync();
        sample.ShouldNotBeNull();
    }

    [Fact]
    public async Task FetchTryCase2()
    {
        var sample = await dbContext
            .SampleClasses.SelectExpr<SampleClass, SampleClassDtoFetched>(s => new
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
            .FirstOrDefaultAsync();
        sample.ShouldNotBeNull();
    }

    internal class SampleDbContext : DbContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder builder)
        {
            builder
                .UseSqlite($"Data Source={DbFileName}")
                .UseSeeding(
                    (dbContext, flag) =>
                    {
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
                        dbContext.SaveChanges();
                    }
                );
        }

        public DbSet<SampleClass> SampleClasses { get; set; }
        public DbSet<SampleChildClass> SampleChildClasses { get; set; }
        public DbSet<SampleChildChildClass> SampleChildChildClasses { get; set; }
        public DbSet<SampleChildClass2> SampleChildClass2s { get; set; }
    }

    internal class SampleClass
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }
        public string Foo { get; set; } = string.Empty;
        public string Bar { get; set; } = string.Empty;

        public List<SampleChildClass> Childs { get; set; } = [];
        public SampleChildClass2? Child2 { get; set; } = null;
        public SampleChildClass3 Child3 { get; set; } = null!;
    }

    internal class SampleChildClass
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }
        public int SampleClassId { get; set; }
        public string Baz { get; set; } = string.Empty;

        [ForeignKey("SampleClassId")]
        public SampleClass SampleClass { get; set; } = null!;

        public SampleChildChildClass? Child { get; set; } = null;
    }

    internal class SampleChildChildClass
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }
        public int SampleChildClassId { get; set; }
        public string Qux { get; set; } = string.Empty;

        [ForeignKey("SampleChildClassId")]
        public SampleChildClass SampleChildClass { get; set; } = null!;
    }

    internal class SampleChildClass2
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        public int SampleClassId { get; set; }

        public string Quux { get; set; } = string.Empty;

        [ForeignKey("SampleClassId")]
        public SampleClass SampleClass { get; set; } = null!;
    }

    internal class SampleChildClass3
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }
        public int SampleClassId { get; set; }
        public string Corge { get; set; } = string.Empty;

        [ForeignKey("SampleClassId")]
        public SampleClass SampleClass { get; set; } = null!;

        public SampleChildChildClass2? Child { get; set; } = null;
    }

    internal class SampleChildChildClass2
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }
        public int SampleChildClass3Id { get; set; }
        public string Grault { get; set; } = string.Empty;

        [ForeignKey("SampleChildClass3Id")]
        public SampleChildClass3 SampleChildClass3 { get; set; } = null!;
    }
}
