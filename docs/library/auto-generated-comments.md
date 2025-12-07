# Auto-Generated Comments

Linqraft can automatically generate XML documentation comments on generated DTOs by extracting comments from source properties.

## Supported Comment Types

Linqraft extracts comments from:

1. **XML summary comments** (`/// <summary>`)
2. **EF Core Comment attributes** (`[Comment("...")]`)
3. **Display attributes** (`[Display(Name = "...")]`)
4. **Single-line comments** (`// ...`)

## Example

```csharp
// Source entity with comments
public class Order
{
    /// <summary>
    /// Unique identifier for the order
    /// </summary>
    [Key]
    public int Id { get; set; }

    [Comment("Customer who placed the order")]
    public string CustomerName { get; set; }

    [Display(Name = "Total order amount")]
    public decimal TotalAmount { get; set; }

    // Collection of items in this order
    public List<OrderItem> Items { get; set; }
}

public class OrderItem
{
    // Product associated with this item
    public Product Product { get; set; }
}

// Use SelectExpr
query.SelectExpr<Order, OrderDto>(o => new
{
    o.Id,
    o.CustomerName,
    Amount = o.TotalAmount,
    ProductNames = o.Items.Select(i => i.Product?.Name).ToList(),
});
```

## Generated Code with Comments

```csharp
/// <summary>
/// Source entity with comments
/// </summary>
/// <remarks>
/// From: <c>Order</c>
/// </remarks>
public partial class OrderDto
{
    /// <summary>
    /// Unique identifier for the order
    /// </summary>
    /// <remarks>
    /// From: <c>Order.Id</c>
    /// Attributes: <c>[Key]</c>
    /// </remarks>
    public required int Id { get; set; }

    /// <summary>
    /// Customer who placed the order
    /// </summary>
    /// <remarks>
    /// From: <c>Order.CustomerName</c>
    /// </remarks>
    public required string CustomerName { get; set; }

    /// <summary>
    /// Total order amount
    /// </summary>
    /// <remarks>
    /// From: <c>Order.TotalAmount</c>
    /// </remarks>
    public required decimal Amount { get; set; }

    /// <summary>
    /// Product associated with this item
    /// </summary>
    /// <remarks>
    /// From: <c>Order.Items.Select(...).ToList()</c>
    /// </remarks>
    public required List<string> ProductNames { get; set; }
}
```

## Configuration

Control comment generation with the `LinqraftCommentOutput` property:

```xml
<PropertyGroup>
  <!-- All: summary + reference (default) -->
  <LinqraftCommentOutput>All</LinqraftCommentOutput>

  <!-- SummaryOnly: only summary, no reference -->
  <LinqraftCommentOutput>SummaryOnly</LinqraftCommentOutput>

  <!-- None: no comments -->
  <LinqraftCommentOutput>None</LinqraftCommentOutput>
</PropertyGroup>
```
