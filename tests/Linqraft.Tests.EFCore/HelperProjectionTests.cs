using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Linqraft.Tests.EFCore;

public sealed class HelperProjectionTests
{
    [Test]
    public async Task Helper_AsLeftJoin_preserves_rows_over_sqlite()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();

        var query = database
            .Context.Orders.AsNoTracking()
            .Where(order => order.OrderNumber == "ORD-001" || order.OrderNumber == "ORD-002")
            .OrderBy(order => order.OrderNumber);

        var expected = await query
            .Select(order => new
            {
                order.OrderNumber,
                CustomerName = order.Customer != null ? order.Customer.Name : null,
            })
            .ToListAsync();

        var result = await query
            .SelectExpr(
                (order, helper) =>
                    new
                    {
                        order.OrderNumber,
                        CustomerName = helper.AsLeftJoin(order.Customer!).Name,
                    }
            )
            .ToListAsync();

        result
            .Select(row => new { row.OrderNumber, CustomerName = (string?)row.CustomerName })
            .ToList()
            .ShouldBe(expected);
    }

    [Test]
    public async Task Helper_AsInnerJoin_filters_rows_over_sqlite()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();

        var query = database
            .Context.Orders.AsNoTracking()
            .Where(order => order.OrderNumber == "ORD-001" || order.OrderNumber == "ORD-002")
            .OrderBy(order => order.OrderNumber);

        var expected = await query
            .Where(order => order.Customer != null)
            .Select(order => new
            {
                order.OrderNumber,
                CustomerName = order.Customer!.Name,
            })
            .ToListAsync();

        var result = await query
            .SelectExpr(
                (order, helper) =>
                    new
                    {
                        order.OrderNumber,
                        CustomerName = helper.AsInnerJoin(order.Customer!).Name,
                    }
            )
            .ToListAsync();

        result
            .Select(row => new { row.OrderNumber, row.CustomerName })
            .ToList()
            .ShouldBe(expected);
    }

    [Test]
    public async Task Helper_AsProjectable_inlines_computed_property_over_sqlite()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();

        var query = database
            .Context.Orders.AsNoTracking()
            .Where(order => order.OrderNumber == "ORD-001" || order.OrderNumber == "ORD-002")
            .OrderBy(order => order.OrderNumber);

        var expected = await query
            .Select(order => new
            {
                order.OrderNumber,
                FirstLargeItemProductName = order
                    .Items.Where(item => item.Quantity >= 2)
                    .OrderBy(item => item.Id)
                    .Select(item => item.ProductName)
                    .FirstOrDefault(),
            })
            .ToListAsync();

        var result = await query
            .SelectExpr(
                (order, helper) =>
                    new
                    {
                        order.OrderNumber,
                        FirstLargeItemProductName = helper.AsProjectable(
                            order.FirstLargeItemProductName
                        ),
                    }
            )
            .ToListAsync();

        result
            .Select(row => new { row.OrderNumber, row.FirstLargeItemProductName })
            .ToList()
            .ShouldBe(expected);
    }

    [Test]
    public async Task Helper_AsProjection_creates_explicit_nested_dto_over_sqlite()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();

        var expected = await database
            .Context.Orders.AsNoTracking()
            .Where(order => order.OrderNumber == "ORD-001")
            .Select(order => new
            {
                order.OrderNumber,
                CustomerId = order.Customer!.Id,
                CustomerName = order.Customer.Name,
            })
            .SingleAsync();

        var result = await database
            .Context.Orders.AsNoTracking()
            .Where(order => order.OrderNumber == "ORD-001")
            .SelectExpr<EfOrder, EfHelperAsProjectionExplicitOrderDto>(
                order =>
                    new
                    {
                        order.OrderNumber,
                        Customer = order.Customer!.AsProjection<EfHelperProjectedCustomerDto>(),
                    }
            )
            .SingleAsync();

        result.Customer.GetType().ShouldBe(typeof(EfHelperProjectedCustomerDto));
        result.OrderNumber.ShouldBe(expected.OrderNumber);
        result.Customer.Id.ShouldBe(expected.CustomerId);
        result.Customer.Name.ShouldBe(expected.CustomerName);
    }

    [Test]
    public async Task Helper_AsProjection_without_generic_uses_source_type_dto_name_over_sqlite()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();

        var expected = await database
            .Context.Orders.AsNoTracking()
            .Where(order => order.OrderNumber == "ORD-001")
            .Select(order => new
            {
                order.OrderNumber,
                CustomerId = order.Customer!.Id,
                CustomerName = order.Customer.Name,
            })
            .SingleAsync();

        var result = await database
            .Context.Orders.AsNoTracking()
            .Where(order => order.OrderNumber == "ORD-001")
            .SelectExpr<EfOrder, EfHelperAsProjectionImplicitOrderDto>(
                (order, helper) =>
                    new
                    {
                        order.OrderNumber,
                        Customer = helper.AsProjection(order.Customer!),
                    }
            )
            .SingleAsync();

        result.Customer.GetType().Name.ShouldBe("EfCustomerDto");
        result.OrderNumber.ShouldBe(expected.OrderNumber);
        result.Customer.Id.ShouldBe(expected.CustomerId);
        result.Customer.Name.ShouldBe(expected.CustomerName);
    }

    [Test]
    public async Task Helper_Project_with_generic_uses_explicit_name_hint_over_sqlite()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();

        var expected = await database
            .Context.Orders.AsNoTracking()
            .Where(order => order.OrderNumber == "ORD-001")
            .Select(order => new
            {
                order.OrderNumber,
                CustomerId = order.Customer!.Id,
                CustomerName = order.Customer.Name,
            })
            .SingleAsync();

        var result = await database
            .Context.Orders.AsNoTracking()
            .Where(order => order.OrderNumber == "ORD-001")
            .SelectExpr<EfOrder, EfHelperProjectExplicitOrderDto>(
                (order, helper) =>
                    new
                    {
                        order.OrderNumber,
                        SelectedCustomer = helper
                            .Project<EfCustomer>(order.Customer!)
                            .Select(customer => new { customer.Id, customer.Name }),
                    }
            )
            .SingleAsync();

        result.SelectedCustomer.GetType().Name.ShouldBe("EfCustomerDto");
        result.OrderNumber.ShouldBe(expected.OrderNumber);
        result.SelectedCustomer.Id.ShouldBe(expected.CustomerId);
        result.SelectedCustomer.Name.ShouldBe(expected.CustomerName);
    }

    [Test]
    public async Task Helper_Project_without_generic_uses_automatic_name_over_sqlite()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();

        var expected = await database
            .Context.Orders.AsNoTracking()
            .Where(order => order.OrderNumber == "ORD-001")
            .Select(order => new
            {
                order.OrderNumber,
                CustomerId = order.Customer!.Id,
                CustomerName = order.Customer.Name,
            })
            .SingleAsync();

        var result = await database
            .Context.Orders.AsNoTracking()
            .Where(order => order.OrderNumber == "ORD-001")
            .SelectExpr<EfOrder, EfHelperProjectImplicitOrderDto>(
                (order, helper) =>
                    new
                    {
                        order.OrderNumber,
                        SelectedCustomer = helper.Project(order.Customer!).Select(customer => new
                        {
                            customer.Id,
                            customer.Name,
                        }),
                    }
            )
            .SingleAsync();

        result.SelectedCustomer.GetType().Name.ShouldBe("SelectedCustomerDto");
        result.OrderNumber.ShouldBe(expected.OrderNumber);
        result.SelectedCustomer.Id.ShouldBe(expected.CustomerId);
        result.SelectedCustomer.Name.ShouldBe(expected.CustomerName);
    }
}

public partial class EfHelperAsProjectionExplicitOrderDto;

public partial class EfHelperProjectedCustomerDto;

public partial class EfHelperAsProjectionImplicitOrderDto;

public partial class EfHelperProjectExplicitOrderDto;

public partial class EfHelperProjectImplicitOrderDto;
