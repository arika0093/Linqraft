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

namespace Linqraft.Tests.Analyzer;

public sealed class AnalyzerCodeFixSmokeTests
{
    [Test]
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

    [Test]
    public async Task Missing_capture_code_fix_appends_missing_member_to_existing_capture_object()
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
                public int Value { get; set; }
            }

            public class EntityDto { }

            public class QueryHolder
            {
                public IQueryable<EntityDto> Project(IQueryable<Entity> source, int offset, int threshold)
                {
                    return source.SelectExpr<Entity, EntityDto>(entity => new
                    {
                        entity.Id,
                        Adjusted = entity.Value + offset + threshold,
                    }, capture: new { offset });
                }
            }
            """;

        var fixedText = (
            await ApplyFixAsync(source, "LQRE001", "Add capture 'threshold'")
        ).PrimaryDocumentText;

        fixedText.ShouldContain("capture: new { offset, threshold }");
    }

    [Test]
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

        var fixedText = (
            await ApplyFixAsync(source, "LQRS005", "Remove capture")
        ).PrimaryDocumentText;

        fixedText.ShouldNotContain("capture:");
    }

    [Test]
    public async Task Unused_capture_code_fix_removes_only_requested_capture_member()
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
                public int Value { get; set; }
            }

            public class EntityDto { }

            public class QueryHolder
            {
                public IQueryable<EntityDto> Project(IQueryable<Entity> source, int threshold, int offset)
                {
                    return source.SelectExpr<Entity, EntityDto>(entity => new
                    {
                        entity.Id,
                        Adjusted = entity.Value + threshold,
                    }, capture: new { threshold, offset });
                }
            }
            """;

        var fixedText = (
            await ApplyFixAsync(source, "LQRS005", "Remove capture 'offset'")
        ).PrimaryDocumentText;

        fixedText.ShouldContain("capture: new");
        fixedText.ShouldContain("threshold");
        fixedText.ShouldNotContain("capture: new { threshold, offset }");
    }

    [Test]
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

    [Test]
    public async Task Anonymous_select_code_fix_converts_to_selectexpr()
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
                    return source.Select(entity => new { entity.Id });
                }
            }
            """;

        var result = await ApplyFixAsync(source, "LQRS002", "Convert to SelectExpr");
        var fixedText = result.PrimaryDocumentText;
        var compilationErrors = await GetCompilationErrorsAsync(result.ChangedSolution);

        compilationErrors.ShouldBeEmpty();
        fixedText.ShouldContain("source.SelectExpr(entity => new { entity.Id })");
    }

    [Test]
    public async Task Anonymous_select_code_fix_simplifies_null_check_ternary()
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
                    return source.Select(entity => new
                    {
                        ChildData = entity.Child != null ? new { entity.Child.Id } : null,
                    });
                }
            }
            """;

        var result = await ApplyFixAsync(source, "LQRS002", "Convert to SelectExpr");
        var fixedText = result.PrimaryDocumentText;
        var compilationErrors = await GetCompilationErrorsAsync(result.ChangedSolution);

        compilationErrors.ShouldBeEmpty();
        fixedText.ShouldContain("ChildData = new { Id = entity.Child?.Id }");
    }

    [Test]
    public async Task Anonymous_select_explicit_dto_code_fix_uses_existing_dto_type()
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
            }

            public class ProjectDto
            {
                public int Id { get; set; }
            }

            public class QueryHolder
            {
                public IQueryable<ProjectDto> Project(IQueryable<Entity> source)
                {
                    return source.Select(entity => new { entity.Id });
                }
            }
            """;

        var result = await ApplyFixAsync(source, "LQRS002", "SelectExpr<T, TDto>");
        var fixedText = result.PrimaryDocumentText;
        var compilationErrors = await GetCompilationErrorsAsync(result.ChangedSolution);

        compilationErrors.ShouldBeEmpty();
        fixedText.ShouldContain("source.SelectExpr<Entity, ProjectDto>(entity => new { entity.Id })");
    }

    [Test]
    public async Task Anonymous_select_explicit_dto_code_fix_simplifies_null_check_ternary()
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

            public class Child
            {
                public int Id { get; set; }
            }

            public class Entity
            {
                public Child? Child { get; set; }
            }

            public class ProjectDto
            {
                public object? ChildData { get; set; }
            }

            public class QueryHolder
            {
                public IQueryable<ProjectDto> Project(IQueryable<Entity> source)
                {
                    return source.Select(entity => new
                    {
                        ChildData = entity.Child != null ? new { entity.Child.Id } : null,
                    });
                }
            }
            """;

        var result = await ApplyFixAsync(source, "LQRS002", "SelectExpr<T, TDto>");
        var fixedText = result.PrimaryDocumentText;
        var compilationErrors = await GetCompilationErrorsAsync(result.ChangedSolution);

        compilationErrors.ShouldBeEmpty();
        fixedText.ShouldContain("source.SelectExpr<Entity, ProjectDto>(entity => new");
        fixedText.ShouldContain("ChildData = new { Id = entity.Child?.Id }");
    }

    [Test]
    public async Task Anonymous_select_benchmark_code_fix_simplifies_nested_projection_ternaries()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using System.Linq;

            namespace System.Linq
            {
                public static class SelectExprExtensions
                {
                    public static IQueryable<TResult> SelectExpr<TIn, TResult>(this IQueryable<TIn> query, Func<TIn, TResult> selector)
                        where TIn : class => throw null!;
                }
            }

            public class SampleChildLeaf
            {
                public int Id { get; set; }
                public string? Grault { get; set; }
            }

            public class SampleChildThird
            {
                public int Id { get; set; }
                public string Corge { get; set; } = string.Empty;
                public SampleChildLeaf? Child { get; set; }
            }

            public class SampleChildSecond
            {
                public int Id { get; set; }
                public string? Quux { get; set; }
            }

            public class SampleChildInner
            {
                public int Id { get; set; }
                public string? Qux { get; set; }
            }

            public class SampleChildClass
            {
                public int Id { get; set; }
                public string Baz { get; set; } = string.Empty;
                public SampleChildInner? Child { get; set; }
            }

            public class SampleClass
            {
                public int Id { get; set; }
                public string Foo { get; set; } = string.Empty;
                public string Bar { get; set; } = string.Empty;
                public IEnumerable<SampleChildClass> Childs { get; set; } = Array.Empty<SampleChildClass>();
                public SampleChildSecond? Child2 { get; set; }
                public SampleChildThird Child3 { get; set; } = new();
            }

            public class QueryHolder
            {
                public object Project(IQueryable<SampleClass> source)
                {
                    return source.Select(s => new
                    {
                        s.Id,
                        s.Foo,
                        s.Bar,
                        Childs = s.Childs.Select(c => new
                        {
                            c.Id,
                            c.Baz,
                            ChildId = c.Child != null ? (int?)c.Child.Id : null,
                            ChildQux = c.Child != null ? c.Child.Qux : null,
                        }),
                        Child2Id = s.Child2 != null ? (int?)s.Child2.Id : null,
                        Child2Quux = s.Child2 != null ? s.Child2.Quux : null,
                        Child3Id = s.Child3.Id,
                        Child3Corge = s.Child3.Corge,
                        Child3ChildId = s.Child3 != null && s.Child3.Child != null ? (int?)s.Child3.Child.Id : null,
                        Child3ChildGrault = s.Child3 != null && s.Child3.Child != null ? s.Child3.Child.Grault : null,
                    });
                }
            }
            """;

        var result = await ApplyFixAsync(source, "LQRS002", "Convert to SelectExpr");
        var fixedText = result.PrimaryDocumentText;
        var compilationErrors = await GetCompilationErrorsAsync(result.ChangedSolution);

        compilationErrors.ShouldBeEmpty();
        fixedText.ShouldContain("source.SelectExpr(s => new");
        fixedText.ShouldContain("Childs = s.Childs.Select(c => new");
        fixedText.ShouldContain("ChildId = c.Child?.Id");
        fixedText.ShouldContain("ChildQux = c.Child?.Qux");
        fixedText.ShouldContain("Child2Id = s.Child2?.Id");
        fixedText.ShouldContain("Child2Quux = s.Child2?.Quux");
        fixedText.ShouldContain("Child3ChildId = s.Child3?.Child?.Id");
        fixedText.ShouldContain("Child3ChildGrault = s.Child3?.Child?.Grault");
        fixedText.ShouldNotContain("c.Child != null ? (int?)c.Child.Id : null");
        fixedText.ShouldNotContain(
            "s.Child3 != null && s.Child3.Child != null ? (int?)s.Child3.Child.Id : null"
        );

        var anonymousLines = fixedText.Replace("\r\n", "\n").Split('\n');
        var anonymousChildsIndex = Array.FindIndex(
            anonymousLines,
            line => line.Contains("Childs = s.Childs.Select(c => new", StringComparison.Ordinal)
        );
        var anonymousChildIdIndex = Array.FindIndex(
            anonymousLines,
            line => line.Contains("ChildId = c.Child?.Id", StringComparison.Ordinal)
        );
        var anonymousChild2IdIndex = Array.FindIndex(
            anonymousLines,
            line => line.Contains("Child2Id = s.Child2?.Id", StringComparison.Ordinal)
        );

        anonymousChildsIndex.ShouldBeGreaterThanOrEqualTo(0);
        anonymousChildIdIndex.ShouldBeGreaterThan(anonymousChildsIndex);
        anonymousChild2IdIndex.ShouldBeGreaterThan(anonymousChildIdIndex);
        CountLeadingSpaces(anonymousLines[anonymousChildIdIndex]).ShouldBeGreaterThan(
            CountLeadingSpaces(anonymousLines[anonymousChildsIndex])
        );
        CountLeadingSpaces(anonymousLines[anonymousChild2IdIndex]).ShouldBe(
            CountLeadingSpaces(anonymousLines[anonymousChildsIndex])
        );
    }

    [Test]
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
        fixedText.ShouldContain(
            "[global::Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(EntityDto))]"
        );
    }

    [Test]
    public async Task Named_select_explicit_dto_code_fix_simplifies_null_check_ternary()
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

            public class Child
            {
                public string? Name { get; set; }
            }

            public class Entity
            {
                public Child? Child { get; set; }
            }

            public class ChildDto
            {
                public string? Name { get; set; }
            }

            public class ProjectDto
            {
                public object? ChildData { get; set; }
            }

            public class QueryHolder
            {
                public IQueryable<ProjectDto> Project(IQueryable<Entity> source)
                {
                    return source.Select(entity => new ProjectDto
                    {
                        ChildData = entity.Child != null ? new ChildDto { Name = entity.Child.Name } : null,
                    });
                }
            }
            """;

        var result = await ApplyFixAsync(source, "LQRS003", "Convert to SelectExpr<T, TDto>");
        var fixedText = result.PrimaryDocumentText;
        var compilationErrors = await GetCompilationErrorsAsync(result.ChangedSolution);

        compilationErrors.ShouldBeEmpty();
        fixedText.ShouldContain("source.SelectExpr<Entity, ProjectDto>(entity => new");
        fixedText.ShouldContain("ChildData = new { Name = entity.Child?.Name }");
    }

    [Test]
    public async Task Named_select_benchmark_code_fix_simplifies_nested_projection_ternaries()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using System.Linq;

            namespace System.Linq
            {
                public static class SelectExprExtensions
                {
                    public static IQueryable<TResult> SelectExpr<TIn, TResult>(this IQueryable<TIn> query, Func<TIn, object> selector)
                        where TIn : class => throw null!;
                }
            }

            public class SampleChildLeaf
            {
                public int Id { get; set; }
                public string? Grault { get; set; }
            }

            public class SampleChildThird
            {
                public int Id { get; set; }
                public string Corge { get; set; } = string.Empty;
                public SampleChildLeaf? Child { get; set; }
            }

            public class SampleChildSecond
            {
                public int Id { get; set; }
                public string? Quux { get; set; }
            }

            public class SampleChildInner
            {
                public int Id { get; set; }
                public string? Qux { get; set; }
            }

            public class SampleChildClass
            {
                public int Id { get; set; }
                public string Baz { get; set; } = string.Empty;
                public SampleChildInner? Child { get; set; }
            }

            public class SampleClass
            {
                public int Id { get; set; }
                public string Foo { get; set; } = string.Empty;
                public string Bar { get; set; } = string.Empty;
                public IEnumerable<SampleChildClass> Childs { get; set; } = Array.Empty<SampleChildClass>();
                public SampleChildSecond? Child2 { get; set; }
                public SampleChildThird Child3 { get; set; } = new();
            }

            public class ManualSampleChildDto
            {
                public int Id { get; set; }
                public string Baz { get; set; } = string.Empty;
                public int? ChildId { get; set; }
                public string? ChildQux { get; set; }
            }

            public class ManualSampleClassDto
            {
                public int Id { get; set; }
                public string Foo { get; set; } = string.Empty;
                public string Bar { get; set; } = string.Empty;
                public IEnumerable<ManualSampleChildDto>? Childs { get; set; }
                public int? Child2Id { get; set; }
                public string? Child2Quux { get; set; }
                public int Child3Id { get; set; }
                public string Child3Corge { get; set; } = string.Empty;
                public int? Child3ChildId { get; set; }
                public string? Child3ChildGrault { get; set; }
            }

            public class QueryHolder
            {
                public IQueryable<ManualSampleClassDto> Project(IQueryable<SampleClass> source)
                {
                    return source.Select(s => new ManualSampleClassDto
                    {
                        Id = s.Id,
                        Foo = s.Foo,
                        Bar = s.Bar,
                        Childs = s.Childs.Select(c => new ManualSampleChildDto
                        {
                            Id = c.Id,
                            Baz = c.Baz,
                            ChildId = c.Child != null ? c.Child.Id : null,
                            ChildQux = c.Child != null ? c.Child.Qux : null,
                        }),
                        Child2Id = s.Child2 != null ? s.Child2.Id : null,
                        Child2Quux = s.Child2 != null ? s.Child2.Quux : null,
                        Child3Id = s.Child3.Id,
                        Child3Corge = s.Child3.Corge,
                        Child3ChildId = s.Child3 != null && s.Child3.Child != null ? s.Child3.Child.Id : null,
                        Child3ChildGrault = s.Child3 != null && s.Child3.Child != null ? s.Child3.Child.Grault : null,
                    });
                }
            }
            """;

        var result = await ApplyFixAsync(source, "LQRS003", "Convert to SelectExpr<T, TDto>");
        var fixedText = result.PrimaryDocumentText;
        var compilationErrors = await GetCompilationErrorsAsync(result.ChangedSolution);

        compilationErrors.ShouldBeEmpty();
        fixedText.ShouldContain("source.SelectExpr<SampleClass, ManualSampleClassDto>(s => new");
        fixedText.ShouldContain("Childs = s.Childs.Select(c => new");
        fixedText.ShouldContain("ChildId = c.Child?.Id");
        fixedText.ShouldContain("ChildQux = c.Child?.Qux");
        fixedText.ShouldContain("Child2Id = s.Child2?.Id");
        fixedText.ShouldContain("Child2Quux = s.Child2?.Quux");
        fixedText.ShouldContain("Child3ChildId = s.Child3?.Child?.Id");
        fixedText.ShouldContain("Child3ChildGrault = s.Child3?.Child?.Grault");
        fixedText.ShouldNotContain("new ManualSampleChildDto");
        fixedText.ShouldNotContain("c.Child != null ? c.Child.Id : null");
        fixedText.ShouldNotContain(
            "s.Child3 != null && s.Child3.Child != null ? s.Child3.Child.Id : null"
        );

        var namedLines = fixedText.Replace("\r\n", "\n").Split('\n');
        var namedChildsIndex = Array.FindIndex(
            namedLines,
            line => line.Contains("Childs = s.Childs.Select(c => new", StringComparison.Ordinal)
        );
        var namedChildIdIndex = Array.FindIndex(
            namedLines,
            line => line.Contains("ChildId = c.Child?.Id", StringComparison.Ordinal)
        );
        var namedChild2IdIndex = Array.FindIndex(
            namedLines,
            line => line.Contains("Child2Id = s.Child2?.Id", StringComparison.Ordinal)
        );

        namedChildsIndex.ShouldBeGreaterThanOrEqualTo(0);
        namedChildIdIndex.ShouldBeGreaterThan(namedChildsIndex);
        namedChild2IdIndex.ShouldBeGreaterThan(namedChildIdIndex);
        CountLeadingSpaces(namedLines[namedChildIdIndex]).ShouldBeGreaterThan(
            CountLeadingSpaces(namedLines[namedChildsIndex])
        );
        CountLeadingSpaces(namedLines[namedChild2IdIndex]).ShouldBe(
            CountLeadingSpaces(namedLines[namedChildsIndex])
        );
    }

    [Test]
    public async Task Named_select_explicit_dto_strict_code_fix_preserves_ternary_pattern()
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

            public class Child
            {
                public string? Name { get; set; }
            }

            public class Entity
            {
                public Child? Child { get; set; }
            }

            public class ChildDto
            {
                public string? Name { get; set; }
            }

            public class ProjectDto
            {
                public object? ChildData { get; set; }
            }

            public class QueryHolder
            {
                public IQueryable<ProjectDto> Project(IQueryable<Entity> source)
                {
                    return source.Select(entity => new ProjectDto
                    {
                        ChildData = entity.Child != null ? new ChildDto { Name = entity.Child.Name } : null,
                    });
                }
            }
            """;

        var result = await ApplyFixAsync(source, "LQRS003", "(strict)");
        var fixedText = result.PrimaryDocumentText;
        var compilationErrors = await GetCompilationErrorsAsync(result.ChangedSolution);

        compilationErrors.ShouldBeEmpty();
        fixedText.ShouldContain("ChildData = entity.Child != null ? new");
        fixedText.ShouldContain("Name = entity.Child.Name");
        fixedText.ShouldNotContain("entity.Child?.Name");
        fixedText.ShouldNotContain("new ChildDto");
    }

    [Test]
    public async Task Named_select_code_fix_can_keep_predefined_dto_shape()
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

            public class EntityDto
            {
                public int Id { get; set; }
            }

            public class QueryHolder
            {
                public IQueryable<EntityDto> Project(IQueryable<Entity> source)
                {
                    return source.Select(entity => new EntityDto
                    {
                        Id = entity.Id,
                    });
                }
            }
            """;

        var result = await ApplyFixAsync(source, "LQRS003", "predefined classes");
        var fixedText = result.PrimaryDocumentText;
        var compilationErrors = await GetCompilationErrorsAsync(result.ChangedSolution);

        compilationErrors.ShouldBeEmpty();
        fixedText.ShouldContain("source.SelectExpr(entity => new EntityDto");
    }

    [Test]
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

        var fixedText = (
            await ApplyFixAsync(source, "LQRS001", "SelectExpr<T, TDto>")
        ).PrimaryDocumentText;

        fixedText.ShouldContain("SelectExpr<Entity, ProjectDto>");
    }

    [Test]
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

    private static async Task<AppliedFixResult> ApplyFixAsync(
        string source,
        string diagnosticId,
        string titleFragment
    )
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

        var actionToApply = actions.FirstOrDefault(action =>
            action.Title.Contains(titleFragment, StringComparison.OrdinalIgnoreCase)
        );
        actionToApply.ShouldNotBeNull();
        var operations = await actionToApply!.GetOperationsAsync(CancellationToken.None);
        var applyChangesOperation = operations.OfType<ApplyChangesOperation>().SingleOrDefault();
        applyChangesOperation.ShouldNotBeNull();
        var changedSolution = applyChangesOperation!.ChangedSolution;
        var changedDocument =
            changedSolution.GetDocument(document.Id)
            ?? changedSolution.Projects.SelectMany(project => project.Documents).First();
        var text = await changedDocument.GetTextAsync();
        return new AppliedFixResult
        {
            PrimaryDocumentText = text.ToString(),
            ChangedSolution = changedSolution,
        };
    }

    private static int CountLeadingSpaces(string value)
    {
        return value.TakeWhile(character => character == ' ').Count();
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

    private static async Task<ImmutableArray<Diagnostic>> GetCompilationErrorsAsync(
        Solution solution
    )
    {
        var builder = ImmutableArray.CreateBuilder<Diagnostic>();
        foreach (var project in solution.Projects)
        {
            var compilation = await project.GetCompilationAsync();
            if (compilation is null)
            {
                continue;
            }

            builder.AddRange(
                compilation
                    .GetDiagnostics()
                    .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            );
        }

        return builder.ToImmutable();
    }

    private static Document CreateDocument(string source)
    {
        var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        var documentId = DocumentId.CreateNewId(projectId);

        var solution = workspace
            .CurrentSolution.AddProject(
                projectId,
                "AnalyzerCodeFixTests",
                "AnalyzerCodeFixTests",
                LanguageNames.CSharp
            )
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

        foreach (
            var assemblyName in new[]
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
            }
        )
        {
            try
            {
                explicitAssemblies.Add(System.Reflection.Assembly.Load(assemblyName));
            }
            catch
            {
                // Best-effort load for compilation metadata.
            }
        }

        return AppDomain
            .CurrentDomain.GetAssemblies()
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
