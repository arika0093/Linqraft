using System.Collections.Generic;
using System.Linq;

namespace Linqraft.Tests;

public sealed class HelperProjectRuntimeTests
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
    public void Helper_Project_with_generic_uses_explicit_name_hint()
    {
        var result = Orders
            .AsTestQueryable()
            .Where(order => order.Customer != null)
            .SelectExpr<HelperProjectionOrder, HelperProjectExplicitOrderDto>(
                (order, helper) =>
                    new
                    {
                        order.Id,
                        SelectedCustomer = helper
                            .Project<HelperProjectionCustomer>(order.Customer!)
                            .Select(customer => new
                            {
                                customer.Id,
                                customer.Name,
                                customer.Tier,
                            }),
                    }
            )
            .ToList();

        result.Count.ShouldBe(1);
        result[0].SelectedCustomer.GetType().Name.ShouldBe("HelperProjectionCustomerDto");
        result[0].SelectedCustomer.Id.ShouldBe(10);
        result[0].SelectedCustomer.Name.ShouldBe("Ada");
        result[0].SelectedCustomer.Tier.ShouldBe("Gold");
    }

    [Test]
    public void Helper_Project_without_generic_uses_automatic_name()
    {
        var result = Orders
            .AsTestQueryable()
            .Where(order => order.Customer != null)
            .SelectExpr<HelperProjectionOrder, HelperProjectImplicitOrderDto>(
                (order, helper) =>
                    new
                    {
                        order.Id,
                        SelectedCustomer = helper
                            .Project(order.Customer!)
                            .Select(customer => new { customer.Id, customer.Name }),
                    }
            )
            .ToList();

        result.Count.ShouldBe(1);
        result[0].SelectedCustomer.GetType().Name.ShouldBe("SelectedCustomerDto");
        result[0].SelectedCustomer.Id.ShouldBe(10);
        result[0].SelectedCustomer.Name.ShouldBe("Ada");
    }
}
