using System.Collections.Generic;
using System.Linq;

namespace Linqraft.Tests.ProjectReferenceDependency;

public static class ReferencedOrderQueries
{
    public static IQueryable<ReferencedOrderFromDependencyDto> ProjectFromDependency(
        this IQueryable<ReferencedOrder> orders
    )
    {
        return orders.SelectExpr<ReferencedOrder, ReferencedOrderFromDependencyDto>(order => new
        {
            order.Id,
            CustomerName = order.Customer.Name,
            LineCount = order.Items.Count,
        });
    }
}

public sealed class ReferencedOrder
{
    public int Id { get; set; }

    public required ReferencedCustomer Customer { get; set; }

    public required List<ReferencedOrderItem> Items { get; set; }
}

public sealed class ReferencedCustomer
{
    public required string Name { get; set; }
}

public sealed class ReferencedOrderItem
{
    public int Quantity { get; set; }
}
