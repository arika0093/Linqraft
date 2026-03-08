using Linqraft.Playground.Models;

namespace Linqraft.Playground.Services;

/// <summary>
/// Service for managing templates and sample code
/// Templates are based on actual examples from the Linqraft repository
/// Note: In Blazor WebAssembly, we cannot directly access the file system at runtime,
/// so templates are embedded from the examples folder during build time.
/// </summary>
public class TemplateService
{
    /// <summary>
    /// Common using directives needed for Roslyn compilation
    /// </summary>
    private const string CommonUsings = """
        using System;
        using System.Linq;
        using System.Collections.Generic;
        """;

    /// <summary>
    /// Stub SelectExpr extension method for Roslyn compilation
    /// This enables semantic analysis without requiring the actual Linqraft library
    /// </summary>
    private const string SelectExprStub = """
        // Stub SelectExpr extension for playground analysis
        namespace Linqraft
        {
            public static class LinqraftExtensions
            {
                public static IQueryable<TResult> SelectExpr<TSource, TResult>(
                    this IQueryable<TSource> source,
                    Expression<Func<TSource, TResult>> selector) => source.Select(selector);
            }
        }
        """;

    public List<Template> GetTemplates()
    {
        return
        [
            CreateReadmeTemplate(),
            CreateMinReproTemplate(),
            CreateAnonymousTypeTemplate(),
            CreateExplicitDtoTemplate(),
        ];
    }

    /// <summary>
    /// Template based on examples/Linqraft.MinimumSample
    /// </summary>
    private static Template CreateReadmeTemplate()
    {
        return new Template
        {
            Name = "Readme Sample",
            Description =
                "Example from Linqraft README demonstrating SelectExpr with nested properties",
            Files =
            [
                new ProjectFile
                {
                    Name = "SampleClasses.cs",
                    Path = "SampleClasses.cs",
                    IsHidden = false,
                    Content =
                        CommonUsings
                        + """

                            namespace Tutorial;

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
                                public string? EmailAddress { get; set; }
                                public string? PhoneNumber { get; set; }
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
                                public decimal UnitPrice { get; set; }
                                public DateTime OrderDate { get; set; }
                            }

                            public class Product
                            {
                                public string Name { get; set; } = "";
                            }
                            """,
                },
                new ProjectFile
                {
                    Name = "Program.cs",
                    Path = "Program.cs",
                    IsHidden = false,
                    // Content from: examples/Linqraft.MinimumSample/Program.cs
                    Content =
                        CommonUsings
                        + """

                            namespace Tutorial;

                            public class TutorialCaseTest
                            {
                                private readonly List<Order> Orders = [];

                                [Fact]
                                public void TryTutorialCaseExplicit()
                                {
                                    var orders = Orders
                                        .AsQueryable()
                                        // Order: input entity type
                                        // OrderDto: output DTO type (auto-generated)
                                        .SelectExpr<Order, OrderDto>(o => new
                                        {
                                            o.Id,
                                            CustomerName = o.Customer?.Name,
                                            CustomerCountry = o.Customer?.Address?.Country?.Name,
                                            CustomerCity = o.Customer?.Address?.City?.Name,
                                            CustomerInfo = new
                                            {
                                                Email = o.Customer?.EmailAddress,
                                                Phone = o.Customer?.PhoneNumber,
                                            },
                                            LatestOrderDate = o.OrderItems.Max(oi => oi.OrderDate),
                                            TotalAmount = o.OrderItems.Sum(oi => oi.Quantity * oi.UnitPrice),
                                            Items = o.OrderItems.Select(oi => new
                                            {
                                                ProductName = oi.Product?.Name,
                                                oi.Quantity,
                                            }),
                                        })
                                        .ToList();
                                }
                            }
                            """,
                },
                new ProjectFile
                {
                    Name = "_LinqraftStub.cs",
                    Path = "_LinqraftStub.cs",
                    IsHidden = true,
                    Content = CommonUsings + "\n" + SelectExprStub,
                },
            ],
        };
    }

    /// <summary>
    /// Minimal reproduction template for issue reporting
    /// Single Program.cs with simplified code using namespace MinRepro
    /// </summary>
    private static Template CreateMinReproTemplate()
    {
        return new Template
        {
            Name = "Min Repro",
            Description = "Minimal reproduction template for issue reporting",
            Files =
            [
                new ProjectFile
                {
                    Name = "Program.cs",
                    Path = "Program.cs",
                    IsHidden = false,
                    Content =
                        CommonUsings
                        + """

                            namespace MinRepro;

                            public class Program
                            {
                                public void Execute()
                                {
                                    var data = new List<Entity>();
                                    var query = data.AsQueryable();
                                    // Reproduce your issue here
                                    var result = query.SelectExpr<Entity, EntityDto>(x => new
                                    {
                                        x.Id,
                                        x.Name,
                                        ChildDescription = x.Child?.Description,
                                        ItemTitles = x.Items.Select(i => i.Title),
                                    });
                                }
                            }

                            // here are some sample classes to work with 
                            public class Entity
                            {
                                public int Id { get; set; }
                                public string Name { get; set; } = "";
                                public Child? Child { get; set; }
                                public List<Item> Items { get; set; } = [];
                            }

                            public class Child
                            {
                                public string Description { get; set; } = "";
                            }

                            public class Item
                            {
                                public string Title { get; set; } = "";
                            }
                            """,
                },
                new ProjectFile
                {
                    Name = "_LinqraftStub.cs",
                    Path = "_LinqraftStub.cs",
                    IsHidden = true,
                    Content = CommonUsings + "\n" + SelectExprStub,
                },
            ],
        };
    }

