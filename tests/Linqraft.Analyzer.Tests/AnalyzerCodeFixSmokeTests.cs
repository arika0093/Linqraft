using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Linqraft.Analyzer;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Linqraft.Analyzer.Tests;

public sealed class AnalyzerCodeFixSmokeTests
{
    [Fact]
    public async Task Missing_capture_code_fix_adds_capture_argument()
    {
        const string source = """
            using System;
            using System.Linq;

            namespace System.Linq
            {
                public static class SelectExprExtensions
                {
                    public static IQueryable<TResult> SelectExpr<TIn, TResult>(this IQueryable<TIn> query, Func<TIn, object> selector)
                        where TIn : class => throw null!;
                }
            }

            public class Entity
            {
                public int Id { get; set; }
                public int Value { get; set; }
            }

            public class EntityDto { }

            public class QueryHolder
            {
                public IQueryable<EntityDto> Project(IQueryable<Entity> source, int threshold)
                {
                    return source.SelectExpr<Entity, EntityDto>(entity => new
                    {
                        entity.Id,
                        IsLarge = entity.Value > threshold,
                    });
                }
            }
            """;

        var fixedText = (await ApplyFixAsync(source, "LQRE001", "Add capture")).PrimaryDocumentText;

        fixedText.ShouldContain("capture: new { threshold }");
    }

    [Fact]
    public async Task Unused_capture_code_fix_removes_capture_argument()
    {
        const string source = """
            using System;
            using System.Linq;

            namespace System.Linq
            {
                public static class SelectExprExtensions
                {
                    public static IQueryable<TResult> SelectExpr<TIn, TResult>(this IQueryable<TIn> query, Func<TIn, object> selector, object capture)
                        where TIn : class => throw null!;
                }
            }

            public class Entity
            {
                public int Id { get; set; }
            }

            public class EntityDto { }

            public class QueryHolder
            {
                public IQueryable<EntityDto> Project(IQueryable<Entity> source, int threshold)
                {
                    return source.SelectExpr<Entity, EntityDto>(entity => new
                    {
                        entity.Id,
                    }, capture: new { threshold });
                }
            }
            """;

        var fixedText = (await ApplyFixAsync(source, "LQRS005", "Remove capture")).PrimaryDocumentText;

        fixedText.ShouldNotContain("capture:");
    }

    [Fact]
    public async Task Anonymous_object_current_file_fix_creates_dto()
    {
        const string source = """
            public class Builder
            {
                public object Create()
                {
                    return new
                    {
                        Id = 1,
                        Name = "sample",
                    };
                }
            }
            """;

        var result = await ApplyFixAsync(source, "LQRF001", "current file");
        var fixedText = result.PrimaryDocumentText;
        var compilationErrors = await GetCompilationErrorsAsync(result.ChangedSolution);

        compilationErrors.ShouldBeEmpty();
        fixedText.ShouldContain("new CreateDto");
        fixedText.ShouldContain("partial class CreateDto");
    }

    [Fact]
    public async Task Anonymous_object_new_file_fix_creates_additional_document()
    {
        const string source = """
            public class Builder
            {
                public object Create()
                {
                    return new
                    {
                        Id = 1,
                    };
                }
            }
            """;

        var result = await ApplyFixAsync(source, "LQRF001", "new file");
        var projectDocuments = result.ChangedSolution.Projects.Single().Documents.ToList();
        var compilationErrors = await GetCompilationErrorsAsync(result.ChangedSolution);

        compilationErrors.ShouldBeEmpty();
        result.PrimaryDocumentText.ShouldContain("new CreateDto");
        projectDocuments.Select(document => document.Name).ShouldContain("CreateDto.cs");
    }

