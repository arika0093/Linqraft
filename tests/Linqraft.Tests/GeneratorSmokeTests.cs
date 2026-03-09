using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Linqraft.SourceGenerator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Linqraft.Tests;

public sealed class GeneratorSmokeTests
{
    [Fact]
    public void ExplicitDto_generation_emits_dto_and_null_checks()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Linq;

            namespace Demo;

            public sealed class Order
            {
                public int Id { get; set; }
                public Customer? Customer { get; set; }
                public List<OrderItem> Items { get; set; } = [];
            }

            public sealed class Customer
            {
                public string Name { get; set; } = string.Empty;
            }

            public sealed class OrderItem
            {
                public string Name { get; set; } = string.Empty;
            }

            public sealed class QueryHolder
            {
                public IQueryable<OrderDto> Project(IQueryable<Order> source)
                {
                    return source.SelectExpr<Order, OrderDto>(order => new
                    {
                        order.Id,
                        CustomerName = order.Customer?.Name,
                        Items = order.Items.Select(item => new
                        {
                            item.Name,
                        }),
                    });
                }
            }
            """;

        var generated = RunGenerator(source);
        var compilationErrors = GetCompilationErrors(source);

        compilationErrors.ShouldBeEmpty();
        generated.ShouldContain("public partial class OrderDto");
        generated.ShouldContain("order.Customer != null ? (global::System.String?)order.Customer.Name : null");
        generated.ShouldContain("public partial class ItemsDto");
    }

    [Fact]
    public void PredefinedDto_generation_keeps_existing_type()
    {
        const string source = """
            using System.Linq;

            namespace Demo;

            public sealed class Product
            {
                public int Id { get; set; }
            }

            public sealed class ProductDto
            {
                public int Id { get; set; }
            }

            public sealed class QueryHolder
            {
                public IQueryable<ProductDto> Project(IQueryable<Product> source)
                {
                    return source.SelectExpr(product => new ProductDto
                    {
                        Id = product.Id,
                    });
                }
            }
            """;

        var generated = RunGenerator(source);
        var compilationErrors = GetCompilationErrors(source);

        compilationErrors.ShouldBeEmpty();
        generated.ShouldContain("new global::Demo.ProductDto");
        generated.ShouldNotContain("public partial class ProductDto");
    }

    [Fact]
    public void Capture_generation_uses_capture_helper()
    {
        const string source = """
            using System.Linq;

            namespace Demo;

            public sealed class Entity
            {
                public int Id { get; set; }
                public int Value { get; set; }
            }

            public sealed class QueryHolder
            {
                public IQueryable<EntityDto> Project(IQueryable<Entity> source, int threshold)
                {
                    return source.SelectExpr<Entity, EntityDto>(
                        entity => new
                        {
                            entity.Id,
                            IsLarge = entity.Value > threshold,
                        },
                        capture: new { threshold });
                }
            }
            """;

        var generated = RunGenerator(source);
        var compilationErrors = GetCompilationErrors(source);

        compilationErrors.ShouldBeEmpty();
        generated.ShouldContain("LinqraftCaptureHelper.GetRequired<int>(capture, \"threshold\")");
        generated.ShouldContain("public partial class EntityDto");
    }

    [Fact]
    public void Mapping_generation_emits_projection_method()
    {
        const string source = """
            using System.Linq;

            namespace Demo;

            public sealed class Order
            {
                public int Id { get; set; }
            }

            [Linqraft.LinqraftMappingGenerate]
            internal class OrderMappingDeclare : Linqraft.LinqraftMappingDeclare<Order>
            {
                protected override void DefineMapping()
                {
                    Source.SelectExpr<Order, OrderDto>(order => new
                    {
                        order.Id,
                    });
                }
            }
            """;

        var generated = RunGenerator(source);
        var compilationErrors = GetCompilationErrors(source);

        compilationErrors.ShouldBeEmpty();
        generated.ShouldContain("ProjectToOrder");
        generated.ShouldContain("OrderMappingDeclare_");
    }

    [Fact]
    public void Static_partial_mapping_generation_honors_custom_method_name()
    {
        const string source = """
            using System.Linq;

            namespace Demo;

            public sealed class Order
            {
                public int Id { get; set; }
            }

