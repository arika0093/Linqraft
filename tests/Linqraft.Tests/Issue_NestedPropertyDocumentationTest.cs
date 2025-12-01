using System.Collections.Generic;
using System.Linq;

namespace Linqraft.Tests;

/// <summary>
/// Test case for issue: The reference location for the generated comment is incorrect
/// See: https://github.com/arika0093/Linqraft/issues/xxx
///
/// Problem:
/// 1. ProductName = oi.Product?.Name should get comment from Product.Name, not OrderItem.Product
/// 2. Items = s.OrderItems.Select(...) should get comment from Order.OrderItems, but has no summary
/// </summary>
public class Issue_NestedPropertyDocumentationTest
{
    [Fact]
    public void Test_NestedPropertyCommentReference()
    {
        var orders = new List<Order>
        {
            new()
            {
                OrderItems =
                [
                    new()
                    {
                        Product = new() { Name = "Product A" },
                        Quantity = 5,
                    },
                    new()
                    {
                        Product = new() { Name = "Product B" },
                        Quantity = 3,
                    },
                ],
            },
        };

        var results = orders
            .AsQueryable()
            .SelectExpr<Order, OrderDto>(s => new
            {
                Items = s.OrderItems.Select(oi => new
                {
                    ProductName = oi.Product?.Name,
                    Quantity = oi.Quantity,
                }),
            })
            .ToList();

        results.Count.ShouldBe(1);
        results[0].Items.Count().ShouldBe(2);
        results[0].Items.First().ProductName.ShouldBe("Product A");
        results[0].Items.First().Quantity.ShouldBe(5);
    }
}

// order
internal class Order
{
    // test item
    public List<OrderItem> OrderItems { get; set; } = new();
}

// orderitem
internal class OrderItem
{
    // product data
    public Product? Product { get; set; }

    // sample quantity
    public int Quantity { get; set; }
}

// product
internal class Product
{
    // sample name
    public string Name { get; set; } = "";
}

internal partial class OrderDto { }
