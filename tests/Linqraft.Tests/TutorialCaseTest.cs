using System.Collections.Generic;
using System.Linq;
using Linqraft;

namespace Tutorial;

public class TutorialCaseTest
{
    private List<Order> Orders = [];

    [Fact]
    public void TryTutorialCaseAnonymous()
    {
        var orders = Orders
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
    public void TryTutorialCaseExplicit()
    {
        var orders = Orders
            .AsQueryable()
            // Order: input entity type
            // OrderDto: output DTO type (auto-generated)
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
