using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Linqraft.Tests.EFCore;

internal sealed class EfCoreSqliteDbContext(DbContextOptions<EfCoreSqliteDbContext> options)
    : DbContext(options)
{
    public DbSet<EfOrder> Orders => Set<EfOrder>();

    public DbSet<EfCustomer> Customers => Set<EfCustomer>();

    public DbSet<EfOrderItem> OrderItems => Set<EfOrderItem>();

    public DbSet<EfPaymentBase> Payments => Set<EfPaymentBase>();

    public DbSet<EfRewardBase> Rewards => Set<EfRewardBase>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder
            .Entity<EfPaymentBase>()
            .HasDiscriminator<string>("PaymentType")
            .HasValue<EfCardPayment>("card")
            .HasValue<EfBankTransferPayment>("bank");

        modelBuilder
            .Entity<EfPaymentBase>()
            .HasOne(payment => payment.Order)
            .WithMany(order => order.Payments)
            .HasForeignKey(payment => payment.OrderId);

        modelBuilder.Entity<EfRewardBase>().UseTpcMappingStrategy();
        modelBuilder.Entity<EfPointsReward>().ToTable("EfPointsRewards");
        modelBuilder.Entity<EfCouponReward>().ToTable("EfCouponRewards");
        modelBuilder
            .Entity<EfRewardBase>()
            .HasOne(reward => reward.Customer)
            .WithMany(customer => customer.Rewards)
            .HasForeignKey(reward => reward.CustomerId);
    }
}

internal static partial class EfOrderQueries
{
    [LinqraftMappingGenerate("ProjectToEfCompiledOrderRow")]
    internal static IQueryable<EfCompiledOrderRow> Template(this IQueryable<EfOrder> source)
    {
        return source.SelectExpr<EfOrder, EfCompiledOrderRow>(order => new
        {
            order.Id,
            order.OrderNumber,
            TotalAmount = order.Items.Sum(item => item.Quantity * item.UnitPrice),
        });
    }
}

public partial class EfSqliteOrderRow;

public partial class EfCompiledOrderRow;

public sealed class EfCoreSqliteProjectionTests
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
                HighValueItemCount = order.Items.Count(item => item.Quantity * item.UnitPrice >= 30),
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
                HighValueItemCount = order.Items.Count(item => item.Quantity * item.UnitPrice >= 30),
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
}

internal sealed class SqliteTestDatabase : IAsyncDisposable
{
    private SqliteTestDatabase(SqliteConnection connection, EfCoreSqliteDbContext context)
    {
        Connection = connection;
        Context = context;
    }

    public SqliteConnection Connection { get; }

    public EfCoreSqliteDbContext Context { get; }

    public static async Task<SqliteTestDatabase> CreateAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<EfCoreSqliteDbContext>()
            .UseSqlite(connection)
            .Options;

        var context = new EfCoreSqliteDbContext(options);
        await context.Database.EnsureCreatedAsync();
        await SeedAsync(context);

