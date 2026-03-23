using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Linqraft.Tests.EFCore;

public sealed class HelperAsProjectionTests
{
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
                (order, helper) =>
                    new
                    {
                        order.OrderNumber,
                        Customer = helper.AsProjection<EfHelperProjectedCustomerDto>(
                            order.Customer
                        ),
                    }
            )
            .SingleAsync();

        var customer = result.Customer.ShouldNotBeNull();
        customer.GetType().ShouldBe(typeof(EfHelperProjectedCustomerDto));
        result.OrderNumber.ShouldBe(expected.OrderNumber);
        customer.Id.ShouldBe(expected.CustomerId);
        customer.Name.ShouldBe(expected.CustomerName);
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
                (order, helper) => new { order.OrderNumber, Customer = helper.AsProjection(order.Customer) }
            )
            .SingleAsync();

        var customer = result.Customer.ShouldNotBeNull();
        customer.GetType().Name.ShouldBe("EfCustomerDto");
        result.OrderNumber.ShouldBe(expected.OrderNumber);
        customer.Id.ShouldBe(expected.CustomerId);
        customer.Name.ShouldBe(expected.CustomerName);
    }
}

public partial class EfHelperAsProjectionExplicitOrderDto;

public partial class EfHelperProjectedCustomerDto;

public partial class EfHelperAsProjectionImplicitOrderDto;
