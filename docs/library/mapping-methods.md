# Generating Select Methods Within a Class

This guide explains how to generate reusable Select extension methods using Linqraft's mapping generation features.

## Overview

Linqraft traditionally uses interceptors to generate efficient Select expressions inline. However, in some scenarios you may want to define reusable Select methods:

- Creating reusable projection methods that can be called from multiple places
- Using with EF Core's compiled queries (`EF.CompileAsyncQuery`)
- Supporting EF Core's precompiled queries (EF9+) for NativeAOT compatibility
- Organizing complex projections in dedicated query classes

Linqraft provides two approaches for generating extension methods instead of using interceptors:

1. **Helper Class Approach (Recommended)**: Inherit from `LinqraftMappingDeclare<T>` for simpler, more concise code
2. **Static Partial Class Approach**: Use `[LinqraftMappingGenerate]` attribute on methods in static partial classes for more control

## Approach 1: Helper Class (Recommended)

The simplest way to define mapping methods is to inherit from `LinqraftMappingDeclare<T>` and add the `[LinqraftMappingGenerate]` attribute:

```csharp
namespace YourNamespace;

[LinqraftMappingGenerate]
internal class OrderMappingDeclare : LinqraftMappingDeclare<Order>
{
    protected override void DefineMapping()
    {
        Source.UseLinqraft().Select<OrderDto>(o => new
        {
            o.Id,
            CustomerName = o.Customer?.Name,  // ?.operator is converted
            Items = o.Items.Select(i => new
            {
                i.ProductName,
                i.Quantity,
            }),
        });
    }
}
```

Linqraft generates a static extension method class with a hash suffix to avoid name collisions:

```csharp
// Generated code (simplified):
namespace YourNamespace
{
    internal static partial class OrderMappingDeclare_A1B2C3D4
    {
        // Default method name: ProjectTo{EntityName}
        internal static IQueryable<OrderDto> ProjectToOrder(
            this IQueryable<Order> source)
        {
            return source.Select(o => new OrderDto
            {
                Id = o.Id,
                CustomerName = o.Customer != null ? o.Customer.Name : null,
                Items = o.Items.Select(i => new OrderItemDto
                {
                    ProductName = i.ProductName,
                    Quantity = i.Quantity,
                }),
            });
        }
    }
}
```

You can then call the generated method from anywhere:

```csharp
var orders = await dbContext.Orders
    .ProjectToOrder()
    .ToListAsync();
```

### Custom Method Name

To customize the generated method name, use the `[LinqraftMappingGenerate]` attribute at the class level:

```csharp
[LinqraftMappingGenerate("CustomProjection")]
internal class OrderMappingDeclare : LinqraftMappingDeclare<Order>
{
    protected override void DefineMapping()
    {
        Source.UseLinqraft().Select<OrderDto>(o => new { o.Id, o.CustomerName });
    }
}

// Usage:
var orders = await dbContext.Orders.CustomProjection().ToListAsync();
```

### Capture Parameters

Generated mapping methods can also expose captured local values as additional parameters. Use the delegate-based `capture:` pattern inside `DefineMapping()` and Linqraft will append the captured values to the generated method signature in a NativeAOT-safe form:

```csharp
[LinqraftMappingGenerate("ProjectToSummaryWithOffset")]
internal sealed class OrderSummaryMapping : LinqraftMappingDeclare<Order>
{
    private int offset = default;
    private string suffix = string.Empty;

    protected override void DefineMapping()
    {
        Source.UseLinqraft().Select<OrderSummaryDto>(
            order => new
            {
                order.Id,
                DisplayName = order.CustomerName + suffix,
                AdjustedTotal = order.Total + offset,
            },
            capture: () => (offset, suffix)
        );
    }
}

// Usage:
var orders = await dbContext.Orders
    .ProjectToSummaryWithOffset(10, " (priority)")
    .ToListAsync();
```

