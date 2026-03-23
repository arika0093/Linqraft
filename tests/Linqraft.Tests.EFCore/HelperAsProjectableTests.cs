using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Linqraft.Tests.EFCore;

public sealed class HelperAsProjectableTests
{
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
}
