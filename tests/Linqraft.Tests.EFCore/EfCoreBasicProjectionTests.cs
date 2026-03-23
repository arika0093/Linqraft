using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Linqraft.Tests.EFCore;

public sealed class EfCoreBasicProjectionTests
{
    [Test]
    public async Task SelectExpr_projects_a_relational_query_over_sqlite()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();

        var result = await database
            .Context.Orders.AsNoTracking()
            .Where(order => order.OrderNumber == "ORD-001" || order.OrderNumber == "ORD-002")
            .OrderBy(order => order.OrderNumber)
            .SelectExpr<EfOrder, EfSqliteOrderRow>(order => new
            {
                order.Id,
                order.OrderNumber,
                CustomerName = order.Customer?.Name,
                ItemCount = order.Items.Count,
            })
            .ToListAsync();

        result.Count.ShouldBe(2);
        result[0].OrderNumber.ShouldBe("ORD-001");
        result[0].CustomerName.ShouldBe("Ada");
        result[0].ItemCount.ShouldBe(2);
        result[1].OrderNumber.ShouldBe("ORD-002");
        result[1].CustomerName.ShouldBeNull();
        result[1].ItemCount.ShouldBe(1);
    }

    [Test]
    public async Task UseLinqraft_select_projects_a_relational_query_over_sqlite()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();

        var result = await database
            .Context.Orders.AsNoTracking()
            .Where(order => order.OrderNumber == "ORD-001" || order.OrderNumber == "ORD-002")
            .OrderBy(order => order.OrderNumber)
            .UseLinqraft()
            .Select<EfFluentSqliteOrderRow>(
                (order, helper) => new
                {
                    order.Id,
                    order.OrderNumber,
                    CustomerName = helper.AsLeftJoin(order.Customer).Name,
                    ItemCount = order.Items.Count,
                }
            )
            .ToListAsync();

        result.Count.ShouldBe(2);
        result[0].OrderNumber.ShouldBe("ORD-001");
        result[0].CustomerName.ShouldBe("Ada");
        result[0].ItemCount.ShouldBe(2);
        result[1].OrderNumber.ShouldBe("ORD-002");
        result[1].CustomerName.ShouldBeNull();
        result[1].ItemCount.ShouldBe(1);
    }

    [Test]
    public async Task UseLinqraft_groupby_projects_order_summaries_over_sqlite()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();

        var result = await database
            .Context.Orders.AsNoTracking()
            .Where(order => order.OrderNumber == "ORD-001" || order.OrderNumber == "ORD-002")
            .UseLinqraft()
            .GroupBy<string, EfFluentSqliteOrderGroupRow>(
                order => order.Customer != null ? order.Customer.Name : "Guest",
                group => new
                {
                    CustomerName = group.Key,
                    OrderCount = group.Count(),
                    ItemCount = group.Sum(order => order.Items.Count),
                }
            )
            .OrderBy(row => row.CustomerName)
            .ToListAsync();

        result.Count.ShouldBe(2);
        result[0].CustomerName.ShouldBe("Ada");
        result[0].OrderCount.ShouldBe(1);
        result[0].ItemCount.ShouldBe(2);
        result[1].CustomerName.ShouldBe("Guest");
        result[1].ItemCount.ShouldBe(1);
    }

    [Test]
    public async Task Generated_mapping_method_projects_over_sqlite()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();

        var result = await database
            .Context.Orders.AsNoTracking()
            .Where(order => order.OrderNumber == "ORD-001" || order.OrderNumber == "ORD-002")
            .OrderBy(order => order.OrderNumber)
            .ProjectToEfCompiledOrderRow()
            .ToListAsync();

        result.Count.ShouldBe(2);
        result[0].OrderNumber.ShouldBe("ORD-001");
        result[0].TotalAmount.ShouldBe(25);
        result[1].OrderNumber.ShouldBe("ORD-002");
        result[1].TotalAmount.ShouldBe(21);
    }

    [Test]
    public async Task SelectExpr_projects_large_filtered_query_over_sqlite()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();

        var customerNames = new[] { "Ada", "Grace", "Margaret" };
        var query = database
            .Context.Orders.AsNoTracking()
            .Where(order => order.Customer != null)
            .Where(order => customerNames.Contains(order.Customer!.Name))
            .Where(order => order.Items.Any(item => item.Quantity >= 2 && item.UnitPrice >= 7))
            .OrderBy(order => order.CreatedOn)
            .ThenBy(order => order.OrderNumber)
            .Skip(4)
            .Take(12);

        var expected = await query
            .Select(order => new
            {
                order.OrderNumber,
                CustomerName = order.Customer!.Name,
                ItemCount = order.Items.Count,
                HighValueItemCount = order.Items.Count(item =>
                    item.Quantity * item.UnitPrice >= 30
                ),
                TotalAmount = order.Items.Sum(item => item.Quantity * item.UnitPrice),
                FirstLargeItem = order
                    .Items.Where(item => item.Quantity >= 3)
                    .OrderBy(item => item.Id)
                    .Select(item => item.ProductName)
                    .FirstOrDefault(),
            })
            .ToListAsync();

        var result = await query
            .SelectExpr(order => new
            {
                order.OrderNumber,
                CustomerName = order.Customer!.Name,
                ItemCount = order.Items.Count,
                HighValueItemCount = order.Items.Count(item =>
                    item.Quantity * item.UnitPrice >= 30
                ),
                TotalAmount = order.Items.Sum(item => item.Quantity * item.UnitPrice),
                FirstLargeItem = order
                    .Items.Where(item => item.Quantity >= 3)
                    .OrderBy(item => item.Id)
                    .Select(item => item.ProductName)
                    .FirstOrDefault(),
            })
            .ToListAsync();

        result
            .Select(row => new
            {
                row.OrderNumber,
                row.CustomerName,
                row.ItemCount,
                row.HighValueItemCount,
                row.TotalAmount,
                row.FirstLargeItem,
            })
            .ToList()
            .ShouldBe(expected);
    }

    [Test]
    public async Task SelectExpr_projects_selectmany_aggregates_over_sqlite()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();

        var query = database
            .Context.Customers.AsNoTracking()
            .Where(customer => customer.Orders.Count != 0)
            .OrderBy(customer => customer.Name);

        var expected = await query
            .Select(customer => new
            {
                customer.Name,
                FlattenedItemCount = customer
                    .Orders.SelectMany(order => order.Items)
                    .Count(item => item.Quantity >= 2),
                DistinctProductCount = customer
                    .Orders.SelectMany(order => order.Items)
                    .Select(item => item.ProductName)
                    .Distinct()
                    .Count(),
                LatestOrderNumber = customer
                    .Orders.OrderByDescending(order => order.CreatedOn)
                    .Select(order => order.OrderNumber)
                    .FirstOrDefault(),
            })
            .ToListAsync();

        var result = await query
            .SelectExpr(customer => new
            {
                customer.Name,
                FlattenedItemCount = customer
                    .Orders.SelectMany(order => order.Items)
                    .Count(item => item.Quantity >= 2),
                DistinctProductCount = customer
                    .Orders.SelectMany(order => order.Items)
                    .Select(item => item.ProductName)
                    .Distinct()
                    .Count(),
                LatestOrderNumber = customer
                    .Orders.OrderByDescending(order => order.CreatedOn)
                    .Select(order => order.OrderNumber)
                    .FirstOrDefault(),
            })
            .ToListAsync();

        result
            .Select(row => new
            {
                row.Name,
                row.FlattenedItemCount,
                row.DistinctProductCount,
                row.LatestOrderNumber,
            })
            .ToList()
            .ShouldBe(expected);
    }

    [Test]
    public async Task SelectExpr_projects_customer_reward_summaries_over_sqlite()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();

        var selectedNames = new[] { "Ada", "Barbara", "Grace" };
        var query = database
            .Context.Customers.AsNoTracking()
            .Where(customer => selectedNames.Contains(customer.Name))
            .OrderBy(customer => customer.Name);

        var expected = await query
            .Select(customer => new
            {
                customer.Name,
                OrderCount = customer.Orders.Count,
                TotalSpent = customer
                    .Orders.SelectMany(order => order.Items)
                    .Sum(item => item.Quantity * item.UnitPrice),
                PrimaryCoupon = customer
                    .Rewards.OfType<EfCouponReward>()
                    .OrderBy(reward => reward.Id)
                    .Select(reward => reward.CouponCode)
                    .FirstOrDefault()
                    ?? "NONE",
                HasLargeCardPayment = customer
                    .Orders.SelectMany(order => order.Payments)
                    .OfType<EfCardPayment>()
                    .Any(payment => payment.Amount >= 30),
            })
            .ToListAsync();

        var result = await query
            .SelectExpr(customer => new
            {
                customer.Name,
                OrderCount = customer.Orders.Count,
                TotalSpent = customer
                    .Orders.SelectMany(order => order.Items)
                    .Sum(item => item.Quantity * item.UnitPrice),
                PrimaryCoupon = customer
                    .Rewards.OfType<EfCouponReward>()
                    .OrderBy(reward => reward.Id)
                    .Select(reward => reward.CouponCode)
                    .FirstOrDefault()
                    ?? "NONE",
                HasLargeCardPayment = customer
                    .Orders.SelectMany(order => order.Payments)
                    .OfType<EfCardPayment>()
                    .Any(payment => payment.Amount >= 30),
            })
            .ToListAsync();

        result
            .Select(row => new
            {
                row.Name,
                row.OrderCount,
                row.TotalSpent,
                row.PrimaryCoupon,
                row.HasLargeCardPayment,
            })
            .ToList()
            .ShouldBe(expected);
    }

    [Test]
    public async Task SelectExpr_AsLeftJoin_preserves_rows_for_nullable_navigation_over_sqlite()
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
    public async Task SelectExpr_AsProjectable_inlines_computed_query_property_over_sqlite()
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

public partial class EfFluentSqliteOrderRow { }

public partial class EfFluentSqliteOrderGroupRow { }