The generated method receives `offset` and `suffix` as normal parameters, and Linqraft unpacks them in generated code without relying on runtime reflection.

### Controlling Generated Visibility

`LinqraftMappingDeclare<T>` itself is emitted as an internal base type, so declaration classes that inherit from it also stay internal. If you need the generated extension method to be public, set the attribute's `Visibility` named argument explicitly:

```csharp
[LinqraftMappingGenerate(
    "ProjectToSummary",
    Visibility = LinqraftMappingVisibility.Public)]
internal sealed class OrderSummaryMapping : LinqraftMappingDeclare<Order>
{
    protected override void DefineMapping()
    {
        Source.UseLinqraft().Select<OrderSummaryDto>(order => new
        {
            order.Id,
            order.OrderNumber,
        });
    }
}
```

For helper-class mappings, the generated static helper class and extension method both use that visibility. When omitted, helper-class mappings remain `internal`.

## Approach 2: Static Partial Class

For more control over the containing class, define a static partial class with a template method marked with `[LinqraftMappingGenerate]`:

```csharp
public static partial class OrderQueries
{
    // Template method - never called, only analyzed by Linqraft
    [LinqraftMappingGenerate("ProjectToDto")]
    internal static IQueryable<OrderDto> Template(this IQueryable<Order> source) => source
        .UseLinqraft()
        .Select<OrderDto>(o => new
        {
            o.Id,
            CustomerName = o.Customer?.Name,  // ?.operator is converted
            Items = o.Items.Select(i => new
            {
                i.ProductName,
                i.Quantity,
            }),
        });
}
```

Linqraft generates the `ProjectToDto` extension method in your class:

```csharp
// Generated code (simplified):
public static partial class OrderQueries
{
    internal static IQueryable<OrderDto> ProjectToDto(
        this IQueryable<Order> source)
    {
        return source.Select(o => new OrderDto
        {
            Id = o.Id,
            CustomerName = o.Customer != null ? o.Customer.Name : null,
            Items = o.Items.Select(i => new OrderItemDto
            {
                ProductName = i.ProductName,
                Quantity = i.Quantity,
            }),
        });
    }
}
```

You can then call the generated method from anywhere in your code:

```csharp
// Call the generated method from anywhere
var orders = await dbContext.Orders
    .ProjectToDto()
    .ToListAsync();
```

Static-partial mappings also support the same `Visibility` named argument:

```csharp
public static partial class OrderQueries
{
    [LinqraftMappingGenerate(
        "ProjectToDto",
        Visibility = LinqraftMappingVisibility.Public)]
    internal static IQueryable<OrderDto> Template(this IQueryable<Order> source) => source
        .UseLinqraft()
        .Select<OrderDto>(order => new
        {
            order.Id,
        });
}
```

For this approach, `Visibility` changes the generated method accessibility only. The containing static partial class still controls the maximum effective visibility, so make the class `public` if you want a public extension method.

### Capture Parameters

Static partial mappings support the same capture flow. Any values passed through `capture: () => ...` are surfaced as additional parameters on the generated method:

```csharp
public static partial class OrderQueries
{
    [LinqraftMappingGenerate("ProjectToDtoWithCapture")]
    internal static IQueryable<OrderSummaryDto> Template(
        this IQueryable<Order> source,
        int offset,
        string suffix) => source
        .UseLinqraft()
        .Select<OrderSummaryDto>(
            order => new
            {
                order.Id,
                DisplayName = order.CustomerName + suffix,
                AdjustedTotal = order.Total + offset,
            },
            capture: () => (offset, suffix)
        );
}

// Usage:
var orders = await dbContext.Orders
    .ProjectToDtoWithCapture(10, " (priority)")
    .ToListAsync();
```

For new code, prefer the delegate capture form shown above. Legacy anonymous-object captures still compile, but they are obsolete and the analyzer can migrate them automatically.

## Using with EF Core

### Compiled Queries