            public static partial class OrderQueries
            {
                [Linqraft.LinqraftMappingGenerate("ProjectToSummary")]
                internal static IQueryable<OrderDto> Template(this IQueryable<Order> source)
                {
                    return source.SelectExpr<Order, OrderDto>(order => new
                    {
                        order.Id,
                    });
                }
            }
            """;

        var generated = RunGenerator(source);
        var diagnostics = GetGeneratorDiagnostics(source);
        var compilationErrors = GetCompilationErrors(source);

        diagnostics.ShouldBeEmpty();
        compilationErrors.ShouldBeEmpty();
        generated.ShouldContain("static partial class OrderQueries");
        generated.ShouldContain("ProjectToSummary");
    }

    [Fact]
    public void Record_and_accessor_configuration_changes_generated_shape()
    {
        const string source = """
            using System.Linq;

            namespace Demo;

            public sealed class Entity
            {
                public int Id { get; set; }
            }

            public sealed class QueryHolder
            {
                public IQueryable<EntityDto> Project(IQueryable<Entity> source)
                {
                    return source.SelectExpr<Entity, EntityDto>(entity => new
                    {
                        entity.Id,
                    });
                }
            }
            """;

        var generated = RunGenerator(
            source,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["build_property.LinqraftRecordGenerate"] = "true",
                ["build_property.LinqraftPropertyAccessor"] = "GetAndInit",
                ["build_property.LinqraftHasRequired"] = "false",
            }
        );
        var compilationErrors = GetCompilationErrors(
            source,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["build_property.LinqraftRecordGenerate"] = "true",
                ["build_property.LinqraftPropertyAccessor"] = "GetAndInit",
                ["build_property.LinqraftHasRequired"] = "false",
            }
        );

        compilationErrors.ShouldBeEmpty();
        generated.ShouldContain("public partial record EntityDto");
        generated.ShouldContain("Id { get; init; }");
        generated.ShouldNotContain("public required");
    }

    [Fact]
    public void Anonymous_projection_without_type_arguments_matches_example_usage()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Linq;

            namespace Demo;

            public sealed class Order
            {
                public int Id { get; set; }
                public Customer? Customer { get; set; }
                public List<OrderItem> OrderItems { get; set; } = [];
            }

            public sealed class Customer
            {
                public string Name { get; set; } = string.Empty;
            }

            public sealed class OrderItem
            {
                public Product? Product { get; set; }
                public int Quantity { get; set; }
            }

            public sealed class Product
            {
                public string Name { get; set; } = string.Empty;
            }

            public sealed class QueryHolder
            {
                public object Project(IQueryable<Order> source)
                {
                    return source.SelectExpr(order => new
                    {
                        order.Id,
                        CustomerName = order.Customer?.Name,
                        Items = order.OrderItems.Select(item => new
                        {
                            ProductName = item.Product?.Name,
                            item.Quantity,
                        }),
                    }).ToList();
                }
            }
            """;

        var generated = RunGenerator(source);
        var diagnostics = GetGeneratorDiagnostics(source);
        var compilationErrors = GetCompilationErrors(source);

