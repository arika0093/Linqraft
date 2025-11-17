using System.Collections.Generic;
using System.Linq;
using GlobalGenerated;

public class LinqraftConfigurationTest
{
    private List<Order> Orders =
    [
        new()
        {
            Id = 1,
            Customer = new()
            {
                Name = "John Doe",
                Address = new()
                {
                    Country = new() { Name = "USA" },
                    City = new() { Name = "New York" },
                },
            },
            OrderItems =
            [
                new()
                {
                    Product = new() { Name = "Laptop" },
                    Quantity = 1,
                },
                new()
                {
                    Product = new() { Name = "Mouse" },
                    Quantity = 2,
                },
            ],
        },
        new()
        {
            Id = 2,
            Customer = new()
            {
                Name = "Jane Smith",
                Address = new()
                {
                    Country = new() { Name = "Canada" },
                    City = new() { Name = "Toronto" },
                },
            },
            OrderItems =
            [
                new()
                {
                    Product = new() { Name = "Smartphone" },
                    Quantity = 1,
                },
            ],
        },
    ];

    [Fact]
    public void Test001()
    {
        var results = Orders
            .AsQueryable()
            .SelectExpr(s => new
            {
                Id = s.Id,
                CustomerName = s.Customer?.Name,
                CustomerCountry = s.Customer?.Address?.Country?.Name,
                CustomerCity = s.Customer?.Address?.City?.Name,
                Items = s
                    .OrderItems.Select(oi => new
                    {
                        ProductName = oi.Product?.Name,
                        Quantity = oi.Quantity,
                    })
                    .ToList(),
            })
            .ToList();
    }

    [Fact]
    public void Test002()
    {
        var results = Orders
            .AsQueryable()
            .SelectExpr<Order, OrderDto>(s => new
            {
                Id = s.Id,
                CustomerName = s.Customer?.Name,
                CustomerCountry = s.Customer?.Address?.Country?.Name,
                CustomerCity = s.Customer?.Address?.City?.Name,
                Items = s
                    .OrderItems.Select(oi => new
                    {
                        ProductName = oi.Product?.Name,
                        Quantity = oi.Quantity,
                    })
                    .ToList(),
            })
            .ToList();
    }
}

// --------------------
// Data model definitions
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
