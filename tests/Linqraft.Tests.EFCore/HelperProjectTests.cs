using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Linqraft.Tests.EFCore;

public sealed class HelperProjectTests
{
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
                        SelectedCustomer = helper
                            .Project(order.Customer!)
                            .Select(customer => new { customer.Id, customer.Name }),
                    }
            )
            .SingleAsync();

        result.SelectedCustomer.GetType().Name.ShouldBe("SelectedCustomerDto");
        result.OrderNumber.ShouldBe(expected.OrderNumber);
        result.SelectedCustomer.Id.ShouldBe(expected.CustomerId);
        result.SelectedCustomer.Name.ShouldBe(expected.CustomerName);
    }
}

public partial class EfHelperProjectExplicitOrderDto;

public partial class EfHelperProjectImplicitOrderDto;
