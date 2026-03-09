using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection; 
using Linqraft.SourceGenerator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Linqraft.Tests;

public sealed class RepositoryCompilationSmokeTests
{
    [Fact]
    public void Source_projects_compile_in_memory()
    {
        var repositoryRoot = GetRepositoryRoot();
        var parseOptions = CreateParseOptions();
        var coreSourceTrees = LoadProjectSyntaxTrees(
            Path.Combine(repositoryRoot, "src", "Linqraft.Core"),
            parseOptions,
            excludedFileNames: ["InternalsVisibleTo.cs"]
        );
        var sourceGeneratorSourceTrees = LoadProjectSyntaxTrees(
            Path.Combine(repositoryRoot, "src", "Linqraft.SourceGenerator"),
            parseOptions
        );

        var coreCompilation = CreateCompilation(
            "Linqraft.Core",
            Path.Combine(repositoryRoot, "src", "Linqraft.Core"),
            additionalSyntaxTrees: null
        );
        GetErrors(coreCompilation).ShouldBeEmpty();
        _ = EmitReference(coreCompilation);

        var sourceGeneratorCompilation = CreateCompilation(
            "Linqraft.SourceGenerator",
            Path.Combine(repositoryRoot, "src", "Linqraft.SourceGenerator"),
            additionalSyntaxTrees: coreSourceTrees
        );
        GetErrors(sourceGeneratorCompilation).ShouldBeEmpty();
        _ = EmitReference(sourceGeneratorCompilation);

        var analyzerCompilation = CreateCompilation(
            "Linqraft.Analyzer",
            Path.Combine(repositoryRoot, "src", "Linqraft.Analyzer"),
            additionalSyntaxTrees: coreSourceTrees
        );
        GetErrors(analyzerCompilation).ShouldBeEmpty();
        var analyzerReference = EmitReference(analyzerCompilation);

        var testsCompilation = CreateCompilation(
            "Linqraft.Tests.Source",
            Path.Combine(repositoryRoot, "tests", "Linqraft.Tests"),
            sourceGeneratorSourceTrees
                .Concat(coreSourceTrees)
                .Concat(new[] { CreateTestGlobalUsingsTree() })
                .ToArray()
        );
        GetErrors(testsCompilation).ShouldBeEmpty();

        var analyzerTestsCompilation = CreateCompilation(
            "Linqraft.Analyzer.Tests.Source",
            Path.Combine(repositoryRoot, "tests", "Linqraft.Analyzer.Tests"),
            new[] { CreateTestGlobalUsingsTree() },
            additionalReferences: new[] { analyzerReference }
        );
        GetErrors(analyzerTestsCompilation).ShouldBeEmpty();
    }

    [Fact]
    public void Configuration_and_example_projects_compile_in_memory_with_generator()
    {
        var repositoryRoot = GetRepositoryRoot();

        GetGeneratedErrors(
            Path.Combine(repositoryRoot, "tests", "Linqraft.Tests.Configuration"),
            OutputKind.ConsoleApplication,
            additionalSyntaxTrees: new[] { CreateBasicImplicitUsingsTree() },
            globalOptions: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["build_property.LinqraftRecordGenerate"] = "true",
                ["build_property.LinqraftPropertyAccessor"] = "GetAndInit",
                ["build_property.LinqraftHasRequired"] = "false",
                ["build_property.LinqraftNestedDtoUseHashNamespace"] = "false",
            }
        ).ShouldBeEmpty();

        GetGeneratedErrors(
            Path.Combine(repositoryRoot, "examples", "Linqraft.MinimumSample"),
            OutputKind.ConsoleApplication,
            additionalSyntaxTrees: new[] { CreateBasicImplicitUsingsTree() }
        ).ShouldBeEmpty();

        GetGeneratedErrors(
            Path.Combine(repositoryRoot, "examples", "Linqraft.ApiSample"),
            OutputKind.ConsoleApplication,
            additionalSyntaxTrees: new[]
            {
                CreateApiImplicitUsingsTree(),
                CreateSourceTree(AspNetExampleStubs, "AspNetExampleStubs.g.cs"),
            }
        ).ShouldBeEmpty();

