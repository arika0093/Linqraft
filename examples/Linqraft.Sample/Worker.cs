using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Linqraft.Sample;

public class Worker(IDbContextFactory<SampleDbContext> dbContextFactory, ILogger<Worker> logger)
    : BackgroundService
{
    private GeneratePattern generatePattern = GeneratePattern.PreCompiledQuery;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var dbContext = await dbContextFactory.CreateDbContextAsync(stoppingToken);

            // create database
            await DatabaseSetup(dbContext, stoppingToken);

            // get sample data
            SampleClassDto? sample = null;

            switch (generatePattern)
            {
                // Pattern 1: Inline mapping
                case GeneratePattern.InlineMapping:
                    sample = await dbContext
                        .SampleClasses.UseLinqraft()
                        .Select<SampleClassDto>(s => new
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
                        .FirstOrDefaultAsync(stoppingToken);
                    break;

                // Pattern 2: Pre-generated mapping (see LinqraftMappingProfile)
                case GeneratePattern.PreGeneratedMapping:
                    sample = await dbContext
                        .SampleClasses.ProjectToSampleClassDto()
                        .FirstOrDefaultAsync(stoppingToken);
                    break;

                // Pattern 3: Pre-compiled query (see GetSampleClassCompiled)
                case GeneratePattern.PreCompiledQuery:
                    sample = await GetSampleClassCompiled(dbContext);
                    break;
            }

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

    // pre-compile query
    private static readonly Func<SampleDbContext, Task<SampleClassDto?>> GetSampleClassCompiled =
        EF.CompileAsyncQuery(
            (SampleDbContext db) => db.SampleClasses.ProjectToSampleClassDto().FirstOrDefault()
        );

    private async Task DatabaseSetup(SampleDbContext dbContext, CancellationToken stoppingToken)
    {
        logger.LogInformation("Worker starting ...");
        await dbContext.Database.EnsureDeletedAsync(stoppingToken);
        await dbContext.Database.EnsureCreatedAsync(stoppingToken);
        logger.LogInformation("Database ensured created.");
    }
}

internal static partial class LinqraftMappingProfile
{
    [LinqraftMapping]
    internal static IQueryable<SampleClassDto> ProjectToSampleClassDto(
        this LinqraftMapper<SampleClass> source
    ) =>
        source.Select<SampleClassDto>(s => new
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
        });
}

internal enum GeneratePattern
{
    InlineMapping,
    PreGeneratedMapping,
    PreCompiledQuery,
}
