# Generating Reusable Mapping Methods

This guide explains how to generate reusable projection extension methods with Linqraft's mapping API.

## Overview

Inline `SelectExpr(...)` and `UseLinqraft().Select(...)` are still the default way to let Linqraft rewrite a single query. Use mapping generation when you want the projection to become a named extension method that can be reused from multiple callers, including EF Core compiled queries.

## Declaring a Mapping

Create a top-level `static partial` class and add a template method with `[LinqraftMapping]`. The first parameter must be `this LinqraftMapper<TSource>`.

```csharp
namespace YourNamespace;

public static partial class OrderQueries
{
    [LinqraftMapping]
    internal static IQueryable<OrderDto> ProjectToOrderDto(this LinqraftMapper<Order> source) =>
        source.Select<OrderDto>(order => new
        {
            order.Id,
            CustomerName = order.Customer?.Name,
            Items = order.Items.Select(item => new
            {
                item.ProductName,
                item.Quantity,
            }),
        });
}
```

The template method is never called directly. Linqraft analyzes its body and generates reusable overloads like:

```csharp
public static partial class OrderQueries
{
    internal static IQueryable<OrderDto> ProjectToOrderDto(this IQueryable<Order> source) { ... }

    internal static IEnumerable<OrderDto> ProjectToOrderDto(this IEnumerable<Order> source) { ... }
}
```

## Parameters Instead of `capture:`

Mapping methods do not use `capture: () => ...`. If the generated extension method needs additional values, declare them directly on the template method:

```csharp
public static partial class OrderQueries
{
    [LinqraftMapping]
    internal static IQueryable<OrderSummaryDto> ProjectToSummary(
        this LinqraftMapper<Order> source,
        int offset,
        string suffix) =>
        source.Select<OrderSummaryDto>(order => new
        {
            order.Id,
            DisplayName = order.CustomerName + suffix,
            AdjustedTotal = order.Total + offset,
        });
}
```

That produces both:

```csharp
query.ProjectToSummary(offset, suffix);
items.ProjectToSummary(offset, suffix);
```

## Visibility

`Visibility` controls the generated extension method accessibility. The default is `internal`.

```csharp
public static partial class OrderQueries
{
    [LinqraftMapping(Visibility = LinqraftMapper.Public)]
    internal static IQueryable<OrderDto> ProjectToOrderDto(this LinqraftMapper<Order> source) =>
        source.Select<OrderDto>(order => new
        {
            order.Id,
        });
}
```

If the source type or DTO type is not publicly accessible, Linqraft still falls back to `internal`.

## Using Projection Helpers

The mapper surface supports the same projection-helper parameter shape as `UseLinqraft()`:

```csharp
public static partial class OrderQueries
{
    [LinqraftMapping]
    internal static IQueryable<OrderRow> ProjectToOrderRow(this LinqraftMapper<Order> source) =>
        source.Select<OrderRow>((order, helper) => new
        {
            order.Id,
            CustomerName = helper.AsLeftJoin(order.Customer!).Name,
        });
}
```

## Using with EF Core

Generated mapping methods are especially useful in compiled queries:

```csharp
private static readonly Func<MyDbContext, Task<List<OrderSummaryDto>>> GetOrderSummaries =
    EF.CompileAsyncQuery(
        (MyDbContext db) => db.Orders
            .ProjectToSummary(10, " (priority)")
            .ToListAsync()
    );
```

## Requirements

1. The containing type must be a top-level `static partial` class.
2. The template method must be marked with `[LinqraftMapping]`.
3. The first parameter must be `this LinqraftMapper<TSource>`.
4. The body must contain a mapper projection call such as `source.Select<TDto>(...)`.
5. Additional generated method parameters must be declared directly on the template method signature.

## Deprecated v0.10 API

`LinqraftMappingGenerate` and `LinqraftMappingDeclare<T>` were deprecated in v0.10. Use `[LinqraftMapping]` with `LinqraftMapper<TSource>` for new code.

## Further Reading

- [Linqraft Usage Patterns](./usage-patterns.md)
- [Linqraft Performance](./performance.md)
- [EF Core Compiled Queries](https://learn.microsoft.com/en-us/ef/core/performance/advanced-performance-topics#compiled-queries)
