using System.Collections.Generic;
using System.Linq;

namespace Linqraft.Tests;

public sealed class HelperProjectionOrder
{
    public int Id { get; set; }

    public HelperProjectionCustomer? Customer { get; set; }

    public List<HelperProjectionItem> Items { get; set; } = [];

    public string? FirstLargeItemName => this
        .Items.Where(item => item.Quantity >= 2)
        .OrderBy(item => item.Name)
        .Select(item => item.Name)
        .FirstOrDefault();
}

public sealed class HelperProjectionCustomer
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Tier { get; set; } = string.Empty;
}

public sealed class HelperProjectionItem
{
    public string Name { get; set; } = string.Empty;

    public int Quantity { get; set; }
}