        diagnostics.ShouldBeEmpty();
        compilationErrors.ShouldBeEmpty();
        generated.ShouldContain("public static global::System.Linq.IQueryable<TResult> SelectExpr_");
        generated.ShouldContain("order.Customer != null ? (global::System.String?)order.Customer.Name : null");
    }

    [Fact]
    public void Minimum_sample_program_shape_compiles()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Linq;

            namespace Demo;

            public class Order
            {
                public int Id { get; set; }
                public Customer? Customer { get; set; }
                public List<OrderItem> OrderItems { get; set; } = [];
            }

            public class Customer
            {
                public string Name { get; set; } = "";
                public Address? Address { get; set; }
            }

            public class Address
            {
                public Country? Country { get; set; }
                public City? City { get; set; }
            }

            public class Country
            {
                public string Name { get; set; } = "";
            }

            public class City
            {
                public string Name { get; set; } = "";
            }

            public class OrderItem
            {
                public Product? Product { get; set; }
                public int Quantity { get; set; }
            }

            public class Product
            {
                public string Name { get; set; } = "";
            }

            public static class SampleData
            {
                public static List<Order> GetOrdersFromOtherSource() => [];
            }

            public static class QueryHolder
            {
                public static object Project()
                {
                    return SampleData
                        .GetOrdersFromOtherSource()
                        .AsQueryable()
                        .SelectExpr(s => new
                        {
                            Id = s.Id,
                            CustomerName = s.Customer?.Name,
                            CustomerCountry = s.Customer?.Address?.Country?.Name,
                            CustomerCity = s.Customer?.Address?.City?.Name,
                            Items = s.OrderItems.Select(oi => new
                            {
                                ProductName = oi.Product?.Name,
                                Quantity = oi.Quantity,
                            }),
                        })
                        .ToList();
                }
            }
            """;

        var generated = RunGenerator(source);
        var diagnostics = GetGeneratorDiagnostics(source);
        var compilationErrors = GetCompilationErrors(source);

        diagnostics.ShouldBeEmpty();
        compilationErrors.ShouldBeEmpty();
        generated.ShouldContain("s.Customer != null && s.Customer.Address != null && s.Customer.Address.Country != null ? (global::System.String?)s.Customer.Address.Country.Name : null");
        generated.ShouldContain("s.Customer != null && s.Customer.Address != null && s.Customer.Address.City != null ? (global::System.String?)s.Customer.Address.City.Name : null");
    }

    [Fact]
    public void Api_sample_controller_shapes_compile()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using System.Linq;

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

            namespace Demo
            {
                public class Order
                {
                    public int Id { get; set; }
                    public Customer? Customer { get; set; }
                    public List<OrderItem> OrderItems { get; set; } = [];
                }

                public class Customer
                {
                    public string Name { get; set; } = "";
                    public Address? Address { get; set; }
                }

                public class Address
                {
                    public Country? Country { get; set; }
                    public City? City { get; set; }
                }

                public class Country
                {
                    public string Name { get; set; } = "";
                }

                public class City
                {
                    public string Name { get; set; } = "";
                }

                public class OrderItem
                {
                    public Product? Product { get; set; }
                    public int Quantity { get; set; }
                }

                public class Product
                {
                    public string Name { get; set; } = "";
                }

                public static class SampleData
                {
                    public static List<Order> GetOrdersFromOtherSource() => [];
                }

                [Microsoft.AspNetCore.Mvc.Route("api/controller/")]
                [Microsoft.AspNetCore.Mvc.ApiController]
                public partial class OrderController : Microsoft.AspNetCore.Mvc.ControllerBase
                {
                    [Microsoft.AspNetCore.Mvc.HttpGet]
                    [Microsoft.AspNetCore.Mvc.Route("get-orders/explicit")]
                    public Microsoft.AspNetCore.Mvc.ActionResult<List<OrderDto>> GetOrdersAsync()
                    {
                        return SampleData
                            .GetOrdersFromOtherSource()
                            .AsQueryable()
                            .SelectExpr<Order, OrderDto>(s => new
                            {
                                Id = s.Id,
                                CustomerName = s.Customer?.Name,
                                CustomerCountry = s.Customer?.Address?.Country?.Name,
                                CustomerCity = s.Customer?.Address?.City?.Name,
                                Items = s.OrderItems.Select(oi => new
                                {
                                    ProductName = oi.Product?.Name,
                                    Quantity = oi.Quantity,
                                }),
                            })
                            .ToList();
                    }

                    [Microsoft.AspNetCore.Mvc.HttpGet]
                    [Microsoft.AspNetCore.Mvc.Route("get-orders/anonymous")]
                    public Microsoft.AspNetCore.Mvc.IActionResult GetOrdersAnonymousAsync()
                    {
                        var results = SampleData
                            .GetOrdersFromOtherSource()
                            .AsQueryable()
                            .SelectExpr(s => new
                            {
                                Id = s.Id,
                                CustomerName = s.Customer?.Name,
                                CustomerCountry = s.Customer?.Address?.Country?.Name,
                                CustomerCity = s.Customer?.Address?.City?.Name,
                                Items = s.OrderItems.Select(oi => new
                                {
                                    ProductName = oi.Product?.Name,
                                    Quantity = oi.Quantity,
                                }),
                            })
                            .ToList();
                        return Ok(results);
                    }
                }
            }
            """;

        var generated = RunGenerator(source);
        var diagnostics = GetGeneratorDiagnostics(source);
        var compilationErrors = GetCompilationErrors(source);

        diagnostics.ShouldBeEmpty();
        compilationErrors.ShouldBeEmpty();
        generated.ShouldContain("public partial class OrderDto");
        generated.ShouldContain("s.Customer != null ? (global::System.String?)s.Customer.Name : null");
    }

    [Fact]
    public void Api_sample_minimal_endpoint_shapes_compile()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using System.Linq;

            namespace Microsoft.Extensions.DependencyInjection
            {
                public interface IServiceCollection { }

                public static class ServiceCollectionExtensions
                {
                    public static IServiceCollection AddOpenApi(this IServiceCollection services) => services;
                    public static IServiceCollection AddControllers(this IServiceCollection services) => services;
                }

                public sealed class ServiceCollection : IServiceCollection { }
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
                    public void UseSwaggerUI(Action<SwaggerUiOptions> configure) => configure(new());
                    public void MapGet(string pattern, Func<object?> handler) { }
                    public void UseHttpsRedirection() { }
                    public void MapControllers() { }
                    public void Run() { }
                }

                public sealed class SwaggerUiOptions
                {
                    public string? RoutePrefix { get; set; }
                    public void SwaggerEndpoint(string url, string name) { }
                }
            }

            namespace Demo
            {
                public class Order
                {
                    public int Id { get; set; }
                    public Customer? Customer { get; set; }
                    public List<OrderItem> OrderItems { get; set; } = [];
                }

                public class Customer
                {
                    public string Name { get; set; } = "";
                    public Address? Address { get; set; }
                }

                public class Address
                {
                    public Country? Country { get; set; }
                    public City? City { get; set; }
                }

                public class Country
                {
                    public string Name { get; set; } = "";
                }

                public class City
                {
                    public string Name { get; set; } = "";
                }

                public class OrderItem
                {
                    public Product? Product { get; set; }
                    public int Quantity { get; set; }
                }

                public class Product
                {
                    public string Name { get; set; } = "";
                }

                public static class SampleData
                {
                    public static List<Order> GetOrdersFromOtherSource() => [];
                }

                public static class AppHost
                {
                    public static void Build(string[] args)
                    {
                        var builder = Microsoft.AspNetCore.Builder.WebApplication.CreateBuilder(args);
                        builder.Services.AddOpenApi();
                        builder.Services.AddControllers();

                        var app = builder.Build();

                        app.MapOpenApi();
                        app.UseSwaggerUI(options =>
                        {
                            options.SwaggerEndpoint("/openapi/v1.json", "v1");
                            options.RoutePrefix = "";
                        });

                        app.MapGet(
                            "/api/minimal/get-orders/explicit",
                            () =>
                            {
                                return SampleData
                                    .GetOrdersFromOtherSource()
                                    .AsQueryable()
                                    .SelectExpr<Order, OrderDtoMinimal>(s => new
                                    {
                                        Id = s.Id,
                                        CustomerName = s.Customer?.Name,
                                        CustomerCountry = s.Customer?.Address?.Country?.Name,
                                        CustomerCity = s.Customer?.Address?.City?.Name,
                                        Items = s.OrderItems.Select(oi => new
                                        {
                                            ProductName = oi.Product?.Name,
                                            Quantity = oi.Quantity,
                                        }),
                                    })
                                    .ToList();
                            }
                        );

                        app.MapGet(
                            "/api/minimal/get-orders/anonymous",
                            () =>
                            {
                                return SampleData
                                    .GetOrdersFromOtherSource()
                                    .AsQueryable()
                                    .SelectExpr(s => new
                                    {
                                        Id = s.Id,
                                        CustomerName = s.Customer?.Name,
                                        CustomerCountry = s.Customer?.Address?.Country?.Name,
                                        CustomerCity = s.Customer?.Address?.City?.Name,
                                        Items = s.OrderItems.Select(oi => new
                                        {
                                            ProductName = oi.Product?.Name,
                                            Quantity = oi.Quantity,
                                        }),
                                    })
                                    .ToList();
                            }
                        );

                        app.UseHttpsRedirection();
                        app.MapControllers();
                        app.Run();
                    }
                }
            }
            """;

        var generated = RunGenerator(source);
        var diagnostics = GetGeneratorDiagnostics(source);
        var compilationErrors = GetCompilationErrors(source);

        diagnostics.ShouldBeEmpty();
        compilationErrors.ShouldBeEmpty();
        generated.ShouldContain("public partial class OrderDtoMinimal");
        generated.ShouldContain("s.Customer != null && s.Customer.Address != null && s.Customer.Address.Country != null ? (global::System.String?)s.Customer.Address.Country.Name : null");
    }

    [Fact]
    public void Entity_framework_worker_query_shape_compiles()
    {
        const string source = """
            using System;
            using System.Collections;
            using System.Collections.Generic;
            using System.Linq;
            using System.Linq.Expressions;
            using System.Threading;
            using System.Threading.Tasks;
            using Microsoft.EntityFrameworkCore;

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
                    public static Task<T?> FirstOrDefaultAsync<T>(this IQueryable<T> source, CancellationToken cancellationToken = default)
                        => throw null!;
                }
            }

            namespace Demo
            {
                public sealed class SampleDbContext
                {
                    public Microsoft.EntityFrameworkCore.DbSet<SampleClass> SampleClasses { get; } = null!;
                }

                public class SampleClass
                {
                    public int Id { get; set; }
                    public string Foo { get; set; } = string.Empty;
                    public string Bar { get; set; } = string.Empty;
                    public List<SampleChildClass> Childs { get; set; } = [];
                    public SampleChildClass2? Child2 { get; set; }
                    public SampleChildClass3 Child3 { get; set; } = null!;
                }

                public class SampleChildClass
                {
                    public int Id { get; set; }
                    public string Baz { get; set; } = string.Empty;
                    public SampleChildChildClass? Child { get; set; }
                }

                public class SampleChildChildClass
                {
                    public int Id { get; set; }
                    public string Qux { get; set; } = string.Empty;
                }

                public class SampleChildClass2
                {
                    public int Id { get; set; }
                    public string Quux { get; set; } = string.Empty;
                }

                public class SampleChildClass3
                {
                    public int Id { get; set; }
                    public string Corge { get; set; } = string.Empty;
                    public SampleChildChildClass2? Child { get; set; }
                }

                public class SampleChildChildClass2
                {
                    public int Id { get; set; }
                    public string Grault { get; set; } = string.Empty;
                }

                public sealed class QueryHolder
                {
                    public async Task<SampleClassDto?> ProjectAsync(SampleDbContext dbContext, CancellationToken cancellationToken)
                    {
                        return await dbContext.SampleClasses
                            .SelectExpr<SampleClass, SampleClassDto>(s => new
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
                            .FirstOrDefaultAsync(cancellationToken);
                    }
                }
            }
            """;

        var generated = RunGenerator(source);
        var diagnostics = GetGeneratorDiagnostics(source);
        var compilationErrors = GetCompilationErrors(source);

        diagnostics.ShouldBeEmpty();
        compilationErrors.ShouldBeEmpty();
        generated.ShouldContain("public partial class SampleClassDto");
        generated.ShouldContain("s.Child2 != null ? (int?)s.Child2.Id : null");
    }

    // TODO: The public nested-SelectExpr docs require empty partial DTO declarations,
    // but the shipped examples also need to compile without them. Keep this compatibility
    // smoke test until that docs/example discrepancy is resolved.
    [Fact]
    public void Nested_explicit_dto_generation_supports_example_style_without_partial_stubs()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Linq;

            namespace Demo;

            public sealed class Parent
            {
                public int Id { get; set; }
                public List<Child> Children { get; set; } = [];
            }

            public sealed class Child
            {
                public int Id { get; set; }
                public GrandChild? Nested { get; set; }
            }

            public sealed class GrandChild
            {
                public string Name { get; set; } = string.Empty;
            }

            public sealed class QueryHolder
            {
                public IQueryable<ParentDto> Project(IQueryable<Parent> source)
                {
                    return source.SelectExpr<Parent, ParentDto>(parent => new
                    {
                        parent.Id,
                        Children = parent.Children.SelectExpr<Child, ChildDto>(child => new
                        {
                            child.Id,
                            NestedName = child.Nested?.Name,
                        }),
                    });
                }
            }
            """;

        var generated = RunGenerator(source);
        var diagnostics = GetGeneratorDiagnostics(source);
        var compilationErrors = GetCompilationErrors(source);

        diagnostics.ShouldBeEmpty();
        compilationErrors.ShouldBeEmpty();
        generated.ShouldContain("public partial class ParentDto");
        generated.ShouldContain("public partial class ChildDto");
        generated.ShouldContain("child.Nested != null ? (global::System.String?)child.Nested.Name : null");
    }

    [Fact]
    public void Array_nullability_removal_emits_non_nullable_collections_with_empty_fallbacks()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Linq;

            namespace Demo;

            public sealed class Entity
            {
                public List<Child>? Children { get; set; }
            }

            public sealed class Child
            {
                public string Name { get; set; } = string.Empty;
            }

            public sealed class QueryHolder
            {
                public IQueryable<EntityDto> Project(IQueryable<Entity> source)
                {
                    return source.SelectExpr<Entity, EntityDto>(entity => new
                    {
                        ChildNames = entity.Children?.Select(child => child.Name).ToList(),
                        ChildNameSequence = entity.Children?.Select(child => child.Name),
                    });
                }
            }
            """;

        var generated = RunGenerator(source);
        var diagnostics = GetGeneratorDiagnostics(source);
        var compilationErrors = GetCompilationErrors(source);

        diagnostics.ShouldBeEmpty();
        compilationErrors.ShouldBeEmpty();
        generated.ShouldContain("public required global::System.Collections.Generic.List<global::System.String> ChildNames");
        generated.ShouldContain("public required global::System.Collections.Generic.IEnumerable<global::System.String> ChildNameSequence");
        generated.ShouldContain(
            "ChildNames = entity.Children != null ? entity.Children.Select(child => child.Name).ToList() : new global::System.Collections.Generic.List<global::System.String>()"
        );
        generated.ShouldContain(
            "ChildNameSequence = entity.Children != null ? entity.Children.Select(child => child.Name) : global::System.Linq.Enumerable.Empty<global::System.String>()"
        );
        generated.ShouldContain(": new global::System.Collections.Generic.List<global::System.String>()");
        generated.ShouldContain(": global::System.Linq.Enumerable.Empty<global::System.String>()");
    }

    [Fact]
    public void Array_nullability_removal_can_be_disabled()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Linq;

            namespace Demo;

            public sealed class Entity
            {
                public List<Child>? Children { get; set; }
            }

            public sealed class Child
            {
                public string Name { get; set; } = string.Empty;
            }

            public sealed class QueryHolder
            {
                public IQueryable<EntityDto> Project(IQueryable<Entity> source)
                {
                    return source.SelectExpr<Entity, EntityDto>(entity => new
                    {
                        ChildNames = entity.Children?.Select(child => child.Name).ToList(),
                    });
                }
            }
            """;

        var generated = RunGenerator(
            source,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["build_property.LinqraftArrayNullabilityRemoval"] = "false",
            }
        );
        var compilationErrors = GetCompilationErrors(
            source,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["build_property.LinqraftArrayNullabilityRemoval"] = "false",
            }
        );

        compilationErrors.ShouldBeEmpty();
        generated.ShouldContain("global::System.Collections.Generic.List<global::System.String>? ChildNames");
        generated.ShouldContain(": null");
    }

    [Fact]
    public void IEnumerable_receiver_generation_emits_enumerable_interceptor()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Linq;

            namespace Demo;

            public sealed class Entity
            {
                public int Id { get; set; }
            }

            public sealed class QueryHolder
            {
                public IEnumerable<EntityDto> Project(IEnumerable<Entity> source)
                {
                    return source.SelectExpr<Entity, EntityDto>(entity => new
                    {
                        entity.Id,
                    });
                }
            }
            """;

        var generated = RunGenerator(source);
        var diagnostics = GetGeneratorDiagnostics(source);
        var compilationErrors = GetCompilationErrors(source);

        diagnostics.ShouldBeEmpty();
        compilationErrors.ShouldBeEmpty();
        generated.ShouldContain("public static global::System.Collections.Generic.IEnumerable<TResult> SelectExpr_");
        generated.ShouldContain("public partial class EntityDto");
    }

    [Fact]
    public void Predeclared_properties_are_not_emitted_twice()
    {
        const string source = """
            using System.Linq;

            namespace Demo;

            public sealed class Entity
            {
                public int Id { get; set; }
                public string Name { get; set; } = string.Empty;
            }

            public partial class EntityDto
            {
                public int Id { get; private set; }
            }

            public sealed class QueryHolder
            {
                public IQueryable<EntityDto> Project(IQueryable<Entity> source)
                {
                    return source.SelectExpr<Entity, EntityDto>(entity => new
                    {
                        entity.Id,
                        entity.Name,
                    });
                }
            }
            """;

        var generated = RunGenerator(source);
        var diagnostics = GetGeneratorDiagnostics(source);
        var compilationErrors = GetCompilationErrors(source);

        diagnostics.ShouldBeEmpty();
        compilationErrors.ShouldBeEmpty();
        generated.ShouldContain("public required global::System.String Name");
        generated.ShouldNotContain("public required int Id");
        generated.ShouldNotContain("public int Id");
        generated.ShouldNotContain("public required global::System.Int32 Id");
        generated.ShouldNotContain("public global::System.Int32 Id");
    }

    [Fact]
    public void Global_namespace_option_places_generated_root_dto_in_configured_namespace()
    {
        const string source = """
            using System.Linq;

            public sealed class Entity
            {
                public int Id { get; set; }
            }

            public sealed class QueryHolder
            {
                public IQueryable<global::Configured.Namespace.EntityDto> Project(IQueryable<Entity> source)
                {
                    return source.SelectExpr<Entity, global::Configured.Namespace.EntityDto>(entity => new
                    {
                        entity.Id,
                    });
                }
            }
            """;

        var generated = RunGenerator(
            source,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["build_property.LinqraftGlobalNamespace"] = "Configured.Namespace",
            }
        );
        var compilationErrors = GetCompilationErrors(
            source,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["build_property.LinqraftGlobalNamespace"] = "Configured.Namespace",
            }
        );

        compilationErrors.ShouldBeEmpty();
        generated.ShouldContain("namespace Configured.Namespace");
        generated.ShouldContain("public partial class EntityDto");
    }

    [Fact]
    public void Prebuild_expression_option_emits_cached_expression_field()
    {
        const string source = """
            using System.Linq;

            namespace Demo;

            public sealed class Entity
            {
                public int Id { get; set; }
            }

            public sealed class QueryHolder
            {
                public IQueryable<EntityDto> Project(IQueryable<Entity> source)
                {
                    return source.SelectExpr<Entity, EntityDto>(entity => new
                    {
                        entity.Id,
                    });
                }
            }
            """;

        var generated = RunGenerator(
            source,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["build_property.LinqraftUsePrebuildExpression"] = "true",
            }
        );
        var compilationErrors = GetCompilationErrors(
            source,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["build_property.LinqraftUsePrebuildExpression"] = "true",
            }
        );

        compilationErrors.ShouldBeEmpty();
        generated.ShouldContain("private static readonly global::System.Linq.Expressions.Expression<global::System.Func<global::Demo.Entity, global::Demo.EntityDto>> s_expression_");
    }

    [Fact]
    public void Hash_class_name_mode_keeps_nested_dto_in_parent_namespace()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Linq;

            namespace Demo;

            public sealed class Entity
            {
                public List<Child> Children { get; set; } = [];
            }

            public sealed class Child
            {
                public int Id { get; set; }
            }

            public sealed class QueryHolder
            {
                public IQueryable<EntityDto> Project(IQueryable<Entity> source)
                {
                    return source.SelectExpr<Entity, EntityDto>(entity => new
                    {
                        Children = entity.Children.Select(child => new
                        {
                            child.Id,
                        }),
                    });
                }
            }
            """;

        var generated = RunGenerator(
            source,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["build_property.LinqraftNestedDtoUseHashNamespace"] = "false",
            }
        );
        var compilationErrors = GetCompilationErrors(
            source,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["build_property.LinqraftNestedDtoUseHashNamespace"] = "false",
            }
        );

        compilationErrors.ShouldBeEmpty();
        generated.ShouldContain("public partial class ChildrenDto_");
        generated.ShouldNotContain("namespace Demo.LinqraftGenerated_");
    }

    [Fact]
    public void Comment_output_none_suppresses_generated_xml_docs()
    {
        const string source = """
            using System.Linq;

            namespace Demo;

            /// <summary>
            /// Entity summary
            /// </summary>
            public sealed class Entity
            {
                /// <summary>
                /// Identifier summary
                /// </summary>
                public int Id { get; set; }
            }

            public sealed class QueryHolder
            {
                public IQueryable<EntityDto> Project(IQueryable<Entity> source)
                {
                    return source.SelectExpr<Entity, EntityDto>(entity => new
                    {
                        entity.Id,
                    });
                }
            }
            """;

        var generated = RunGenerator(
            source,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["build_property.LinqraftCommentOutput"] = "None",
            }
        );
        var compilationErrors = GetCompilationErrors(
            source,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["build_property.LinqraftCommentOutput"] = "None",
            }
        );

        compilationErrors.ShouldBeEmpty();
        generated.ShouldNotContain("/// <summary>");
        generated.ShouldNotContain("/// <remarks>");
    }

    private static string RunGenerator(string source, IReadOnlyDictionary<string, string>? globalOptions = null)
    {
        var result = RunGeneratorAndGetResult(source, globalOptions);
        return string.Join(
            "\n\n",
            result.Results.SelectMany(generatorResult => generatorResult.GeneratedSources).Select(sourceResult => sourceResult.SourceText.ToString())
        );
    }

    private static ImmutableArray<Diagnostic> GetGeneratorDiagnostics(
        string source,
        IReadOnlyDictionary<string, string>? globalOptions = null
    )
    {
        var result = RunGeneratorAndGetResult(source, globalOptions);
        return result.Results.SelectMany(generatorResult => generatorResult.Diagnostics).ToImmutableArray();
    }

    private static ImmutableArray<Diagnostic> GetCompilationErrors(
        string source,
        IReadOnlyDictionary<string, string>? globalOptions = null
    )
    {
        var parseOptions = CreateParseOptions();
        var syntaxTree = CSharpSyntaxTree.ParseText(source, parseOptions, path: "Test0.cs");
        var implicitUsingsTree = CreateImplicitUsingsTree(source);
        var compilation = CSharpCompilation.Create(
            assemblyName: "GeneratorTests",
            syntaxTrees: [syntaxTree, implicitUsingsTree],
            references: GetMetadataReferences(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable)
        );

        var generator = new LinqraftSourceGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [generator.AsSourceGenerator()],
            parseOptions: parseOptions,
            optionsProvider: globalOptions is null ? null : new TestAnalyzerConfigOptionsProvider(globalOptions)
        );

        driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var generatorDiagnostics);
        return generatorDiagnostics
            .Concat(outputCompilation.GetDiagnostics())
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToImmutableArray();
    }

    private static GeneratorDriverRunResult RunGeneratorAndGetResult(
        string source,
        IReadOnlyDictionary<string, string>? globalOptions = null
    )
    {
        var parseOptions = CreateParseOptions();
        var syntaxTree = CSharpSyntaxTree.ParseText(source, parseOptions, path: "Test0.cs");
        var implicitUsingsTree = CreateImplicitUsingsTree(source);
        var compilation = CSharpCompilation.Create(
            assemblyName: "GeneratorTests",
            syntaxTrees: [syntaxTree, implicitUsingsTree],
            references: GetMetadataReferences(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable)
        );

        var generator = new LinqraftSourceGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [generator.AsSourceGenerator()],
            parseOptions: parseOptions,
            optionsProvider: globalOptions is null ? null : new TestAnalyzerConfigOptionsProvider(globalOptions)
        );

        driver = driver.RunGenerators(compilation);
        return driver.GetRunResult();
    }

    private static IEnumerable<MetadataReference> GetMetadataReferences()
    {
        var explicitAssemblies = new List<Assembly>
        {
            typeof(object).Assembly,
            typeof(Console).Assembly,
            typeof(Enumerable).Assembly,
            typeof(Queryable).Assembly,
            typeof(System.Linq.Expressions.Expression).Assembly,
            typeof(List<>).Assembly,
            typeof(Task).Assembly,
            typeof(GeneratedCodeAttribute).Assembly,
        };

        foreach (var assemblyName in new[]
        {
            "System.Runtime",
            "netstandard",
            "System.Collections",
            "System.Console",
            "System.Linq",
            "System.Linq.Queryable",
            "System.Linq.Expressions",
            "System.Threading.Tasks",
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

    private static SyntaxTree CreateImplicitUsingsTree(string source)
    {
        var usings = new List<string>
        {
            "global using System;",
            "global using System.Collections;",
            "global using System.Collections.Generic;",
            "global using System.Linq;",
            "global using System.Threading;",
            "global using System.Threading.Tasks;",
        };

        if (source.Contains("namespace Microsoft.Extensions.DependencyInjection", StringComparison.Ordinal)
            || source.Contains(".AddOpenApi(", StringComparison.Ordinal)
            || source.Contains(".AddControllers(", StringComparison.Ordinal)
            || source.Contains(".AddHostedService<", StringComparison.Ordinal))
        {
            usings.Add("global using Microsoft.Extensions.DependencyInjection;");
        }

        if (source.Contains("namespace Microsoft.AspNetCore.Builder", StringComparison.Ordinal)
            || source.Contains("WebApplication", StringComparison.Ordinal))
        {
            usings.Add("global using Microsoft.AspNetCore.Builder;");
        }

        if (source.Contains("namespace Microsoft.Extensions.Hosting", StringComparison.Ordinal)
            || source.Contains("Host.CreateApplicationBuilder", StringComparison.Ordinal))
        {
            usings.Add("global using Microsoft.Extensions.Hosting;");
        }

        if (source.Contains("namespace Microsoft.Extensions.Logging", StringComparison.Ordinal)
            || source.Contains("ILogger<", StringComparison.Ordinal))
        {
            usings.Add("global using Microsoft.Extensions.Logging;");
        }

        return CSharpSyntaxTree.ParseText(
            string.Join(Environment.NewLine, usings),
            CreateParseOptions(),
            path: "GlobalUsings.g.cs"
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
}
