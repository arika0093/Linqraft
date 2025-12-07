# Nested SelectExpr (Beta)

You can use `SelectExpr` inside another `SelectExpr` to explicitly control the DTO class generation for nested collections.  
This allows you to create reusable DTOs for nested entities instead of auto-generated DTOs in hash namespaces.

## Important Notes

### Beta Feature Warning

This feature is currently in **beta** (available since v0.6.2). While it works correctly, the API and behavior may change in future versions. Please report any issues on GitHub.

### .NET 9+ Recommended

This feature is **recommended for .NET 9 or later**.
In older .NET versions, type inference may fail for unknown reasons.
See [GitHub Issue #211](https://github.com/your-org/linqraft/issues/211) for details.

### Empty Partial Class Declarations Required

To ensure DTOs are generated in the correct location, you **must** declare empty partial class definitions for all explicit DTO types:

```csharp
public class MyService
{
    public void GetOrders(IQueryable<Order> query)
    {
        var result = query
            .SelectExpr<Order, OrderDto>(o => new
            {
                o.Id,
                Items = o.OrderItems.SelectExpr<OrderItem, OrderItemDto>(i => new
                {
                    i.ProductName,
                }),
            });
    }

    // Empty partial class definitions - REQUIRED
    internal partial class OrderDto;
    internal partial class OrderItemDto;
}
```

**Why is this necessary?**

The source generator determines where to generate DTOs based on where the empty partial class is declared. Without these declarations:
- The generator might place DTOs in the wrong namespace
- DTO generation might fail
- The generated code might not compile

## Basic Usage

```csharp
var result = query
    .SelectExpr<Order, OrderDto>(o => new
    {
        o.Id,
        o.CustomerName,
        // Using SelectExpr - ItemDto is generated in your namespace
        Items = o.OrderItems.SelectExpr<OrderItem, OrderItemDto>(i => new
        {
            i.ProductName,
            i.Quantity,
        }),
    });

// Required partial class declarations
internal partial class OrderDto;
internal partial class OrderItemDto;
```

**Generated DTOs:**
```csharp
namespace MyProject
{
    public partial class OrderDto
    {
        public required int Id { get; set; }
        public required string CustomerName { get; set; }
        public required IEnumerable<OrderItemDto> Items { get; set; }
    }

    // This DTO is directly accessible and reusable
    public partial class OrderItemDto
    {
        public required string ProductName { get; set; }
        public required int Quantity { get; set; }
    }
}
```

## Multiple Nesting Levels

You can nest `SelectExpr` calls multiple levels deep:

```csharp
var result = query
    .SelectExpr<Entity, EntityDto>(x => new
    {
        x.Id,
        x.Name,
        Items = x.Items.SelectExpr<Item, ItemDto>(i => new
        {
            i.Id,
            i.Title,
            SubItems = i.SubItems.SelectExpr<SubItem, SubItemDto>(si => new
            {
                si.Id,
                si.Value,
            }),
        }),
    })
    .ToList();

// Declare all DTO types
internal partial class EntityDto;
internal partial class ItemDto;
internal partial class SubItemDto;
```

## Mixing Select and SelectExpr

You can mix regular `Select` and `SelectExpr` within the same query:

```csharp
var result = query
    .SelectExpr<Entity, EntityDto>(x => new
    {
        x.Id,
        // Reusable DTO - generated in your namespace
        Items = x.Items.SelectExpr<Item, ItemDto>(i => new
        {
            i.Id,
            // Auto-generated DTO in hash namespace
            SubItems = i.SubItems.Select(si => new { si.Value }),
        }),
    });

internal partial class EntityDto;
internal partial class ItemDto;
// No need to declare SubItemDto - it's auto-generated
```

## When to Use Nested SelectExpr

**Use nested `SelectExpr` when:**
* You need to reuse nested DTOs across multiple queries
* You want full control over nested DTO naming
* You need to extend nested DTOs with partial classes
* You want to reference nested DTOs in your API documentation

**Use regular `Select` when:**
* The nested DTO is used only once
* You don't need to reference the nested DTO type
* You prefer simpler, less verbose code

## Comparison

| Feature | Regular Select | Nested SelectExpr |
|---------|---------------|-------------------|
| DTO Location | `LinqraftGenerated_HASH` namespace | Your namespace |
| Reusability | No | Yes |
| Declaration Required | No | Yes (empty partial class) |
| .NET Version | Any | .NET 9+ recommended |

## See Also

* [Usage Patterns](usage-patterns.md) - Overview of all SelectExpr patterns
* [Nested DTO Naming](nested-dto-naming.md) - Configure nested DTO naming strategy
* [Partial Classes](partial-classes.md) - Extend generated DTOs
