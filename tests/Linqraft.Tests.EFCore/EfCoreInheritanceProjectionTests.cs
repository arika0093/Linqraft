using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Linqraft.Tests.EFCore;

public sealed class EfCoreInheritanceProjectionTests
{
    [Test]
    public async Task SelectExpr_projects_tph_oftype_over_sqlite()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();

        var query = database
            .Context.Orders.AsNoTracking()
            .Where(order => order.Payments.OfType<EfCardPayment>().Any())
            .OrderBy(order => order.OrderNumber)
            .Take(10);

        var expected = await query
            .Select(order => new
            {
                order.OrderNumber,
                CardPaymentCount = order.Payments.OfType<EfCardPayment>().Count(),
                CardAmount = order.Payments.OfType<EfCardPayment>().Sum(payment => payment.Amount),
                FirstCardLast4 = order
                    .Payments.OfType<EfCardPayment>()
                    .OrderBy(payment => payment.Id)
                    .Select(payment => payment.Last4)
                    .FirstOrDefault(),
                FirstTransferReference = order
                    .Payments.OfType<EfBankTransferPayment>()
                    .OrderBy(payment => payment.Id)
                    .Select(payment => payment.Reference)
                    .FirstOrDefault(),
            })
            .ToListAsync();

        var result = await query
            .SelectExpr(order => new
            {
                order.OrderNumber,
                CardPaymentCount = order.Payments.OfType<EfCardPayment>().Count(),
                CardAmount = order.Payments.OfType<EfCardPayment>().Sum(payment => payment.Amount),
                FirstCardLast4 = order
                    .Payments.OfType<EfCardPayment>()
                    .OrderBy(payment => payment.Id)
                    .Select(payment => payment.Last4)
                    .FirstOrDefault(),
                FirstTransferReference = order
                    .Payments.OfType<EfBankTransferPayment>()
                    .OrderBy(payment => payment.Id)
                    .Select(payment => payment.Reference)
                    .FirstOrDefault(),
            })
            .ToListAsync();

        result
            .Select(row => new
            {
                row.OrderNumber,
                row.CardPaymentCount,
                row.CardAmount,
                row.FirstCardLast4,
                row.FirstTransferReference,
            })
            .ToList()
            .ShouldBe(expected);
    }

    [Test]
    public async Task SelectExpr_projects_tpc_oftype_over_sqlite()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();

        var query = database
            .Context.Customers.AsNoTracking()
            .Where(customer => customer.Rewards.Count != 0)
            .OrderBy(customer => customer.Name);

        var expected = await query
            .Select(customer => new
            {
                customer.Name,
                PointsTotal = customer.Rewards.OfType<EfPointsReward>().Sum(reward => reward.Points),
                CouponCount = customer.Rewards.OfType<EfCouponReward>().Count(),
                FirstCouponCode = customer
                    .Rewards.OfType<EfCouponReward>()
                    .OrderBy(reward => reward.Id)
                    .Select(reward => reward.CouponCode)
                    .FirstOrDefault(),
            })
            .ToListAsync();

        var result = await query
            .SelectExpr(customer => new
            {
                customer.Name,
                PointsTotal = customer.Rewards.OfType<EfPointsReward>().Sum(reward => reward.Points),
                CouponCount = customer.Rewards.OfType<EfCouponReward>().Count(),
                FirstCouponCode = customer
                    .Rewards.OfType<EfCouponReward>()
                    .OrderBy(reward => reward.Id)
                    .Select(reward => reward.CouponCode)
                    .FirstOrDefault(),
            })
            .ToListAsync();

        result
            .Select(row => new
            {
                row.Name,
                row.PointsTotal,
                row.CouponCount,
                row.FirstCouponCode,
            })
            .ToList()
            .ShouldBe(expected);
    }

    [Test]
    public async Task SelectExpr_projects_direct_payment_root_queries_over_sqlite()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();

        var query = database
            .Context.Payments.AsNoTracking()
            .OfType<EfCardPayment>()
            .Where(payment => payment.Amount >= 25)
            .OrderBy(payment => payment.Order.OrderNumber)
            .Take(12);

        var expected = await query
            .Select(payment => new
            {
                payment.Order.OrderNumber,
                payment.Last4,
                payment.Amount,
                CustomerName = payment.Order.Customer != null ? payment.Order.Customer.Name : null,
                ItemCount = payment.Order.Items.Count,
            })
            .ToListAsync();

        var result = await query
            .SelectExpr(payment => new
            {
                payment.Order.OrderNumber,
                payment.Last4,
                payment.Amount,
                CustomerName = payment.Order.Customer != null ? payment.Order.Customer.Name : null,
                ItemCount = payment.Order.Items.Count,
            })
            .ToListAsync();

        result
            .Select(row => new
            {
                row.OrderNumber,
                row.Last4,
                row.Amount,
                row.CustomerName,
                row.ItemCount,
            })
            .ToList()
            .ShouldBe(expected);
    }

    [Test]
    public async Task SelectExpr_projects_direct_reward_root_queries_over_sqlite()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();

        var query = database
            .Context.Rewards.AsNoTracking()
            .OfType<EfCouponReward>()
            .Where(reward => reward.DiscountAmount >= 5)
            .OrderBy(reward => reward.Customer.Name)
            .ThenBy(reward => reward.CouponCode);

        var expected = await query
            .Select(reward => new
            {
                CustomerName = reward.Customer.Name,
                reward.CouponCode,
                reward.DiscountAmount,
                RewardCount = reward.Customer.Rewards.Count,
                LatestOrderNumber = reward.Customer
                    .Orders.OrderByDescending(order => order.CreatedOn)
                    .Select(order => order.OrderNumber)
                    .FirstOrDefault(),
            })
            .ToListAsync();

        var result = await query
            .SelectExpr(reward => new
            {
                CustomerName = reward.Customer.Name,
                reward.CouponCode,
                reward.DiscountAmount,
                RewardCount = reward.Customer.Rewards.Count,
                LatestOrderNumber = reward.Customer
                    .Orders.OrderByDescending(order => order.CreatedOn)
                    .Select(order => order.OrderNumber)
                    .FirstOrDefault(),
            })
            .ToListAsync();

        result
            .Select(row => new
            {
                row.CustomerName,
                row.CouponCode,
                row.DiscountAmount,
                row.RewardCount,
                row.LatestOrderNumber,
            })
            .ToList()
            .ShouldBe(expected);
    }
}
