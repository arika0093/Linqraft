using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace EFCore.ExprGenerator.Sample;

public class Worker(IDbContextFactory<SampleDbContext> dbContextFactory, ILogger<Worker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var dbContext = await dbContextFactory.CreateDbContextAsync(stoppingToken);

            // create database
            logger.LogInformation("Worker starting ...");
            await dbContext.Database.EnsureDeletedAsync(stoppingToken);
            await dbContext.Database.EnsureCreatedAsync(stoppingToken);
            logger.LogInformation("Database ensured created.");

            // get sample data
            var sample = await dbContext
                .SampleClasses.SelectExpr(s => new
                {
                    s.Id,
                    s.Foo,
                    s.Bar,
                    Childs = s
                        .Childs.Select(c => new
                        {
                            c.Id,
                            c.Baz,
                            ChildId = c.Child?.Id,
                            ChildQux = c.Child?.Qux,
                        })
                        .ToList(),
                    Child2Id = s.Child2?.Id,
                    Child2Quux = s.Child2?.Quux,
                    Child3Id = s.Child3.Id,
                    Child3Corge = s.Child3.Corge,
                    Child3ChildId = s.Child3?.Child?.Id,
                    Child3ChildGrault = s.Child3?.Child?.Grault,
                })
                .FirstOrDefaultAsync(stoppingToken);

            logger.LogInformation(
                "Sample data retrieved: {Sample}",
                JsonSerializer.Serialize(sample)
            );
        }
        finally
        {
            // finish work
            logger.LogInformation("Worker finished ...");
            // exit
            Environment.Exit(0);
        }
    }
}
