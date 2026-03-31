using System;
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

public abstract class ReferencedMasterBase<TSelf, TEnum>
    where TSelf : ReferencedMasterBase<TSelf, TEnum>
    where TEnum : struct, Enum
{
    public TEnum Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public int Order { get; set; }

    public string Label => Id.ToString() ?? Name;
}

public enum ReferencedItemCategory
{
    Alpha = 1,
    Beta = 2,
}

public sealed class ReferencedItemType
    : ReferencedMasterBase<ReferencedItemType, ReferencedItemCategory>
{
    public bool IsPrimary { get; set; }
}

public sealed class ReferencedGenericBaseOrder
{
    public int OrderId { get; set; }

    public required ReferencedItemType Item { get; set; }
}
