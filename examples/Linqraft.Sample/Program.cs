using Linqraft.Sample;
using Microsoft.EntityFrameworkCore;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<Worker>();

// add EFCore
builder.Services.AddDbContextFactory<SampleDbContext>(options =>
{
    options
        .UseSqlite("Data Source=sample.db")
        .UseAsyncSeeding(
            // seed data
            async (dbContext, flag, ct) =>
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
                await dbContext.SaveChangesAsync(ct);
            }
        );
});

var host = builder.Build();
host.Run();
