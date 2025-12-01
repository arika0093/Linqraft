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
        using System.Linq.Expressions;
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
            CreateMinReproTemplate(),
            CreateSimpleSampleTemplate(),
            CreateAnonymousTypeTemplate(),
            CreateExplicitDtoTemplate(),
            CreateNestedObjectTemplate(),
        ];
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

                            using Linqraft;

                            namespace MinRepro;

                            public class Entity
                            {
                                public int Id { get; set; }
                                public string Name { get; set; } = "";
                                public Child? Child { get; set; }
                            }

                            public class Child
                            {
                                public string Description { get; set; } = "";
                            }

                            public class Program
                            {
                                public void Execute()
                                {
                                    var data = new List<Entity>();
                                    var query = data.AsQueryable();

                                    // Reproduce your issue here
                                    var result = query.SelectExpr(x => new
                                    {
                                        x.Id,
                                        x.Name,
                                        ChildDescription = x.Child?.Description,
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
    /// Template based on examples/Linqraft.MinimumSample
    /// </summary>
    private static Template CreateSimpleSampleTemplate()
    {
        return new Template
        {
            Name = "Simple Sample",
            Description =
                "Basic example showing SelectExpr with null-conditional operators (from examples/Linqraft.MinimumSample)",
            Files =
            [
                new ProjectFile
                {
                    Name = "SampleClasses.cs",
                    Path = "SampleClasses.cs",
                    IsHidden = false,
                    // Content from: examples/Linqraft.MinimumSample/SampleClasses.cs
                    Content =
                        CommonUsings
                        + """

                            namespace Linqraft.MinimumSample;

                            public class Order
                            {
                                public int Id { get; set; }
                                public Customer? Customer { get; set; }
                                public List<OrderItem> OrderItems { get; set; } = new();
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

                            using Linqraft;
                            using Linqraft.MinimumSample;

                            public class Program
                            {
                                public void Execute()
                                {
                                    var orders = new List<Order>();
                                    var results = orders
                                        .AsQueryable()
                                        .SelectExpr<Order,OrderDto>(s => new
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

                            using Linqraft;
                            using AnonymousTypeExample;

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

                            using Linqraft;
                            using ExplicitDtoExample;

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

    /// <summary>
    /// Template demonstrating nested object selections
    /// </summary>
    private static Template CreateNestedObjectTemplate()
    {
        return new Template
        {
            Name = "Nested Objects",
            Description = "Working with nested object selections and collections",
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

                            namespace NestedObjectExample;

                            public class Company
                            {
                                public int Id { get; set; }
                                public string Name { get; set; } = "";
                                public Address? Headquarters { get; set; }
                                public List<Employee> Employees { get; set; } = new();
                            }

                            public class Address
                            {
                                public string Street { get; set; } = "";
                                public string City { get; set; } = "";
                                public string Country { get; set; } = "";
                            }

                            public class Employee
                            {
                                public int Id { get; set; }
                                public string Name { get; set; } = "";
                                public Department? Department { get; set; }
                            }

                            public class Department
                            {
                                public string Name { get; set; } = "";
                                public string Code { get; set; } = "";
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

                            using Linqraft;
                            using NestedObjectExample;

                            public class QueryExample
                            {
                                public void Execute()
                                {
                                    var companies = new List<Company>();
                                    // Nested object selection with null-conditional operators
                                    var query = companies.AsQueryable();
                                    var result = query.SelectExpr(c => new
                                    {
                                        c.Id,
                                        c.Name,
                                        // Nested address info
                                        HeadquartersCity = c.Headquarters?.City,
                                        HeadquartersCountry = c.Headquarters?.Country,
                                        // Nested employee with department
                                        EmployeeInfo = c.Employees.Select(e => new
                                        {
                                            e.Name,
                                            DepartmentName = e.Department?.Name,
                                        }),
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
}