        return new SqliteTestDatabase(connection, context);
    }

    public async ValueTask DisposeAsync()
    {
        await Context.DisposeAsync();
        await Connection.DisposeAsync();
    }

    private static async Task SeedAsync(EfCoreSqliteDbContext context)
    {
        var customers = CreateCustomers();
        var orders = CreateOrders(customers);

        context.Customers.AddRange(customers);
        context.Orders.AddRange(orders);
        await context.SaveChangesAsync();
    }

    private static List<EfCustomer> CreateCustomers()
    {
        var rewardId = 1;
        return
        [
            new()
            {
                Name = "Ada",
                Rewards =
                [
                    new EfPointsReward
                    {
                        Id = rewardId++,
                        Label = "Ada Starter",
                        Points = 120,
                    },
                    new EfCouponReward
                    {
                        Id = rewardId++,
                        Label = "Ada VIP",
                        CouponCode = "ADA10",
                        DiscountAmount = 10,
                    },
                ],
            },
            new()
            {
                Name = "Grace",
                Rewards =
                [
                    new EfPointsReward
                    {
                        Id = rewardId++,
                        Label = "Grace Loyalty",
                        Points = 220,
                    },
                ],
            },
            new()
            {
                Name = "Margaret",
                Rewards =
                [
                    new EfCouponReward
                    {
                        Id = rewardId++,
                        Label = "Margaret Shipping",
                        CouponCode = "SHIPFREE",
                        DiscountAmount = 0,
                    },
                    new EfPointsReward
                    {
                        Id = rewardId++,
                        Label = "Margaret Bonus",
                        Points = 80,
                    },
                ],
            },
            new()
            {
                Name = "Linus",
                Rewards =
                [
                    new EfCouponReward
                    {
                        Id = rewardId++,
                        Label = "Linus Save",
                        CouponCode = "LINUS5",
                        DiscountAmount = 5,
                    },
                ],
            },
            new()
            {
                Name = "Barbara",
                Rewards =
                [
                    new EfPointsReward
                    {
                        Id = rewardId++,
                        Label = "Barbara Gold",
                        Points = 340,
                    },
                    new EfCouponReward
                    {
                        Id = rewardId++,
                        Label = "Barbara VIP",
                        CouponCode = "BARB20",
                        DiscountAmount = 20,
                    },
                ],
            },
        ];
    }

    private static List<EfOrder> CreateOrders(IReadOnlyList<EfCustomer> customers)
    {
        var orders = new List<EfOrder>
        {
            new()
            {
                OrderNumber = "ORD-001",
                CreatedOn = new DateTime(2024, 1, 1),
                Customer = customers[0],
                Items =
                [
                    new EfOrderItem
                    {
                        ProductName = "Keyboard",
                        Quantity = 2,
                        UnitPrice = 10,
                    },
                    new EfOrderItem
                    {
                        ProductName = "Cable",
                        Quantity = 1,
                        UnitPrice = 5,
                    },
                ],
                Payments = [new EfCardPayment { Amount = 25, Last4 = "1001" }],
            },
            new()
            {
                OrderNumber = "ORD-002",
                CreatedOn = new DateTime(2024, 1, 2),
                Customer = null,
                Items =
                [
                    new EfOrderItem
                    {
                        ProductName = "Mouse",
                        Quantity = 3,
                        UnitPrice = 7,
                    },
                ],
                Payments =
                [
                    new EfBankTransferPayment
                    {
                        Amount = 21,
                        Reference = "BANK-0002",
                    },
                ],
            },
        };

        for (var index = 3; index <= 120; index++)
        {
            var customer = index % 6 == 0 ? null : customers[(index - 3) % customers.Count];
            var items = CreateItems(index);
            var totalAmount = items.Sum(item => item.Quantity * item.UnitPrice);

            orders.Add(
                new EfOrder
                {
                    OrderNumber = $"ORD-{index:000}",
                    CreatedOn = new DateTime(2024, 1, 1).AddDays(index),
                    Customer = customer,
                    Items = items,
                    Payments = CreatePayments(index, totalAmount),
                }
            );
        }

        return orders;
    }

    private static List<EfOrderItem> CreateItems(int index)
    {
        var items = new List<EfOrderItem>
        {
            new()
            {
                ProductName = index % 2 == 0 ? "Keyboard Bundle" : "Mouse Bundle",
                Quantity = 1 + (index % 4),
                UnitPrice = 6 + (index % 5),
            },
            new()
            {
                ProductName = index % 3 == 0 ? "Dock" : "Cable",
                Quantity = 1 + ((index + 1) % 3),
                UnitPrice = 4 + ((index * 2) % 6),
            },
        };

        if (index % 5 == 0)
        {
            items.Add(
                new EfOrderItem
                {
                    ProductName = "Monitor",
                    Quantity = 2,
                    UnitPrice = 15,
                }
            );
        }

        if (index % 7 == 0)
        {
            items.Add(
                new EfOrderItem
                {
                    ProductName = "Adapter",
                    Quantity = 3,
                    UnitPrice = 8,
                }
            );
        }

        return items;
    }

    private static List<EfPaymentBase> CreatePayments(int orderIndex, int totalAmount)
    {
        if (orderIndex % 5 == 0)
        {
            var cardAmount = totalAmount / 2;
            return
            [
                new EfCardPayment
                {
                    Amount = cardAmount,
                    Last4 = (1000 + orderIndex).ToString("0000"),
                },
                new EfBankTransferPayment
                {
                    Amount = totalAmount - cardAmount,
                    Reference = $"BANK-{orderIndex:0000}",
                },
            ];
        }

        if (orderIndex % 2 == 0)
        {
            return
            [
                new EfBankTransferPayment
                {
                    Amount = totalAmount,
                    Reference = $"BANK-{orderIndex:0000}",
                },
            ];
        }

        return
        [
            new EfCardPayment
            {
                Amount = totalAmount,
                Last4 = (1000 + orderIndex).ToString("0000"),
            },
        ];
    }
}

public sealed class EfOrder
{
    public int Id { get; set; }

    public string OrderNumber { get; set; } = string.Empty;

    public DateTime CreatedOn { get; set; }

    public int? CustomerId { get; set; }

    public EfCustomer? Customer { get; set; }

    public List<EfOrderItem> Items { get; set; } = [];

    public List<EfPaymentBase> Payments { get; set; } = [];
}

public sealed class EfCustomer
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public List<EfOrder> Orders { get; set; } = [];

    public List<EfRewardBase> Rewards { get; set; } = [];
}

public sealed class EfOrderItem
{
    public int Id { get; set; }

    public int OrderId { get; set; }

    public EfOrder Order { get; set; } = null!;

    public string ProductName { get; set; } = string.Empty;

    public int Quantity { get; set; }

    public int UnitPrice { get; set; }
}

public abstract class EfPaymentBase
{
    public int Id { get; set; }

    public int OrderId { get; set; }

    public EfOrder Order { get; set; } = null!;

    public int Amount { get; set; }
}

public sealed class EfCardPayment : EfPaymentBase
{
    public string Last4 { get; set; } = string.Empty;
}

public sealed class EfBankTransferPayment : EfPaymentBase
{
    public string Reference { get; set; } = string.Empty;
}

public abstract class EfRewardBase
{
    public int Id { get; set; }

    public int CustomerId { get; set; }

    public EfCustomer Customer { get; set; } = null!;

    public string Label { get; set; } = string.Empty;
}

public sealed class EfPointsReward : EfRewardBase
{
    public int Points { get; set; }
}

public sealed class EfCouponReward : EfRewardBase
{
    public string CouponCode { get; set; } = string.Empty;

    public int DiscountAmount { get; set; }
}
