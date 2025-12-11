# Generating Select Methods Within a Class

This guide explains how to generate reusable Select extension methods within your own classes using Linqraft's `[LinqraftMappingGenerate]` attribute.

## Overview

Linqraft traditionally uses interceptors to generate efficient Select expressions inline. However, in some scenarios you may want to define reusable Select methods within your own classes:

- Creating reusable projection methods that can be called from multiple places
- Using with EF Core's compiled queries (`EF.CompileAsyncQuery`)
- Supporting EF Core's precompiled queries (EF9+) for NativeAOT compatibility
- Organizing complex projections in dedicated query classes

The `[LinqraftMappingGenerate]` attribute generates extension methods in your class instead of using interceptors, enabling these scenarios.

## Basic Usage

Define a static partial class with a template method marked with `[LinqraftMappingGenerate]`:

```csharp
public static partial class OrderQueries
{
    // Template method - never called, only analyzed by Linqraft
    [LinqraftMappingGenerate("ProjectToDto")]
    internal static IQueryable<OrderDto> Template(this IQueryable<Order> source) => source
        .SelectExpr<Order, OrderDto>(o => new
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

## Using with EF Core

### Compiled Queries

Improve performance by compiling queries once and reusing them:

```csharp
public static partial class OrderQueries
{
    [LinqraftMappingGenerate("ProjectToSummary")]
    internal static IQueryable<OrderSummaryDto> Template(
        this IQueryable<Order> source) => source
        .SelectExpr<Order, OrderSummaryDto>(o => new
        {
            o.Id,
            o.OrderNumber,
            TotalAmount = o.Items.Sum(i => i.Quantity * i.UnitPrice),
        });
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

## Requirements

1. **Static Partial Class**: The containing class must be `static` and `partial`
2. **Top-Level Class**: Extension methods must be in a non-nested class
3. **SelectExpr Inside**: The template method must contain at least one `SelectExpr` call

## Further Reading

- [Linqraft Usage Patterns](./usage-patterns.md) - Learn about the three main Linqraft patterns
- [Linqraft Performance](./performance.md) - Performance optimization tips
- [EF Core Compiled Queries](https://learn.microsoft.com/en-us/ef/core/performance/advanced-performance-topics#compiled-queries) - Using with EF Core
- [EF Core Precompiled Queries](https://github.com/dotnet/efcore/issues/25009) - NativeAOT support in EF9
