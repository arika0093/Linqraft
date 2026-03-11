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

    public DbSet<EfShipment> Shipments => Set<EfShipment>();

    public DbSet<EfShipmentEvent> ShipmentEvents => Set<EfShipmentEvent>();

    public DbSet<EfShipmentEventSummary> ShipmentEventSummaries => Set<EfShipmentEventSummary>();

    public DbSet<EfShipmentFee> ShipmentFees => Set<EfShipmentFee>();

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

        modelBuilder
            .Entity<EfOrder>()
            .HasOne(order => order.Shipment)
            .WithOne(shipment => shipment.Order)
            .HasForeignKey<EfShipment>(shipment => shipment.OrderId);

        modelBuilder
            .Entity<EfShipment>()
            .HasMany(shipment => shipment.Events)
            .WithOne(@event => @event.Shipment)
            .HasForeignKey(@event => @event.ShipmentId);

        modelBuilder
            .Entity<EfShipmentEvent>()
            .HasOne(@event => @event.Summary)
            .WithOne(summary => summary.ShipmentEvent)
            .HasForeignKey<EfShipmentEventSummary>(summary => summary.ShipmentEventId);

        modelBuilder
            .Entity<EfShipmentEvent>()
            .HasMany(@event => @event.Fees)
            .WithOne(fee => fee.ShipmentEvent)
            .HasForeignKey(fee => fee.ShipmentEventId);
    }
}
