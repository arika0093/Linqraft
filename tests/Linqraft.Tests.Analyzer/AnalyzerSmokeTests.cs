using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Linqraft.Analyzer;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Linqraft.Tests.Analyzer;

public sealed class AnalyzerSmokeTests
{
    [Test]
    public async Task Missing_capture_reports_LQRE001()
    {
        const string source = """
            using System;
            using System.Linq;
            using Linqraft;

            public class Entity
            {
                public int Id { get; set; }
                public int Value { get; set; }
            }

            public class EntityDto { }

            namespace Linqraft
            {
                public static class SelectExprExtensions
                {
                    public static IQueryable<TResult> SelectExpr<TIn, TResult>(this IQueryable<TIn> query, Func<TIn, object> selector)
                        where TIn : class => throw null!;
                }
            }

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

        var diagnostics = await GetDiagnosticsAsync(source);
        diagnostics.Select(diagnostic => diagnostic.Id).ShouldContain("LQRE001");
    }

    [Test]
    public async Task Matching_capture_does_not_report_capture_diagnostics()
    {
        const string source = """
            using System;
            using System.Linq;
            using Linqraft;

            namespace Linqraft
            {
                public static class SelectExprExtensions
                {
                    public static IQueryable<TResult> SelectExpr<TIn, TResult>(this IQueryable<TIn> query, Func<TIn, object> selector, Func<object> capture)
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
                    }, capture: () => threshold);
                }
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(source);
        diagnostics.Select(diagnostic => diagnostic.Id).ShouldNotContain("LQRE001");
        diagnostics.Select(diagnostic => diagnostic.Id).ShouldNotContain("LQRW003");
    }

    [Test]
    public async Task Mapper_method_parameters_do_not_require_capture()
    {
        const string source = """
            using System;
            using System.Linq;
            using Linqraft;

            namespace Linqraft
            {
                [AttributeUsage(AttributeTargets.Method)]
                public sealed class LinqraftMappingAttribute : Attribute
                {
                }

                public sealed class LinqraftMapper<T> where T : class
                {
                    public IQueryable<TResult> Select<TResult>(Func<T, object> selector) => throw null!;
                }
            }

            public class Entity
            {
                public int Id { get; set; }
                public int Value { get; set; }
            }

            public class EntityDto { }

            public static partial class QueryHolder
            {
                [LinqraftMapping]
                internal static IQueryable<EntityDto> ToDto(this LinqraftMapper<Entity> source, int threshold)
                {
                    return source.Select<EntityDto>(entity => new
                    {
                        entity.Id,
                        IsLarge = entity.Value > threshold,
                    });
                }
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(source);
        diagnostics.Select(diagnostic => diagnostic.Id).ShouldNotContain("LQRE001");
        diagnostics.Select(diagnostic => diagnostic.Id).ShouldNotContain("LQRS010");
    }

    [Test]
    public async Task Anonymous_capture_reports_LQRW004()
    {
        const string source = """
            using System;
            using System.Linq;
            using Linqraft;

            namespace Linqraft
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
                public IQueryable<EntityDto> Project(IQueryable<Entity> source, int threshold)
                {
                    return source.SelectExpr<Entity, EntityDto>(entity => new
                    {
                        entity.Id,
                        IsLarge = entity.Value > threshold,
                    }, capture: new { threshold });
                }
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(source);
        diagnostics.Select(diagnostic => diagnostic.Id).ShouldContain("LQRW004");
    }

    [Test]
    public async Task Queryable_select_anonymous_reports_LQRS002()
    {
        const string source = """
            using System.Linq;
            using Linqraft;

            public class Entity
            {
                public int Id { get; set; }
            }

            public class QueryHolder
            {
                public IQueryable<object> Project(IQueryable<Entity> source)
                {
                    return source.Select(entity => new { entity.Id });
                }
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(source);
        var diagnostic = diagnostics.Single(current => current.Id == "LQRS002");

        diagnostic.Severity.ShouldBe(DiagnosticSeverity.Hidden);
        diagnostic
            .GetMessage()
            .ShouldBe("IQueryable.Select can be converted to UseLinqraft().Select(...)");
        diagnostics.Select(current => current.Id).ShouldNotContain("LQRS005");
    }

    [Test]
    public async Task Queryable_select_anonymous_with_null_ternary_reports_LQRS005()
    {
        const string source = """
            using System.Linq;
            using Linqraft;

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
                public IQueryable<object> Project(IQueryable<Entity> source)
                {
                    return source.Select(entity => new
                    {
                        ChildData = entity.Child != null ? new { entity.Child.Id } : null,
                    });
                }
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(source);
        var diagnostic = diagnostics.Single(current => current.Id == "LQRS005");

        diagnostic.Severity.ShouldBe(DiagnosticSeverity.Info);
        diagnostic
            .GetMessage()
            .ShouldBe("IQueryable.Select can be converted to UseLinqraft().Select(...)");
        diagnostics.Select(current => current.Id).ShouldNotContain("LQRS002");
    }

    [Test]
    public async Task Enumerable_select_anonymous_does_not_report_LQRS002()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Linq;
            using Linqraft;

            public class Entity
            {
                public int Id { get; set; }
            }

            public class QueryHolder
            {
                public IEnumerable<object> Project(IEnumerable<Entity> source)
                {
                    return source.Select(entity => new { entity.Id });
                }
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(source);
        diagnostics.Select(diagnostic => diagnostic.Id).ShouldNotContain("LQRS002");
    }

    [Test]
    public async Task Hash_namespace_usage_reports_LQRW001()
    {
        const string source = """
            using Demo.LinqraftGenerated_1234ABCD;

            public class Sample { }
            """;

        var diagnostics = await GetDiagnosticsAsync(source);
        diagnostics.Select(diagnostic => diagnostic.Id).ShouldContain("LQRW001");
    }

    [Test]
    public async Task Auto_generated_dto_usage_reports_LQRW002()
    {
        const string source = """
            namespace Linqraft
            {
                internal sealed class LinqraftAutoGeneratedDtoAttribute : System.Attribute { }
            }

            [Linqraft.LinqraftAutoGeneratedDto]
            public sealed class GeneratedItemsDto
            {
            }

            public sealed class QueryModel
            {
                public GeneratedItemsDto Item { get; set; } = new();
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(source);
        diagnostics.Select(diagnostic => diagnostic.Id).ShouldContain("LQRW002");
    }

    [Test]
    public async Task Untyped_selectexpr_reports_LQRS001()
    {
        const string source = """
            using System;
            using System.Linq;
            using Linqraft;

            namespace Linqraft
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

        var diagnostics = await GetDiagnosticsAsync(source);
        diagnostics.Select(diagnostic => diagnostic.Id).ShouldContain("LQRS001");
    }

    [Test]
    public async Task UseLinqraft_select_does_not_report_queryable_select_diagnostic()
    {
        const string source = """
            using System;
            using System.Linq;
            using Linqraft;

            namespace Linqraft
            {
                public interface IProjectionHelper
                {
                }

                public sealed class LinqraftQuery<T>
                    where T : class
                {
                    public IQueryable<TResult> Select<TResult>(Func<T, TResult> selector) => throw null!;
                    public IQueryable<TResult> Select<TResult>(Func<T, IProjectionHelper, TResult> selector) => throw null!;
                    public IQueryable<TResult> Select<TResult>(Func<T, TResult> selector, Func<object> capture) => throw null!;
                    public IQueryable<TResult> Select<TResult>(Func<T, IProjectionHelper, TResult> selector, Func<object> capture) => throw null!;
                }

                public static class LinqraftQueryExtensions
                {
                    public static LinqraftQuery<T> UseLinqraft<T>(this IQueryable<T> query)
                        where T : class => throw null!;
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
                    return source
                        .UseLinqraft()
                        .Select<EntityDto>(entity => new
                        {
                            entity.Id,
                        });
                }
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(source);
        diagnostics.Select(diagnostic => diagnostic.Id).ShouldNotContain("LQRS003");
        diagnostics.Select(diagnostic => diagnostic.Id).ShouldNotContain("LQRS002");
    }

    [Test]
    public async Task UseLinqraft_typed_anonymous_projection_reports_LQRS010()
    {
        const string source = """
            using System;
            using System.Linq;
            using Linqraft;

            namespace Linqraft
            {
                public interface IProjectionHelper
                {
                }

                public sealed class LinqraftQuery<T>
                    where T : class
                {
                    public IQueryable<TResult> Select<TResult>(Func<T, TResult> selector) => throw null!;
                    public IQueryable<TResult> Select<TResult>(Func<T, TResult> selector, Func<object> capture) => throw null!;
                }

                public static class LinqraftQueryExtensions
                {
                    public static LinqraftQuery<T> UseLinqraft<T>(this IQueryable<T> query)
                        where T : class => throw null!;
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
                    return source.UseLinqraft().Select<EntityDto>(entity => new
                    {
                        entity.Id,
                    });
                }
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(source);
        diagnostics.Select(diagnostic => diagnostic.Id).ShouldContain("LQRS010");
    }

    [Test]
    public async Task UseLinqraft_untyped_anonymous_projection_does_not_report_LQRS010()
    {
        const string source = """
            using System;
            using System.Linq;
            using Linqraft;

            namespace Linqraft
            {
                public interface IProjectionHelper
                {
                }

                public sealed class LinqraftQuery<T>
                    where T : class
                {
                    public IQueryable<TResult> Select<TResult>(Func<T, TResult> selector) => throw null!;
                }

                public static class LinqraftQueryExtensions
                {
                    public static LinqraftQuery<T> UseLinqraft<T>(this IQueryable<T> query)
                        where T : class => throw null!;
                }
            }

            public class Entity
            {
                public int Id { get; set; }
            }

            public class QueryHolder
            {
                public IQueryable<object> Project(IQueryable<Entity> source)
                {
                    return source.UseLinqraft().Select(entity => new
                    {
                        entity.Id,
                    });
                }
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(source);
        diagnostics.Select(diagnostic => diagnostic.Id).ShouldNotContain("LQRS010");
    }

    [Test]
    public async Task Queryable_select_named_reports_LQRS003()
    {
        const string source = """
            using System.Linq;
            using Linqraft;

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

        var diagnostics = await GetDiagnosticsAsync(source);
        var diagnostic = diagnostics.Single(current => current.Id == "LQRS003");

        diagnostic.Severity.ShouldBe(DiagnosticSeverity.Hidden);
        diagnostic
            .GetMessage()
            .ShouldBe("IQueryable.Select can be converted to UseLinqraft().Select(...)");
        diagnostics.Select(current => current.Id).ShouldNotContain("LQRS006");
    }

    [Test]
    public async Task Queryable_select_named_with_null_ternary_reports_LQRS006()
    {
        const string source = """
            using System.Linq;
            using Linqraft;

            namespace Linqraft
            {
                public static class AsLeftJoinExtensions
                {
                    public static T AsLeftJoin<T>(this T value) => value;
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

            public class EntityDto
            {
                public object? ChildData { get; set; }
            }

            public class QueryHolder
            {
                public IQueryable<EntityDto> Project(IQueryable<Entity> source)
                {
                    return source.Select(entity => new EntityDto
                    {
                        ChildData = entity.Child != null ? new { entity.Child.Name } : null,
                    });
                }
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(source);
        var diagnostic = diagnostics.Single(current => current.Id == "LQRS006");

        diagnostic.Severity.ShouldBe(DiagnosticSeverity.Info);
        diagnostic
            .GetMessage()
            .ShouldBe("IQueryable.Select can be converted to UseLinqraft().Select(...)");
        diagnostics.Select(current => current.Id).ShouldNotContain("LQRS003");
    }

    [Test]
    public async Task Projection_helper_hook_inside_selectexpr_reports_only_LQRS010()
    {
        const string source = """
            using System;
            using System.Linq;
            using Linqraft;

            namespace Linqraft
            {
                public interface IProjectionHelper
                {
                    T AsLeftJoin<T>(T value);
                }

                public static class SelectExprExtensions
                {
                    public static IQueryable<TResult> SelectExpr<TIn, TResult>(this IQueryable<TIn> query, Func<TIn, IProjectionHelper, object> selector)
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

            public class QueryHolder
            {
                public IQueryable<object> Project(IQueryable<Entity> source)
                {
                    return source.SelectExpr<Entity, object>((entity, helper) => new
                    {
                        Name = helper.AsLeftJoin(entity.Child).Name,
                    });
                }
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(source);
        diagnostics.Select(diagnostic => diagnostic.Id).ShouldBe(["LQRS010"]);
    }

    [Test]
    public async Task Ternary_inside_selectexpr_reports_LQRS004()
    {
        const string source = """
            using System;
            using System.Linq;
            using Linqraft;

            namespace Linqraft
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

        var diagnostics = await GetDiagnosticsAsync(source);
        diagnostics.Select(diagnostic => diagnostic.Id).ShouldContain("LQRS004");
    }

    [Test]
    public async Task Unused_capture_reports_LQRW003()
    {
        const string source = """
            using System;
            using System.Linq;
            using Linqraft;

            namespace Linqraft
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

        var diagnostics = await GetDiagnosticsAsync(source);
        diagnostics.Select(diagnostic => diagnostic.Id).ShouldContain("LQRW003");
    }

    [Test]
    public async Task Anonymous_groupby_key_reports_LQRE002()
    {
        const string source = """
            using System;
            using System.Linq;
            using Linqraft;

            namespace Linqraft
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

        var diagnostics = await GetDiagnosticsAsync(source);
        diagnostics.Select(diagnostic => diagnostic.Id).ShouldContain("LQRE002");
    }

    [Test]
    public async Task Anonymous_groupby_key_without_selectexpr_does_not_report_LQRE002()
    {
        const string source = """
            using System.Linq;
            using Linqraft;

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
                        .Select(group => group.Key);
                }
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(source);
        diagnostics.Select(diagnostic => diagnostic.Id).ShouldNotContain("LQRE002");
    }

    [Test]
    public async Task Api_controller_without_response_metadata_reports_LQRF001()
    {
        const string source = """
            using System;
            using System.Linq;
            using Linqraft;

            namespace Microsoft.AspNetCore.Mvc
            {
                public sealed class ApiControllerAttribute : Attribute { }
                public interface IActionResult { }
                public abstract class ControllerBase { }
            }

            namespace Linqraft
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

        var diagnostics = await GetDiagnosticsAsync(source);
        diagnostics.Select(diagnostic => diagnostic.Id).ShouldContain("LQRF001");
    }

    [Test]
    public async Task Api_controller_with_response_metadata_does_not_report_LQRF001()
    {
        const string source = """
            using System;
            using System.Linq;
            using Linqraft;

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

            namespace Linqraft
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
                [Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(EntityDto))]
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

        var diagnostics = await GetDiagnosticsAsync(source);
        diagnostics.Select(diagnostic => diagnostic.Id).ShouldNotContain("LQRF001");
    }

    [Test]
    public async Task Null_conditional_chained_member_access_does_not_trigger_LQRE001()
    {
        const string source = """
            using System;
            using System.Linq;
            using Linqraft;

            public class Product
            {
                public int ProductId { get; set; }
                public ProductDetail? Detail { get; set; }
            }

            public class ProductDetail
            {
                public string Description { get; set; } = "";
            }

            public class OrderLine
            {
                public int Id { get; set; }
                public Product? Product { get; set; }
                public DateTimeOffset CreatedAt { get; set; }
            }

            public class OrderLineViewDto { }

            public static class QueryHelper
            {
                public static void QueryOrderLines(IQueryable<OrderLine> query)
                {
                    query.SelectExpr<OrderLine, OrderLineViewDto>(l => new
                    {
                        ProductDescription = l.Product?.Detail.Description,
                        l.CreatedAt,
                    });
                }
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(source);
        diagnostics.Select(diagnostic => diagnostic.Id).ShouldNotContain("LQRE001");
    }

    private static async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(string source)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
        var syntaxTree = CSharpSyntaxTree.ParseText(source, parseOptions, path: "Test0.cs");
        var compilation = CSharpCompilation.Create(
            assemblyName: "AnalyzerTests",
            syntaxTrees: [syntaxTree],
            references: GetMetadataReferences(),
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable
            )
        );

        var analyzer = new LinqraftCompositeAnalyzer();
        var compilationWithAnalyzers = compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(analyzer)
        );
        return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
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
}