    [Fact]
    public async Task Groupby_key_code_fix_creates_named_key_type()
    {
        const string source = """
            using System;
            using System.Linq;

            namespace System.Linq
            {
                public static class SelectExprExtensions
                {
                    public static IQueryable<TResult> SelectExpr<TIn, TResult>(this IQueryable<TIn> query, Func<TIn, TResult> selector)
                        where TIn : class => throw null!;
                }
            }

            public class Entity
            {
                public int Id { get; set; }
            }

            public class QueryHolder
            {
                public object Project(IQueryable<Entity> source)
                {
                    return source
                        .GroupBy(entity => new { entity.Id })
                        .SelectExpr(group => new { group.Key });
                }
            }
            """;

        var result = await ApplyFixAsync(source, "LQRE002", "named type");
        var fixedText = result.PrimaryDocumentText;
        var compilationErrors = await GetCompilationErrorsAsync(result.ChangedSolution);

        compilationErrors.ShouldBeEmpty();
        fixedText.ShouldContain("new EntityGroupKey");
        fixedText.ShouldContain("class EntityGroupKey");
    }

    [Fact]
    public async Task Produces_response_type_code_fix_adds_attribute()
    {
        const string source = """
            using System;
            using System.Linq;

            namespace Microsoft.AspNetCore.Mvc
            {
                public sealed class ApiControllerAttribute : Attribute { }
                public sealed class ProducesResponseTypeAttribute : Attribute
                {
                    public ProducesResponseTypeAttribute(Type type) { }
                }
                public interface IActionResult { }
                public abstract class ControllerBase { }
            }

            namespace System.Linq
            {
                public static class SelectExprExtensions
                {
                    public static IQueryable<TResult> SelectExpr<TIn, TResult>(this IQueryable<TIn> query, Func<TIn, object> selector)
                        where TIn : class => throw null!;
                }
            }

            public class Entity
            {
                public int Id { get; set; }
            }

            public class EntityDto { }

            [Microsoft.AspNetCore.Mvc.ApiController]
            public sealed class EntityController : Microsoft.AspNetCore.Mvc.ControllerBase
            {
                public Microsoft.AspNetCore.Mvc.IActionResult Get(IQueryable<Entity> source)
                {
                    var _ = source.SelectExpr<Entity, EntityDto>(entity => new
                    {
                        entity.Id,
                    });
                    return default!;
                }
            }
            """;

        var result = await ApplyFixAsync(source, "LQRF002", "ProducesResponseType");
        var fixedText = result.PrimaryDocumentText;
        var compilationErrors = await GetCompilationErrorsAsync(result.ChangedSolution);

        compilationErrors.ShouldBeEmpty();
        fixedText.ShouldContain("[global::Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(EntityDto))]");
    }

    [Fact]
    public async Task Untyped_selectexpr_code_fix_inserts_explicit_dto_types()
    {
        const string source = """
            using System;
            using System.Linq;

            namespace System.Linq
            {
                public static class SelectExprExtensions
                {
                    public static IQueryable<TResult> SelectExpr<TIn, TResult>(this IQueryable<TIn> query, Func<TIn, TResult> selector)
                        where TIn : class => throw null!;
                }
            }

            public class Entity
            {
                public int Id { get; set; }
            }

            public class QueryHolder
            {
                public object Project(IQueryable<Entity> source)
                {
                    return source.SelectExpr(entity => new
                    {
                        entity.Id,
                    });
                }
            }
            """;

        var fixedText = (await ApplyFixAsync(source, "LQRS001", "SelectExpr<T, TDto>")).PrimaryDocumentText;

        fixedText.ShouldContain("SelectExpr<Entity, ProjectDto>");
    }

    [Fact]
    public async Task Ternary_simplification_code_fix_applies_null_conditional()
    {
        const string source = """
            using System;
            using System.Linq;

            namespace System.Linq
            {
                public static class SelectExprExtensions
                {
                    public static IQueryable<TResult> SelectExpr<TIn, TResult>(this IQueryable<TIn> query, Func<TIn, TResult> selector)
                        where TIn : class => throw null!;
                }
            }

            public class Child
            {
                public int Id { get; set; }
            }

            public class Entity
            {
                public Child? Child { get; set; }
            }

            public class QueryHolder
            {
                public object Project(IQueryable<Entity> source)
                {
                    return source.SelectExpr(entity => entity.Child != null
                        ? new { ChildId = entity.Child.Id }
                        : null);
                }
            }
            """;

        var result = await ApplyFixAsync(source, "LQRS004", "Simplify null-check ternary");
        var fixedText = result.PrimaryDocumentText;
        var compilationErrors = await GetCompilationErrorsAsync(result.ChangedSolution);

        compilationErrors.ShouldBeEmpty();
        fixedText.ShouldContain("new { ChildId = entity.Child?.Id }");
    }

