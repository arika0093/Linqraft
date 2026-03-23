using System.Collections.Generic;
using System.Linq;

namespace Linqraft.Tests;

public sealed class HelperAsProjectableRuntimeTests
{
    private static readonly List<HelperProjectionOrder> Orders =
    [
        new()
        {
            Id = 1,
            Customer = new HelperProjectionCustomer
            {
                Id = 10,
                Name = "Ada",
                Tier = "Gold",
            },
            Items =
            [
                new HelperProjectionItem { Name = "Keyboard", Quantity = 2 },
                new HelperProjectionItem { Name = "Cable", Quantity = 1 },
            ],
        },
        new()
        {
            Id = 2,
            Customer = null,
            Items = [new HelperProjectionItem { Name = "Mouse", Quantity = 3 }],
        },
    ];

    [Test]
    public void Helper_AsProjectable_inlines_computed_property()
    {
        var result = Orders
            .AsTestQueryable()
            .OrderBy(order => order.Id)
            .SelectExpr<HelperProjectionOrder, HelperAsProjectableOrderDto>(
                (order, helper) =>
                    new
                    {
                        order.Id,
                        FirstLargeItemName = helper.AsProjectable(order.FirstLargeItemName),
                    }
            )
            .ToList();

        result
            .Select(row => new { row.Id, row.FirstLargeItemName })
            .ToList()
            .ShouldBe(
                new[]
                {
                    new { Id = 1, FirstLargeItemName = (string?)"Keyboard" },
                    new { Id = 2, FirstLargeItemName = (string?)"Mouse" },
                }
            );
    }
}

public partial class HelperAsProjectableOrderDto;
