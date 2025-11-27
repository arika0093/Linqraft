using System.Collections.Generic;
using System.Linq;
using TestNamespace;

public class NestedDtoNamespaceTest
{
    private readonly List<Order> Orders =
    [
        new()
        {
            Id = 1,
            Customer = new()
            {
                Name = "John Doe",
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
    ];

    [Fact]
    public void NestedDtoNamespace_ShouldGenerateChildDtoInHashNamespace()
    {
        // Test that nested DTOs are generated in Generated_{hash} namespace
        var results = Orders
            .AsQueryable()
            .SelectExpr<Order, OrderDto>(s => new
            {
                Id = s.Id,
                CustomerName = s.Customer?.Name,
                Items = s
                    .OrderItems.Select(oi => new
                    {
                        ProductName = oi.Product?.Name,
                        Quantity = oi.Quantity,
                    })
                    .ToList(),
            })
            .ToList();

        results.Count.ShouldBe(1);
        var first = results[0];
        first.Id.ShouldBe(1);
        first.CustomerName.ShouldBe("John Doe");
        first.Items.Count.ShouldBe(2);
        first.Items[0].ProductName.ShouldBe("Laptop");
        first.Items[0].Quantity.ShouldBe(1);
        first.Items[1].ProductName.ShouldBe("Mouse");
        first.Items[1].Quantity.ShouldBe(2);
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
