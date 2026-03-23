using System.Collections.Generic;
using System.Linq;

namespace Linqraft.Tests;

public sealed class HelperAsLeftJoinRuntimeTests
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
    public void Helper_AsLeftJoin_preserves_rows_for_nullable_navigation()
    {
        var result = Orders
            .AsTestQueryable()
            .OrderBy(order => order.Id)
            .SelectExpr<HelperProjectionOrder, HelperAsLeftJoinOrderDto>(
                (order, helper) =>
                    new { order.Id, CustomerName = helper.AsLeftJoin(order.Customer!).Name }
            )
            .ToList();

        result
            .Select(row => new { row.Id, row.CustomerName })
            .ToList()
            .ShouldBe(
                new[]
                {
                    new { Id = 1, CustomerName = (string?)"Ada" },
                    new { Id = 2, CustomerName = (string?)null },
                }
            );
    }
}

public partial class HelperAsLeftJoinOrderDto;
