using System;
using System.Collections.Generic;

namespace Linqraft.Tests.EFCore;

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