        GetGeneratedErrors(
            Path.Combine(repositoryRoot, "examples", "Linqraft.Sample"),
            OutputKind.ConsoleApplication,
            additionalSyntaxTrees: new[]
            {
                CreateWorkerImplicitUsingsTree(),
                CreateSourceTree(WorkerExampleStubs, "WorkerExampleStubs.g.cs"),
            }
        ).ShouldBeEmpty();
    }

    private static string GetRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "README.md"))
                && Directory.Exists(Path.Combine(directory.FullName, "src")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find the repository root for Linqraft.");
    }

    private static CSharpCompilation CreateCompilation(
        string assemblyName,
        string projectDirectory,
        IReadOnlyCollection<SyntaxTree>? additionalSyntaxTrees = null,
        OutputKind outputKind = OutputKind.DynamicallyLinkedLibrary,
        params MetadataReference[] additionalReferences
    )
    {
        var parseOptions = CreateParseOptions();
        var syntaxTrees = LoadProjectSyntaxTrees(projectDirectory, parseOptions).ToList();
        if (additionalSyntaxTrees is not null)
        {
            syntaxTrees.AddRange(additionalSyntaxTrees);
        }

        return CSharpCompilation.Create(
            assemblyName,
            syntaxTrees,
            GetBaseReferences().Concat(additionalReferences),
            new CSharpCompilationOptions(
                outputKind,
                nullableContextOptions: NullableContextOptions.Enable
            )
        );
    }

    private static ImmutableArray<Diagnostic> GetErrors(CSharpCompilation compilation)
    {
        return compilation.GetDiagnostics()
            .Where(diagnostic => diagnostic.Id != "RSEXPERIMENTAL002")
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToImmutableArray();
    }

    private static ImmutableArray<Diagnostic> GetGeneratedErrors(
        string projectDirectory,
        OutputKind outputKind,
        IReadOnlyCollection<SyntaxTree>? additionalSyntaxTrees = null,
        IReadOnlyDictionary<string, string>? globalOptions = null
    )
    {
        var compilation = CreateCompilation(
            Path.GetFileName(projectDirectory),
            projectDirectory,
            additionalSyntaxTrees,
            outputKind
        );
        var generator = new LinqraftSourceGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [generator.AsSourceGenerator()],
            parseOptions: CreateParseOptions(),
            optionsProvider: globalOptions is null ? null : new TestAnalyzerConfigOptionsProvider(globalOptions)
        );

        driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var generatorDiagnostics);
        return generatorDiagnostics
            .Concat(outputCompilation.GetDiagnostics())
            .Where(diagnostic => diagnostic.Id != "RSEXPERIMENTAL002")
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToImmutableArray();
    }

    private static MetadataReference EmitReference(CSharpCompilation compilation)
    {
        using var stream = new MemoryStream();
        var emitResult = compilation.Emit(stream);
        emitResult.Diagnostics
            .Where(diagnostic => diagnostic.Id != "RSEXPERIMENTAL002")
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToImmutableArray()
            .ShouldBeEmpty();
        stream.Position = 0;
        return MetadataReference.CreateFromImage(stream.ToArray());
    }

    private static IEnumerable<MetadataReference> GetBaseReferences()
    {
        var explicitAssemblies = new List<Assembly>
        {
            typeof(object).Assembly,
            typeof(Console).Assembly,
            typeof(Enumerable).Assembly,
            typeof(Queryable).Assembly,
            typeof(System.Linq.Expressions.Expression).Assembly,
            typeof(ImmutableArray<>).Assembly,
            typeof(CSharpCompilation).Assembly,
            typeof(System.Text.Json.JsonSerializer).Assembly,
            typeof(System.ComponentModel.DataAnnotations.KeyAttribute).Assembly,
        };

        foreach (var assemblyName in new[]
        {
            "System.Runtime",
            "netstandard",
            "System.Collections",
            "System.Collections.Immutable",
            "System.Console",
            "System.Linq",
            "System.Linq.Queryable",
            "System.Linq.Expressions",
            "System.Threading.Tasks",
            "Microsoft.CodeAnalysis.Workspaces",
            "Microsoft.CodeAnalysis.Features",
            "Microsoft.CodeAnalysis.CSharp.Workspaces",
            "System.Composition.AttributedModel",
            "System.Composition.Runtime",
            "xunit.assert",
            "Shouldly",
        })
        {
            try
            {
                explicitAssemblies.Add(Assembly.Load(assemblyName));
            }
            catch
            {
                // Best-effort load for compilation metadata.
            }
        }

        return AppDomain.CurrentDomain
            .GetAssemblies()
            .Concat(explicitAssemblies)
            .Where(assembly => !assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
            .Where(assembly =>
            {
                var name = assembly.GetName().Name ?? string.Empty;
                return !name.StartsWith("Linqraft", StringComparison.Ordinal);
            })
            .Select(assembly => MetadataReference.CreateFromFile(assembly.Location))
            .Distinct(MetadataReferencePathComparer.Instance);
    }

    private static IReadOnlyList<SyntaxTree> LoadProjectSyntaxTrees(
        string projectDirectory,
        CSharpParseOptions parseOptions,
        IReadOnlyCollection<string>? excludedFileNames = null
    )
    {
        var excluded = excludedFileNames is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(excludedFileNames, StringComparer.OrdinalIgnoreCase);
        return Directory
            .EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !excluded.Contains(Path.GetFileName(path)))
            .Select(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path), parseOptions, path))
            .ToList();
    }

    private static SyntaxTree CreateGlobalUsingsTree(string source)
    {
        return CSharpSyntaxTree.ParseText(
            source,
            CreateParseOptions(),
            path: "GlobalUsings.g.cs"
        );
    }

    private static SyntaxTree CreateTestGlobalUsingsTree()
    {
        return CreateGlobalUsingsTree(
            """
            global using Xunit;
            global using Shouldly;
            """
        );
    }

    private static SyntaxTree CreateBasicImplicitUsingsTree()
    {
        return CreateGlobalUsingsTree(
            """
            global using System;
            global using System.Collections;
            global using System.Collections.Generic;
            global using System.Linq;
            global using System.Threading;
            global using System.Threading.Tasks;
            """
        );
    }

    private static SyntaxTree CreateApiImplicitUsingsTree()
    {
        return CreateGlobalUsingsTree(
            """
            global using System;
            global using System.Collections;
            global using System.Collections.Generic;
            global using System.Linq;
            global using System.Threading;
            global using System.Threading.Tasks;
            global using Microsoft.AspNetCore.Builder;
            global using Microsoft.Extensions.DependencyInjection;
            """
        );
    }

    private static SyntaxTree CreateWorkerImplicitUsingsTree()
    {
        return CreateGlobalUsingsTree(
            """
            global using System;
            global using System.Collections;
            global using System.Collections.Generic;
            global using System.Linq;
            global using System.Threading;
            global using System.Threading.Tasks;
            global using Microsoft.Extensions.DependencyInjection;
            global using Microsoft.Extensions.Hosting;
            global using Microsoft.Extensions.Logging;
            """
        );
    }

    private static SyntaxTree CreateSourceTree(string source, string path)
    {
        return CSharpSyntaxTree.ParseText(
            source,
            CreateParseOptions(),
            path: path
        );
    }

    private static CSharpParseOptions CreateParseOptions()
    {
        return new CSharpParseOptions(LanguageVersion.Preview).WithFeatures(
            new[] { new KeyValuePair<string, string>("InterceptorsNamespaces", "Linqraft") }
        );
    }

    private sealed class TestAnalyzerConfigOptionsProvider : AnalyzerConfigOptionsProvider
    {
        private readonly AnalyzerConfigOptions _globalOptions;

        public TestAnalyzerConfigOptionsProvider(IReadOnlyDictionary<string, string> globalOptions)
        {
            _globalOptions = new TestAnalyzerConfigOptions(globalOptions);
        }

        public override AnalyzerConfigOptions GlobalOptions => _globalOptions;

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => _globalOptions;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => _globalOptions;
    }

    private sealed class TestAnalyzerConfigOptions : AnalyzerConfigOptions
    {
        private readonly IReadOnlyDictionary<string, string> _options;

        public TestAnalyzerConfigOptions(IReadOnlyDictionary<string, string> options)
        {
            _options = options;
        }

        public override bool TryGetValue(string key, out string value)
        {
            if (_options.TryGetValue(key, out var resolved))
            {
                value = resolved;
                return true;
            }

            value = string.Empty;
            return false;
        }
    }

    private const string AspNetExampleStubs =
        """
        using System;

        namespace Microsoft.AspNetCore.Mvc
        {
            [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
            public sealed class RouteAttribute : Attribute
            {
                public RouteAttribute(string template) { }
            }

            [AttributeUsage(AttributeTargets.Class)]
            public sealed class ApiControllerAttribute : Attribute { }

            [AttributeUsage(AttributeTargets.Method)]
            public sealed class HttpGetAttribute : Attribute { }

            public interface IActionResult { }

            public class ActionResult<T> : IActionResult
            {
                public static implicit operator ActionResult<T>(T value) => new();
            }

            public abstract class ControllerBase
            {
                protected IActionResult Ok(object? value) => throw null!;
            }
        }

        namespace Microsoft.Extensions.DependencyInjection
        {
            public interface IServiceCollection { }

            public sealed class ServiceCollection : IServiceCollection { }

            public static class ServiceCollectionExtensions
            {
                public static IServiceCollection AddOpenApi(this IServiceCollection services) => services;

                public static IServiceCollection AddControllers(this IServiceCollection services) => services;
            }
        }

        namespace Microsoft.AspNetCore.Builder
        {
            public sealed class WebApplicationBuilder
            {
                public Microsoft.Extensions.DependencyInjection.IServiceCollection Services { get; } = new Microsoft.Extensions.DependencyInjection.ServiceCollection();

                public WebApplication Build() => new();
            }

            public sealed class WebApplication
            {
                public static WebApplicationBuilder CreateBuilder(string[] args) => new();

                public void MapOpenApi() { }

                public void UseSwaggerUI(Action<SwaggerUIOptions> configure) => configure(new());

                public void MapGet(string pattern, Func<object?> handler) { }

                public void UseHttpsRedirection() { }

                public void MapControllers() { }

                public void Run() { }
            }

            public sealed class SwaggerUIOptions
            {
                public string? RoutePrefix { get; set; }

                public void SwaggerEndpoint(string url, string name) { }
            }
        }
        """;

    private const string WorkerExampleStubs =
        """
        using System;
        using System.Collections;
        using System.Linq;
        using System.Linq.Expressions;
        using System.Threading;
        using System.Threading.Tasks;

        namespace Microsoft.Extensions.Logging
        {
            public interface ILogger<T>
            {
                void LogInformation(string message, params object?[] args);
            }
        }

        namespace Microsoft.Extensions.DependencyInjection
        {
            public interface IServiceCollection { }

            public sealed class ServiceCollection : IServiceCollection { }

            public static class ServiceCollectionExtensions
            {
                public static IServiceCollection AddHostedService<T>(this IServiceCollection services)
                    where T : class => services;

                public static IServiceCollection AddDbContextFactory<TContext>(
                    this IServiceCollection services,
                    Action<Microsoft.EntityFrameworkCore.DbContextOptionsBuilder> configure)
                    where TContext : class
                {
                    configure(new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder());
                    return services;
                }
            }
        }

        namespace Microsoft.Extensions.Hosting
        {
            public interface IHost
            {
                void Run();
            }

            public sealed class HostApplicationBuilder
            {
                public Microsoft.Extensions.DependencyInjection.IServiceCollection Services { get; } = new Microsoft.Extensions.DependencyInjection.ServiceCollection();

                public IHost Build() => new HostInstance();

                private sealed class HostInstance : IHost
                {
                    public void Run() { }
                }
            }

            public static class Host
            {
                public static HostApplicationBuilder CreateApplicationBuilder(string[] args) => new();
            }

            public abstract class BackgroundService
            {
                protected abstract Task ExecuteAsync(CancellationToken stoppingToken);
            }
        }

        namespace Microsoft.EntityFrameworkCore
        {
            public class DbContextOptions { }

            public class DbContextOptions<TContext> : DbContextOptions
                where TContext : class
            {
            }

            public class DbContextOptionsBuilder
            {
                public DbContextOptions Options { get; } = new();
            }

            public class DbContext : IDisposable
            {
                protected DbContext() { }

                protected DbContext(DbContextOptions options) { }

                public DatabaseFacade Database { get; } = new();

                public void Add(object entity) { }

                public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);

                public void Dispose() { }
            }

            public sealed class DatabaseFacade
            {
                public Task EnsureDeletedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

                public Task EnsureCreatedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
            }

            public interface IDbContextFactory<TContext>
                where TContext : class
            {
                Task<TContext> CreateDbContextAsync(CancellationToken cancellationToken = default);
            }

            public class DbSet<T> : IQueryable<T>
            {
                public Type ElementType => throw null!;

                public Expression Expression => throw null!;

                public IQueryProvider Provider => throw null!;

                public IEnumerator<T> GetEnumerator() => throw null!;

                IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
            }

            public static class DbContextOptionsBuilderExtensions
            {
                public static DbContextOptionsBuilder UseSqlite(this DbContextOptionsBuilder builder, string connectionString) => builder;

                public static DbContextOptionsBuilder UseAsyncSeeding(
                    this DbContextOptionsBuilder builder,
                    Func<global::Linqraft.Sample.SampleDbContext, bool, CancellationToken, Task> seedingAction) => builder;
            }

            public static class EntityFrameworkQueryableExtensions
            {
                public static Task<T?> FirstOrDefaultAsync<T>(this IQueryable<T> source, CancellationToken cancellationToken = default) => Task.FromResult<T?>(default);
            }
        }
        """;

    private sealed class MetadataReferencePathComparer : IEqualityComparer<MetadataReference>
    {
        public static readonly MetadataReferencePathComparer Instance = new();

        public bool Equals(MetadataReference? x, MetadataReference? y)
        {
            return StringComparer.OrdinalIgnoreCase.Equals(GetPath(x), GetPath(y));
        }

        public int GetHashCode(MetadataReference obj)
        {
            return StringComparer.OrdinalIgnoreCase.GetHashCode(GetPath(obj) ?? string.Empty);
        }

        private static string? GetPath(MetadataReference? reference)
        {
            return (reference as PortableExecutableReference)?.FilePath;
        }
    }
}