    /// Template demonstrating anonymous type pattern
    /// </summary>
    private static Template CreateAnonymousTypeTemplate()
    {
        return new Template
        {
            Name = "Anonymous Type",
            Description = "Using anonymous types with SelectExpr (Pattern 1)",
            Files =
            [
                new ProjectFile
                {
                    Name = "Models.cs",
                    Path = "Models.cs",
                    IsHidden = false,
                    Content =
                        CommonUsings
                        + """

                            namespace AnonymousTypeExample;

                            public class Product
                            {
                                public int Id { get; set; }
                                public string Name { get; set; } = "";
                                public decimal Price { get; set; }
                                public Category? Category { get; set; }
                            }

                            public class Category
                            {
                                public int Id { get; set; }
                                public string Name { get; set; } = "";
                                public string Description { get; set; } = "";
                            }
                            """,
                },
                new ProjectFile
                {
                    Name = "Query.cs",
                    Path = "Query.cs",
                    IsHidden = false,
                    Content =
                        CommonUsings
                        + """

                            namespace AnonymousTypeExample;

                            public class QueryExample
                            {
                                public void Execute()
                                {
                                    var products = new List<Product>();
                                    // Pattern 1: use anonymous type to specify selection
                                    // Return type is anonymous type
                                    var query = products.AsQueryable();
                                    var result = query.SelectExpr(x => new
                                    {
                                        x.Id,
                                        x.Name,
                                        // You can use the null-conditional operator
                                        CategoryName = x.Category?.Name,
                                    });
                                }
                            }
                            """,
                },
                new ProjectFile
                {
                    Name = "_LinqraftStub.cs",
                    Path = "_LinqraftStub.cs",
                    IsHidden = true,
                    Content = CommonUsings + "\n" + SelectExprStub,
                },
            ],
        };
    }

    /// <summary>
    /// Template demonstrating explicit DTO pattern
    /// </summary>
    private static Template CreateExplicitDtoTemplate()
    {
        return new Template
        {
            Name = "Explicit DTO",
            Description = "Using explicit DTO type parameter with SelectExpr (Pattern 2)",
            Files =
            [
                new ProjectFile
                {
                    Name = "Models.cs",
                    Path = "Models.cs",
                    IsHidden = false,
                    Content =
                        CommonUsings
                        + """

                            namespace ExplicitDtoExample;

                            public class Order
                            {
                                public int Id { get; set; }
                                public DateTime OrderDate { get; set; }
                                public Customer? Customer { get; set; }
                                public List<OrderItem> Items { get; set; } = new();
                            }

                            public class Customer
                            {
                                public int Id { get; set; }
                                public string Name { get; set; } = "";
                                public string Email { get; set; } = "";
                            }

                            public class OrderItem
                            {
                                public int Id { get; set; }
                                public string ProductName { get; set; } = "";
                                public int Quantity { get; set; }
                                public decimal UnitPrice { get; set; }
                            }
                            """,
                },
                new ProjectFile
                {
                    Name = "Query.cs",
                    Path = "Query.cs",
                    IsHidden = false,
                    Content =
                        CommonUsings
                        + """

                            namespace ExplicitDtoExample;

                            public class QueryExample
                            {
                                public void Execute()
                                {
                                    var orders = new List<Order>();
                                    // Pattern 2: use an explicit DTO class
                                    // Return type is OrderDto (auto-generated)
                                    var query = orders.AsQueryable();
                                    var result = query.SelectExpr<Order, OrderDto>(x => new
                                    {
                                        x.Id,
                                        x.OrderDate,
                                        CustomerName = x.Customer?.Name,
                                        // You can select child properties
                                        ItemNames = x.Items.Select(i => i.ProductName).ToList(),
                                    });
                                }
                            }

                            // This DTO class will be auto-generated by Linqraft
                            public partial class OrderDto { }
                            """,
                },
                new ProjectFile
                {
                    Name = "_LinqraftStub.cs",
                    Path = "_LinqraftStub.cs",
                    IsHidden = true,
                    Content =
                        CommonUsings
                        + """

                            // Stub SelectExpr extension for playground analysis
                            namespace Linqraft
                            {
                                public static class LinqraftExtensions
                                {
                                    public static IQueryable<TResult> SelectExpr<TSource, TResult>(
                                        this IQueryable<TSource> source,
                                        Expression<Func<TSource, TResult>> selector) => source.Select(selector);
                                    
                                    public static IQueryable<TResult> SelectExpr<TSource, TResult>(
                                        this IQueryable<TSource> source,
                                        Expression<Func<TSource, object>> selector) => source.Cast<TResult>();
                                }
                            }
                            """,
                },
            ],
        };
    }
}
