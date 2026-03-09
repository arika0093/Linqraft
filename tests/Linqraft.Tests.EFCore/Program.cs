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
            .OrderBy(order => order.OrderNumber)
            .ProjectToEfCompiledOrderRow()
            .ToListAsync();

        result.Count.ShouldBe(2);
        result[0].OrderNumber.ShouldBe("ORD-001");
        result[0].TotalAmount.ShouldBe(25);
        result[1].OrderNumber.ShouldBe("ORD-002");
        result[1].TotalAmount.ShouldBe(21);
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
        var ada = new EfCustomer { Name = "Ada" };
        var order1 = new EfOrder
        {
            OrderNumber = "ORD-001",
            Customer = ada,
            Items =
            [
                new()
                {
                    ProductName = "Keyboard",
                    Quantity = 2,
                    UnitPrice = 10,
                },
                new()
                {
                    ProductName = "Cable",
                    Quantity = 1,
                    UnitPrice = 5,
                },
            ],
        };
        var order2 = new EfOrder
        {
            OrderNumber = "ORD-002",
            Customer = null,
            Items =
            [
                new()
                {
                    ProductName = "Mouse",
                    Quantity = 3,
                    UnitPrice = 7,
                },
            ],
        };

        context.Orders.AddRange(order1, order2);
        await context.SaveChangesAsync();
    }
}

public sealed class EfOrder
{
    public int Id { get; set; }

    public string OrderNumber { get; set; } = string.Empty;

    public int? CustomerId { get; set; }

    public EfCustomer? Customer { get; set; }

    public List<EfOrderItem> Items { get; set; } = [];
}

public sealed class EfCustomer
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public List<EfOrder> Orders { get; set; } = [];
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