Both approaches work seamlessly with EF Core's compiled queries. Here's an example using the helper class:

```csharp
// Define the mapping using helper class
[LinqraftMappingGenerate("ProjectToSummary")]
internal class OrderSummaryMapping : LinqraftMappingDeclare<Order>
{
    protected override void DefineMapping()
    {
        Source.UseLinqraft().Select<OrderSummaryDto>(o => new
        {
            o.Id,
            o.OrderNumber,
            TotalAmount = o.Items.Sum(i => i.Quantity * i.UnitPrice),
        });
    }
}

public class OrderService
{
    // Compile the query once
    private static readonly Func<MyDbContext, Task<List<OrderSummaryDto>>> 
        GetOrderSummaries = EF.CompileAsyncQuery(
            (MyDbContext db) => db.Orders
                .ProjectToSummary()
                .ToListAsync()
        );

    public async Task<List<OrderSummaryDto>> GetSummariesAsync(MyDbContext db)
    {
        // Reuse the compiled query
        return await GetOrderSummaries(db);
    }
}
```

The same works with the static partial class approach - simply use the generated method name in `EF.CompileAsyncQuery`.

## Comparison of Approaches

| Feature | Helper Class (Recommended) | Static Partial Class |
|---------|---------------------------|----------------------|
| **Simplicity** | ✅ Very simple | ⚠️ More verbose boilerplate |
| **Verbosity** | ✅ Minimal code | ⚠️ More code required |
| **Class Control** | ⚠️ A separate class is generated | ✅ Contained within the same class |
| **Multiple Mappings** | ⚠️ One per class | ✅ Multiple per class |
| **Use Cases** | Single projection per entity | Multiple projections in one class |

**When to use Helper Class:**
- You want the simplest, most concise code
- You have one projection per entity type
- You don't need to control the generated class name

**When to use Static Partial Class:**
- You need multiple projections for the same entity in one class
- You need full control over the generated class name
- You're migrating existing code

## Requirements

### Helper Class Requirements:
1. **Inherit from `LinqraftMappingDeclare<T>`**: Your class must inherit from the base helper class
2. **Add `[LinqraftMappingGenerate]` attribute**: The class must be marked with this attribute (with or without a custom method name)
3. **Optional `Visibility` override**: Set `Visibility = LinqraftMappingVisibility.Public` when the generated extension should be public
4. **Override `DefineMapping()`**: Implement the abstract method with your mapping logic
5. **Use `Source` property**: Use the `Source` property to access the queryable
6. **Projection Call Inside**: The `DefineMapping()` method must contain a Linqraft projection call such as `UseLinqraft().Select<TDto>(...)` (direct entry points still work when you need them)
7. **Capture Pattern**: If you reference outer values, pass them via `capture: () => ...` so Linqraft can generate matching method parameters safely

### Static Partial Class Requirements:
1. **Static Partial Class**: The containing class must be `static` and `partial`
2. **[LinqraftMappingGenerate] attribute**: Methods must be marked with this attribute and specify the desired method name
3. **Optional `Visibility` override**: Use `Visibility = LinqraftMappingVisibility.Public` or `.Internal` to override the generated method accessibility
4. **Top-Level Class**: Extension methods must be in a non-nested class
5. **Projection Call Inside**: The template method must contain at least one Linqraft projection call such as `UseLinqraft().Select<TDto>(...)`
6. **Capture Pattern**: If the selector depends on local values, use `capture: () => ...` so the generated method exposes those values explicitly

## Further Reading

- [Linqraft Usage Patterns](./usage-patterns.md) - Learn about the three main Linqraft patterns
- [Linqraft Performance](./performance.md) - Performance optimization tips
- [EF Core Compiled Queries](https://learn.microsoft.com/en-us/ef/core/performance/advanced-performance-topics#compiled-queries) - Using with EF Core
- [EF Core Precompiled Queries](https://github.com/dotnet/efcore/issues/25009) - NativeAOT support in EF9