    [Fact]
    public async Task Async_api_response_code_fix_rewrites_method_shape()
    {
        const string source = """
            using System;
            using System.Collections;
            using System.Collections.Generic;
            using System.Linq;
            using System.Linq.Expressions;

            namespace Microsoft.EntityFrameworkCore
            {
                public class DbSet<T> : IQueryable<T>
                {
                    public Type ElementType => throw null!;
                    public Expression Expression => throw null!;
                    public IQueryProvider Provider => throw null!;
                    public IEnumerator<T> GetEnumerator() => throw null!;
                    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
                }

                public static class EntityFrameworkQueryableExtensions
                {
                    public static global::System.Threading.Tasks.Task<global::System.Collections.Generic.List<T>> ToListAsync<T>(this IQueryable<T> source)
                        => throw null!;
                }
            }

            namespace System.Linq
            {
                public static class SelectExprExtensions
                {
                    public static IQueryable<TResult> SelectExpr<TIn, TResult>(this IQueryable<TIn> query, Func<TIn, object> selector)
                        where TIn : class => throw null!;
                }
            }

            public class Entity
            {
                public int Id { get; set; }
            }

            public class ExecuteDto
            {
                public int Id { get; set; }
            }

            public sealed class Context
            {
                public Microsoft.EntityFrameworkCore.DbSet<Entity> Entities { get; } = null!;
            }

            public class ApiService
            {
                public void Execute(Context db)
                {
                    db.Entities.Select(entity => new { entity.Id });
                }
            }
            """;

        var result = await ApplyFixAsync(source, "LQRF003", "async API response method");
        var fixedText = result.PrimaryDocumentText;
        var compilationErrors = await GetCompilationErrorsAsync(result.ChangedSolution);

        compilationErrors.ShouldBeEmpty();
        fixedText.ShouldContain("Task<List<ExecuteDto>> ExecuteAsync");
        fixedText.ShouldContain("SelectExpr<Entity, ExecuteDto>");
        fixedText.ShouldContain(".ToListAsync()");
        fixedText.ShouldContain("return await");
        fixedText.ShouldContain("using System.Threading.Tasks;");
        fixedText.ShouldContain("using Microsoft.EntityFrameworkCore;");
    }

    [Fact]
    public async Task Sync_api_response_code_fix_rewrites_method_shape()
    {
        const string source = """
            using System.Linq;

            namespace System.Linq
            {
                public static class SelectExprExtensions
                {
                    public static IQueryable<TResult> SelectExpr<TIn, TResult>(this IQueryable<TIn> query, System.Func<TIn, object> selector)
                        where TIn : class => throw null!;
                }
            }

            public class Entity
            {
                public int Id { get; set; }
            }

            public class ExecuteDto
            {
                public int Id { get; set; }
            }

            public class ApiService
            {
                public void Execute(IQueryable<Entity> source)
                {
                    source.Select(entity => new { entity.Id });
                }
            }
            """;

        var result = await ApplyFixAsync(source, "LQRF004", "synchronous API response method");
        var fixedText = result.PrimaryDocumentText;
        var compilationErrors = await GetCompilationErrorsAsync(result.ChangedSolution);

        compilationErrors.ShouldBeEmpty();
        fixedText.ShouldContain("List<ExecuteDto> Execute");
        fixedText.ShouldContain("SelectExpr<Entity, ExecuteDto>");
        fixedText.ShouldContain(".ToList()");
        fixedText.ShouldContain("return ");
        fixedText.ShouldContain("using System.Collections.Generic;");
    }

