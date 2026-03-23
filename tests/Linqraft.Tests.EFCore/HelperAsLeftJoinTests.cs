using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Linqraft.Tests.EFCore;

public sealed class HelperAsLeftJoinTests
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
}
