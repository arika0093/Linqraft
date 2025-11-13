using System.Collections.Generic;
using System.Linq;

namespace Tutorial;

public class TutorialCaseTest
{
    private List<Order> Orders = [];

    [Fact]
    public void TryTutorialCase()
    {
        var orders = Orders
            .AsQueryable()
            // Order: input entity type
            // OrderDto: output DTO type (auto-generated)
            .SelectExpr<Order, OrderDto>(o => new
            {
                Id = o.Id,
                CustomerName = o.Customer?.Name,
                CustomerCountry = o.Customer?.Address?.Country?.Name,
                CustomerCity = o.Customer?.Address?.City?.Name,
                Items = o.OrderItems.Select(oi => new
                {
                    ProductName = oi.Product?.Name,
                    Quantity = oi.Quantity,
                }),
            })
            .ToList();
    }
}

public class Order
{
    public int Id { get; set; }
    public Customer? Customer { get; set; }
    public List<OrderItem>? OrderItems { get; set; }
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
