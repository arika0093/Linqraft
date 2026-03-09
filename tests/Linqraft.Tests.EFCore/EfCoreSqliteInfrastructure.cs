using System.Linq;
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
