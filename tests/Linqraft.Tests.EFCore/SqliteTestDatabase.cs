using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Linqraft.Tests.EFCore;

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