    private static async Task<AppliedFixResult> ApplyFixAsync(string source, string diagnosticId, string titleFragment)
    {
        var document = CreateDocument(source);
        var diagnostic = await GetDiagnosticAsync(document, diagnosticId);
        var provider = new LinqraftCompositeCodeFixProvider();
        var actions = new List<CodeAction>();
        var context = new CodeFixContext(
            document,
            diagnostic,
            (action, _) => actions.Add(action),
            CancellationToken.None
        );
        await provider.RegisterCodeFixesAsync(context);

        var actionToApply = actions.FirstOrDefault(
            action => action.Title.Contains(titleFragment, StringComparison.OrdinalIgnoreCase)
        );
        actionToApply.ShouldNotBeNull();
        var operations = await actionToApply!.GetOperationsAsync(CancellationToken.None);
        var applyChangesOperation = operations.OfType<ApplyChangesOperation>().SingleOrDefault();
        applyChangesOperation.ShouldNotBeNull();
        var changedSolution = applyChangesOperation!.ChangedSolution;
        var changedDocument = changedSolution.GetDocument(document.Id)
            ?? changedSolution.Projects.SelectMany(project => project.Documents).First();
        var text = await changedDocument.GetTextAsync();
        return new AppliedFixResult
        {
            PrimaryDocumentText = text.ToString(),
            ChangedSolution = changedSolution,
        };
    }

    private static async Task<Diagnostic> GetDiagnosticAsync(Document document, string diagnosticId)
    {
        var compilation = await document.Project.GetCompilationAsync();
        var analyzer = new LinqraftCompositeAnalyzer();
        var diagnostics = await compilation!
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(analyzer))
            .GetAnalyzerDiagnosticsAsync();
        var diagnostic = diagnostics.FirstOrDefault(candidate => candidate.Id == diagnosticId);
        diagnostic.ShouldNotBeNull();
        return diagnostic!;
    }

    private static async Task<ImmutableArray<Diagnostic>> GetCompilationErrorsAsync(Solution solution)
    {
        var builder = ImmutableArray.CreateBuilder<Diagnostic>();
        foreach (var project in solution.Projects)
        {
            var compilation = await project.GetCompilationAsync();
            if (compilation is null)
            {
                continue;
            }

            builder.AddRange(compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        }

        return builder.ToImmutable();
    }

    private static Document CreateDocument(string source)
    {
        var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        var documentId = DocumentId.CreateNewId(projectId);

        var solution = workspace.CurrentSolution
            .AddProject(projectId, "AnalyzerCodeFixTests", "AnalyzerCodeFixTests", LanguageNames.CSharp)
            .WithProjectParseOptions(projectId, new CSharpParseOptions(LanguageVersion.Preview))
            .WithProjectCompilationOptions(
                projectId,
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary,
                    nullableContextOptions: NullableContextOptions.Enable
                )
            );

        foreach (var reference in GetMetadataReferences())
        {
            solution = solution.AddMetadataReference(projectId, reference);
        }

        solution = solution.AddDocument(documentId, "Test0.cs", SourceText.From(source));
        return solution.GetDocument(documentId)!;
    }

    private static IEnumerable<MetadataReference> GetMetadataReferences()
    {
        var explicitAssemblies = new List<Assembly>
        {
            typeof(object).Assembly,
            typeof(Enumerable).Assembly,
            typeof(Queryable).Assembly,
            typeof(System.Linq.Expressions.Expression).Assembly,
            typeof(List<>).Assembly,
            typeof(Task).Assembly,
        };

        foreach (var assemblyName in new[]
        {
            "System.Runtime",
            "netstandard",
            "System.Collections",
            "System.Linq",
            "System.Linq.Queryable",
            "System.Linq.Expressions",
            "System.Threading.Tasks",
            "Microsoft.CodeAnalysis.Workspaces",
            "Microsoft.CodeAnalysis.Features",
            "Microsoft.CodeAnalysis.CSharp.Workspaces",
            "System.Composition.AttributedModel",
            "System.Composition.Runtime",
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
            .GroupBy(reference => reference.Display, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First());
    }

    private sealed record AppliedFixResult
    {
        public required string PrimaryDocumentText { get; init; }

        public required Solution ChangedSolution { get; init; }
    }
}
