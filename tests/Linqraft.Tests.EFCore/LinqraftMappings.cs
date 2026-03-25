using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Linqraft.Tests.EFCore;

internal static partial class EfOrderQueries
{
    [LinqraftMapping]
    internal static IQueryable<EfCompiledOrderRow> ProjectToEfCompiledOrderRow(
        this LinqraftMapper<EfOrder> source
    )
    {
        return source.Select<EfCompiledOrderRow>(order => new
        {
            order.Id,
            order.OrderNumber,
            TotalAmount = order.Items.Sum(item => item.Quantity * item.UnitPrice),
        });
    }

    [LinqraftMapping]
    internal static IQueryable<EfNullableShipmentRow> ProjectToEfNullableShipmentRow(
        this LinqraftMapper<EfOrder> source
    )
    {
        return source.Select<EfNullableShipmentRow>(order => new
        {
            order.OrderNumber,
            CarrierName = order.Shipment?.CarrierName,
            FirstEvent = order.Shipment?.Events.OrderBy(evt => evt.Sequence).FirstOrDefault(),
            TotalFeeAmount = order.Shipment?.Events.Sum(evt => evt.Fees!.Sum(fee => fee.Amount)),
            TotalSurcharge = order
                .Shipment?.Events.Where(evt => evt.Summary != null)
                .Sum(evt => evt.Summary!.Surcharge),
        });
    }
}

public partial class EfSqliteOrderRow;

public partial class EfCompiledOrderRow;

public partial class EfNullableShipmentRow;
