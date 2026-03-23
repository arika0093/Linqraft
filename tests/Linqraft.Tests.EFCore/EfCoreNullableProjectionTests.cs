using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Linqraft.Tests.EFCore;

public sealed class EfCoreNullableProjectionTests
{
    [Test]
    public async Task SelectExpr_projects_nullable_navigation_firstordefault_and_nested_sums_over_sqlite()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();

        var query = database
            .Context.Orders.AsNoTracking()
            .Where(order =>
                order.OrderNumber == "ORD-001"
                || order.OrderNumber == "ORD-002"
                || order.OrderNumber == "ORD-003"
            )
            .OrderBy(order => order.OrderNumber);

        var expected = await CreateExpectedRowsAsync(query);

        var result = await query
            .UseLinqraft()
            .Select(order => new
            {
                order.OrderNumber,
                CarrierName = order.Shipment?.CarrierName,
                FirstEvent = order.Shipment?.Events.OrderBy(evt => evt.Sequence).FirstOrDefault(),
                TotalFeeAmount = order.Shipment?.Events.Sum(evt =>
                    evt.Fees!.Sum(fee => fee.Amount)
                ),
                TotalSurcharge = order
                    .Shipment?.Events.Where(evt => evt.Summary != null)
                    .Sum(evt => evt.Summary!.Surcharge),
            })
            .ToListAsync();

        result
            .Select(row => new EfNullableShipmentExpectation(
                row.OrderNumber,
                row.CarrierName,
                row.FirstEvent?.Code,
                row.TotalFeeAmount,
                row.TotalSurcharge
            ))
            .ToList()
            .ShouldBe(expected);
    }

    [Test]
    public async Task MappingDeclare_projects_nullable_navigation_firstordefault_and_nested_sums_over_sqlite()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();

        var query = database
            .Context.Orders.AsNoTracking()
            .Where(order =>
                order.OrderNumber == "ORD-001"
                || order.OrderNumber == "ORD-002"
                || order.OrderNumber == "ORD-003"
            )
            .OrderBy(order => order.OrderNumber);

        var expected = await CreateExpectedRowsAsync(query);

        var result = await query.ProjectToEfNullableShipmentRow().ToListAsync();

        result.Select(row => ToExpectation(row)).ToList().ShouldBe(expected);
    }

    private static async Task<List<EfNullableShipmentExpectation>> CreateExpectedRowsAsync(
        IQueryable<EfOrder> query
    )
    {
        var orders = await query
            .Include(order => order.Shipment)
                .ThenInclude(shipment => shipment!.Events)
                    .ThenInclude(evt => evt.Summary)
            .Include(order => order.Shipment)
                .ThenInclude(shipment => shipment!.Events)
                    .ThenInclude(evt => evt.Fees)
            .ToListAsync();

        return orders.Select(ToExpectation).ToList();
    }

    private static EfNullableShipmentExpectation ToExpectation(EfOrder order)
    {
        return new EfNullableShipmentExpectation(
            order.OrderNumber,
            order.Shipment?.CarrierName,
            order.Shipment?.Events.OrderBy(evt => evt.Sequence).FirstOrDefault()?.Code,
            order.Shipment?.Events.Sum(evt => evt.Fees!.Sum(fee => fee.Amount)),
            order
                .Shipment?.Events.Where(evt => evt.Summary != null)
                .Sum(evt => evt.Summary!.Surcharge)
        );
    }

    private static EfNullableShipmentExpectation ToExpectation(EfNullableShipmentRow row)
    {
        return new EfNullableShipmentExpectation(
            row.OrderNumber,
            row.CarrierName,
            row.FirstEvent?.Code,
            row.TotalFeeAmount,
            row.TotalSurcharge
        );
    }
}

public sealed record EfNullableShipmentExpectation(
    string OrderNumber,
    string? CarrierName,
    string? FirstEventCode,
    int? TotalFeeAmount,
    int? TotalSurcharge
);
