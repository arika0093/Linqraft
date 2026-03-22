using System.Collections.Generic;
using System.Linq;

namespace Linqraft.Tests;

public sealed class HelperProjectionRuntimeTests
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
                    new
                    {
                        order.Id,
                        CustomerName = helper.AsLeftJoin(order.Customer!).Name,
                    }
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

    [Test]
    public void Helper_AsInnerJoin_projects_non_null_navigation_access()
    {
        var result = Orders
            .AsTestQueryable()
            .Where(order => order.Customer != null)
            .OrderBy(order => order.Id)
            .SelectExpr<HelperProjectionOrder, HelperAsInnerJoinOrderDto>(
                (order, helper) =>
                    new
                    {
                        order.Id,
                        CustomerName = helper.AsInnerJoin(order.Customer!).Name,
                    }
            )
            .ToList();

        result
            .Select(row => new { row.Id, row.CustomerName })
            .ToList()
            .ShouldBe(new[] { new { Id = 1, CustomerName = "Ada" } });
    }

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

    [Test]
    public void Helper_AsProjection_creates_explicit_nested_dto()
    {
        var result = Orders
            .AsTestQueryable()
            .Where(order => order.Customer != null)
            .SelectExpr<HelperProjectionOrder, HelperAsProjectionExplicitOrderDto>(
                order =>
                    new
                    {
                        order.Id,
                        Customer = order.Customer!.AsProjection<HelperExplicitProjectedCustomerDto>(),
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
                            .Select(customer => new { customer.Id, customer.Name, customer.Tier }),
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
                        SelectedCustomer = helper.Project(order.Customer!).Select(customer => new
                        {
                            customer.Id,
                            customer.Name,
                        }),
                    }
            )
            .ToList();

        result.Count.ShouldBe(1);
        result[0].SelectedCustomer.GetType().Name.ShouldBe("SelectedCustomerDto");
        result[0].SelectedCustomer.Id.ShouldBe(10);
        result[0].SelectedCustomer.Name.ShouldBe("Ada");
    }
}

public sealed class HelperProjectionOrder
{
    public int Id { get; set; }

    public HelperProjectionCustomer? Customer { get; set; }

    public List<HelperProjectionItem> Items { get; set; } = [];

    public string? FirstLargeItemName => this
        .Items.Where(item => item.Quantity >= 2)
        .OrderBy(item => item.Name)
        .Select(item => item.Name)
        .FirstOrDefault();
}

public sealed class HelperProjectionCustomer
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Tier { get; set; } = string.Empty;
}

public sealed class HelperProjectionItem
{
    public string Name { get; set; } = string.Empty;

    public int Quantity { get; set; }
}

public partial class HelperAsLeftJoinOrderDto;

public partial class HelperAsInnerJoinOrderDto;

public partial class HelperAsProjectableOrderDto;

public partial class HelperAsProjectionExplicitOrderDto;

public partial class HelperExplicitProjectedCustomerDto;

public partial class HelperAsProjectionImplicitOrderDto;

public partial class HelperProjectExplicitOrderDto;

public partial class HelperProjectImplicitOrderDto;
