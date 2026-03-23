using System.Collections.Generic;
using System.Linq;

namespace Linqraft.Tests;

public sealed class HelperAsProjectionRuntimeTests
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
    public void Helper_AsProjection_creates_explicit_nested_dto()
    {
        var result = Orders
            .AsTestQueryable()
            .Where(order => order.Customer != null)
            .SelectExpr<HelperProjectionOrder, HelperAsProjectionExplicitOrderDto>(
                (order, helper) =>
                    new
                    {
                        order.Id,
                        Customer = helper.AsProjection<HelperExplicitProjectedCustomerDto>(
                            order.Customer!
                        ),
                    }
            )
            .ToList();

        result.Count.ShouldBe(1);
        result[0].Customer.GetType().ShouldBe(typeof(HelperExplicitProjectedCustomerDto));
        result[0].Customer.Id.ShouldBe(10);
        result[0].Customer.Name.ShouldBe("Ada");
        result[0].Customer.Tier.ShouldBe("Gold");
    }

    [Test]
    public void Helper_AsProjection_without_generic_uses_source_type_dto_name()
    {
        var result = Orders
            .AsTestQueryable()
            .Where(order => order.Customer != null)
            .SelectExpr<HelperProjectionOrder, HelperAsProjectionImplicitOrderDto>(
                (order, helper) =>
                    new
                    {
                        order.Id,
                        Customer = helper.AsProjection(order.Customer!),
                    }
            )
            .ToList();

        result.Count.ShouldBe(1);
        result[0].Customer.GetType().Name.ShouldBe("HelperProjectionCustomerDto");
        result[0].Customer.Id.ShouldBe(10);
        result[0].Customer.Name.ShouldBe("Ada");
        result[0].Customer.Tier.ShouldBe("Gold");
    }
}

public partial class HelperAsProjectionExplicitOrderDto;

public partial class HelperExplicitProjectedCustomerDto;

public partial class HelperAsProjectionImplicitOrderDto;
