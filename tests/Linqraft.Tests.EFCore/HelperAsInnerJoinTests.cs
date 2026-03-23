using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Linqraft.Tests.EFCore;

public sealed class HelperAsInnerJoinTests
{
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
            .Select(order => new { order.OrderNumber, CustomerName = order.Customer!.Name })
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

        result.Select(row => new { row.OrderNumber, row.CustomerName }).ToList().ShouldBe(expected);
    }
}
