using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Linqraft.Tests.EFCore;

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

[LinqraftMappingGenerate("ProjectToEfNullableShipmentRow")]
internal sealed class EfNullableShipmentMappingDeclare : LinqraftMappingDeclare<EfOrder>
{
    protected override void DefineMapping()
    {
        Source.SelectExpr<EfOrder, EfNullableShipmentRow>(order => new
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
