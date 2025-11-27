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
    public List<Template> GetTemplates()
    {
        return new List<Template>
        {
            CreateMinimumSampleTemplate(),
            CreateAnonymousTypeTemplate(),
            CreateExplicitDtoTemplate(),
            CreateNestedObjectTemplate(),
        };
    }

    /// <summary>
    /// Template based on examples/Linqraft.MinimumSample
    /// </summary>
    private static Template CreateMinimumSampleTemplate()
    {
        return new Template
        {
            Name = "Minimum Sample",
            Description = "Basic example showing SelectExpr with null-conditional operators (from examples/Linqraft.MinimumSample)",
            Files = new List<ProjectFile>
            {
                new ProjectFile
                {
                    Name = "SampleClasses.cs",
                    Path = "SampleClasses.cs",
                    // Content from: examples/Linqraft.MinimumSample/SampleClasses.cs
                    Content = """
                        namespace Linqraft.MinimumSample;

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
                        """
                },
                new ProjectFile
                {
                    Name = "Program.cs",
                    Path = "Program.cs",
                    // Content from: examples/Linqraft.MinimumSample/Program.cs
                    Content = """
                        using System.Text.Json;
                        using Linqraft.MinimumSample;

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

                        var resultJson = JsonSerializer.Serialize(
                            results,
                            new JsonSerializerOptions { WriteIndented = true }
                        );
                        Console.WriteLine(resultJson);
                        """
                }
            }
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
            Files = new List<ProjectFile>
            {
                new ProjectFile
                {
                    Name = "Models.cs",
                    Path = "Models.cs",
                    Content = """
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
                        """
                },
                new ProjectFile
                {
                    Name = "Query.cs",
                    Path = "Query.cs",
                    Content = """
                        using AnonymousTypeExample;

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
                        """
                }
            }
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
            Files = new List<ProjectFile>
            {
                new ProjectFile
                {
                    Name = "Models.cs",
                    Path = "Models.cs",
                    Content = """
                        namespace ExplicitDtoExample;

                        public class Order
                        {
                            public int Id { get; set; }
                            public DateTime OrderDate { get; set; }
                            public Customer? Customer { get; set; }
                            public List<OrderItem> Items { get; set; } = [];
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
                        """
                },
                new ProjectFile
                {
                    Name = "Query.cs",
                    Path = "Query.cs",
                    Content = """
                        using ExplicitDtoExample;

                        // Pattern 2: use an explicit DTO class
                        // Return type is OrderDto (auto-generated)
                        var query = orders.AsQueryable();
                        var result = query.SelectExpr<OrderDto>(x => new
                        {
                            x.Id,
                            x.OrderDate,
                            CustomerName = x.Customer?.Name,
                            // You can select child properties
                            ItemNames = x.Items.Select(i => i.ProductName).ToList(),
                        });
                        """
                }
            }
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
            Files = new List<ProjectFile>
            {
                new ProjectFile
                {
                    Name = "Models.cs",
                    Path = "Models.cs",
                    Content = """
                        namespace NestedObjectExample;

                        public class Company
                        {
                            public int Id { get; set; }
                            public string Name { get; set; } = "";
                            public Address? Headquarters { get; set; }
                            public List<Employee> Employees { get; set; } = [];
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
                        """
                },
                new ProjectFile
                {
                    Name = "Query.cs",
                    Path = "Query.cs",
                    Content = """
                        using NestedObjectExample;

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
                        """
                }
            }
        };
    }
}
