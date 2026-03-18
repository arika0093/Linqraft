# Usage Patterns

Linqraft provides several usage patterns for different scenarios. This guide explains when and how to use each pattern, including `SelectExpr`-based projections and `LinqraftKit.Generate` for non-query object generation.

## Overview

| Pattern | Return Type | Use Case |
|---------|-------------|----------|
| [Anonymous](#anonymous-pattern) | Anonymous type | Quick queries, one-off projections |
| [Explicit DTO](#explicit-dto-pattern) | Auto-generated DTO | Reusable DTOs, API responses, type-safe code |
| [Pre-existing DTO](#pre-existing-dto-pattern) | Your DTO class | Using existing DTOs, shared types |
| [Aggregation & Flattening](#aggregation--flattening-helpers) | Named or anonymous projections | GroupBy / SelectMany pipelines without manual intermediate `IGrouping` handling |
| [Projection Hooks](./projection-hooks.md) | Rewritten selector fragments | Opt into `AsLeftJoin()` / `AsProjectable()` rewrites inside generated projections |
| [LinqraftKit.Generate](#linqraftkitgenerate) | Auto-generated DTO | Building generated DTOs from runtime objects outside `IEnumerable` / `IQueryable` pipelines |

## Anonymous Pattern

Use `SelectExpr` without generic type parameters to get an anonymous-type projection.

### When to Use
* Quick data exploration
* One-off queries
* When you don't need a named type

### Example

```csharp
var orders = await dbContext.Orders
    .SelectExpr(o => new
    {
        o.Id,
        CustomerName = o.Customer?.Name,
        TotalAmount = o.OrderItems.Sum(oi => oi.Quantity * oi.UnitPrice),
    })
    .ToListAsync();

// Result type: IEnumerable<anonymous type>
```

### Pros & Cons

**Pros:**
* Simple and quick to write
* No type declaration needed
* Good for prototyping

**Cons:**
* Cannot use as return type from methods
* Limited IntelliSense support
* Not suitable for API responses

## Explicit DTO Pattern

Specify the input entity type and output DTO type as generic parameters. Linqraft automatically generates the DTO class based on your selector.

### When to Use
* API endpoints
* Shared data transfer objects
* When you need type-safe code
* When you need to reference the DTO type elsewhere

### Example

```csharp
var orders = await dbContext.Orders
    // Order: input entity type
    // OrderDto: output DTO type (auto-generated)
    .SelectExpr<Order, OrderDto>(o => new
    {
        o.Id,
        CustomerName = o.Customer?.Name,
        CustomerCountry = o.Customer?.Address?.Country?.Name,
        Items = o.OrderItems.Select(oi => new
        {
            ProductName = oi.Product?.Name,
            oi.Quantity
        }),
    })
    .ToListAsync();

// Result type: List<OrderDto>
```

### Generated Code

Linqraft generates a partial class for `OrderDto`:

```csharp
public partial class OrderDto
{
    public required int Id { get; set; }
    public required string? CustomerName { get; set; }
    public required string? CustomerCountry { get; set; }
    public required IEnumerable<ItemsDto> Items { get; set; }
}

// Nested DTOs are also generated automatically
namespace LinqraftGenerated_HASH
{
    public partial class ItemsDto
    {
        public required string? ProductName { get; set; }
        public required int Quantity { get; set; }
    }
}
```

### Working with IEnumerable

You can also use `SelectExpr` with `IEnumerable<T>` for in-memory collections:

```csharp
var orders = myList
    .SelectExpr<Order, OrderDto>(o => new
    {
        o.Id,
        CustomerName = o.Customer?.Name,
    })
    .ToList();

// Works with any IEnumerable<T>, not just IQueryable<T>
```

### Extending Generated DTOs

Since generated DTOs are `partial` classes, you can extend them:

```csharp
// Add custom methods to the generated DTO
public partial class OrderDto
{
    public string GetDisplayName() => $"{Id}: {CustomerName}";

    public decimal GetAverageItemPrice() =>
        Items.Any() ? TotalAmount / Items.Count() : 0;
}
```

### Pros & Cons

**Pros:**
* Type-safe and reusable
* Full IntelliSense support
* Perfect for API responses
* Can extend with partial classes
* Nested DTOs generated automatically

**Cons:**
* Slightly more verbose than anonymous
* DTO names must be unique in the namespace

## Pre-existing DTO Pattern

Use your own pre-defined DTO classes with `SelectExpr`. This pattern doesn't generate DTOs but still provides null-propagation support.

### When to Use
* When you already have DTO classes
* When DTOs are shared across projects
* When you need specific attributes or interfaces on DTOs
* When DTO structure is defined by external requirements (e.g., API contracts)

### Example

```csharp
// Your existing DTO class
public class OrderDto
{
    [JsonPropertyName("order_id")]
    public int Id { get; set; }

    [Required]
    public string CustomerName { get; set; }

    public decimal TotalAmount { get; set; }
}

// Use SelectExpr with your DTO
var orders = await dbContext.Orders
    .SelectExpr(o => new OrderDto
    {
        Id = o.Id,
        CustomerName = o.Customer?.Name, // null-propagation still works
        TotalAmount = o.OrderItems.Sum(oi => oi.Quantity * oi.UnitPrice),
    })
    .ToListAsync();

// Result type: List<OrderDto>
```

### Pros & Cons

**Pros:**
* Full control over DTO structure
* Can add attributes and interfaces
* Can share DTOs across projects
* Still benefits from null-propagation

**Cons:**
* Must manually maintain DTO classes
* Changes to query require updating the DTO

## Aggregation & Flattening Helpers

`GroupByExpr` and `SelectManyExpr` reuse the same DTO-generation pipeline as `SelectExpr`, but let you skip manual intermediate `GroupBy(...).Select(...)` and `SelectMany(...).Select(...)` steps.

### GroupByExpr

Use `GroupByExpr` when the final result is a projection over an `IGrouping<TKey, TSource>`.

```csharp
var result = dbContext.HealthChecks
    .GroupByExpr<HealthCheck, string, HealthSummaryDto>(
        check => check.Region,
        group => new
        {
            Region = group.Key,
            AllHealthy = group.All(x => x.Status == "Healthy"),
            Checks = group.Select(x => new
            {
                x.Id,
                x.Status,
            }),
        })
    .ToListAsync();
```

### SelectManyExpr

Use `SelectManyExpr` when the end result is a flattened projection.

```csharp
var rows = dbContext.Orders
    .SelectManyExpr<Order, OrderItemRow>(order =>
        order.Items.Select(item => new
        {
            OrderId = order.Id,
            item.ProductName,
            item.Quantity,
        }))
    .ToListAsync();
```

### LinqraftKit.Generate

Use `LinqraftKit.Generate<T>(...)` when you want Linqraft to generate and populate a DTO from a runtime object instead of from an `IEnumerable` / `IQueryable` projection.

This is useful when you want Linqraft's DTO generation rules without going through a query provider. Nested anonymous objects, arrays, lists, and values returned from `SelectExpr` can all be combined into one generated DTO.

```csharp
var dto = LinqraftKit.Generate<OrderBundleDto>(
    new
    {
        Id = 42,
        Customer = new { Name = "Ada" },
        ItemNames = new[] { "Keyboard", "Mouse" },
    }
);
```

This pattern works well for:
* 1:1 DTO generation from anonymous objects
* Combining multiple projection results into one response DTO
* Reusing Linqraft's nested DTO naming and collection handling outside query pipelines

When the initializer depends on local values, prefer the delegate-based `capture:` overload so the generated code stays compatible with NativeAOT:

```csharp
var id = 42;
var prefix = "Order-";

var dto = LinqraftKit.Generate<OrderLabelDto>(
    new
    {
        Id = id,
        Label = prefix + id,
    },
    capture: () => (id, prefix)
);
```

The delegate return value is unpacked by generated code without runtime reflection, so it is the recommended capture form for new code. Anonymous-object capture remains available for older call sites when you need source compatibility, but it is obsolete and the analyzer can rewrite it to the delegate form.
