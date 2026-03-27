---
name: linqraft
description: >-
  Source-generated LINQ projections with on-demand DTO generation, null-propagation
  support, and projection helpers for IQueryable and IEnumerable pipelines.
  Reference this skill when generating DTOs or when UseLinqraft() appears in code.
---

# Linqraft

Linqraft is a Roslyn Source Generator that rewrites LINQ `Select`, `SelectMany`, and `GroupBy` projections at compile time. It auto-generates DTO classes from anonymous-object selector bodies and translates C# null-propagation operators (`?.`) into expression-tree-safe ternary guards — features that standard LINQ expression trees cannot represent.

> **Zero runtime dependency.** The `Linqraft` NuGet package ships only analyzers and source generators; nothing is added to the published application.

## When to Use Linqraft

Use Linqraft when the code:

- Projects entities into DTOs via `IQueryable<T>` or `IEnumerable<T>` pipelines.
- Needs on-demand DTO generation — the shape of the DTO is defined by the selector, not a separate class file.
- Uses null-conditional navigation (`?.`) inside projections (unsupported by raw expression trees).
- Requires reusable mapping methods for EF Core compiled queries or shared projection logic.
- Needs `GroupBy` or `SelectMany` projections with auto-generated result types.
- Wants to inline computed properties/methods into server-side queries via `helper.AsInline(...)`.

## When NOT to Use Linqraft

- **Simple materialized queries** — If the query already calls `.ToList()` before projecting, standard LINQ-to-Objects `Select` is sufficient.
- **Hand-maintained DTO contracts** — When DTOs must carry custom attributes (`[JsonPropertyName]`, `[Required]`, etc.) or implement interfaces, use the Pre-existing DTO pattern (`UseLinqraft().Select(o => new ExistingDto { ... })`) or maintain DTOs manually.
- **Non-LINQ mapping** — For object-to-object mapping outside LINQ pipelines (e.g., AutoMapper-style), use `LinqraftKit.Generate<T>(...)` only if the source is an anonymous object; otherwise, a dedicated mapper is more appropriate.
- **Projections that don't benefit from null-propagation** — If none of the selected members traverse nullable navigations, the value Linqraft adds is minimal.

## How to Write Projections

### Entry Point

Always start with `.UseLinqraft()` on an `IQueryable<T>` or `IEnumerable<T>` source, then chain a projection method.

```csharp
var result = source
    .UseLinqraft()
    .Select<OrderDto>(o => new
    {
        o.Id,
        CustomerName = o.Customer?.Name,
    })
    .ToListAsync();
```

### Pattern 1 — Explicit DTO (recommended for API responses)

```csharp
var orders = await dbContext.Orders
    .UseLinqraft()
    .Select<OrderDto>(o => new
    {
        o.Id,
        CustomerName = o.Customer?.Name,
        CustomerCountry = o.Customer?.Address?.Country?.Name,
        Items = o.OrderItems.Select(oi => new
        {
            ProductName = oi.Product?.Name,
            oi.Quantity,
        }),
    })
    .ToListAsync();
```

Linqraft generates `OrderDto` (and a nested `ItemsDto`) as partial classes. The top-level DTO lives in the same namespace as the calling code; nested DTOs are placed in a `LinqraftGenerated_{Hash}` sub-namespace.

### Pattern 2 — Anonymous projection

```csharp
var orders = await dbContext.Orders
    .UseLinqraft()
    .Select(o => new
    {
        o.Id,
        CustomerName = o.Customer?.Name,
    })
    .ToListAsync();
```

Use this for one-off exploration or prototyping. No DTO class is generated.

### Pattern 3 — Pre-existing DTO

```csharp
var orders = await dbContext.Orders
    .UseLinqraft()
    .Select(o => new OrderDto
    {
        Id = o.Id,
        CustomerName = o.Customer?.Name ?? string.Empty,
    })
    .ToListAsync();
```

Linqraft still rewrites null-propagation, but the DTO class is yours to own.

### GroupBy

```csharp
var result = dbContext.HealthChecks
    .UseLinqraft()
    .GroupBy<string, HealthSummaryDto>(
        check => check.Region,
        group => new
        {
            Region = group.Key,
            AllHealthy = group.All(x => x.Status == "Healthy"),
        })
    .ToListAsync();
```

### SelectMany

```csharp
var rows = dbContext.Orders
    .UseLinqraft()
    .SelectMany<OrderItemRow>(order =>
        order.Items.Select(item => new
        {
            OrderId = order.Id,
            item.ProductName,
            item.Quantity,
        }))
    .ToListAsync();
```

### LinqraftKit.Generate (runtime DTO from anonymous objects)

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

## Projection Helpers

When the selector takes a second `helper` parameter, projection helpers are available:

```csharp
.Select<OrderRow>((o, helper) => new
{
    CustomerName  = helper.AsLeftJoin(o.Customer).Name,      // LEFT JOIN null guard
    RequiredName  = helper.AsInnerJoin(o.Customer).Name,     // INNER JOIN filter
    Customer      = helper.AsProjection<CustomerDto>(o.Customer), // nested DTO
    CustomerInfo  = helper.Project(o.Customer).Select(c => new { c.Id, c.Name }), // shaped nested projection
    FirstBigItem  = helper.AsInline(o.FirstLargeItemProductName), // inline computed property
})
```

| Helper | Purpose |
|--------|---------|
| `AsLeftJoin(nav)` | Emit explicit null guard (LEFT JOIN style) |
| `AsInnerJoin(nav)` | Filter out null rows before projecting |
| `AsProjection<TDto>(nav)` | Generate a DTO from scalar/enum/string/value-type members of the navigation |
| `Project(nav).Select(...)` | Shape a nested projection with a sub-selector |
| `AsInline(computedValue)` | Inline a computed property/method body into the generated query |

## Local Variable Capture

Linqraft selectors are rewritten into separate generated methods, so local variables must be captured explicitly:

```csharp
var threshold = 100;
var suffix = " units";

var result = dbContext.Entities
    .UseLinqraft()
    .Select<EntityDto>(
        x => new
        {
            x.Id,
            IsExpensive = x.Price > threshold,
            Label = x.Name + suffix,
        },
        capture: () => (threshold, suffix)
    );
```

Use the delegate `capture: () => (...)` form. Anonymous-object capture (`capture: new { ... }`) is obsolete.

## Reusable Mapping Methods

For shared projections or EF Core compiled queries, declare a `[LinqraftMapping]` template:

```csharp
public static partial class OrderQueries
{
    [LinqraftMapping]
    internal static IQueryable<OrderDto> ProjectToOrderDto(this LinqraftMapper<Order> source) =>
        source.Select<OrderDto>(o => new
        {
            o.Id,
            CustomerName = o.Customer?.Name,
        });
}
```

Linqraft generates `IQueryable<Order>` and `IEnumerable<Order>` extension methods automatically.

## Extending Generated DTOs

All generated DTOs are `partial` classes. Add methods, interfaces, or attributes freely:

```csharp
public partial class OrderDto
{
    public string DisplayName => $"Order #{Id} — {CustomerName}";
}

public partial class OrderDto : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext ctx) { /* ... */ }
}
```

Pre-declare a property when you need to control its accessibility:

```csharp
public partial class OrderDto
{
    internal string InternalNote { get; set; } = "";
}
```

Linqraft will populate the pre-declared member instead of generating a new one.

## Inspecting Generated Code

### IDE: Go to Definition (F12)

Place the cursor on a generated DTO name and press F12.

### File output

Add to `.csproj`:

```xml
<PropertyGroup>
  <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
  <CompilerGeneratedFilesOutputPath>Generated</CompilerGeneratedFilesOutputPath>
</PropertyGroup>
```

Generated files appear under `Generated/Linqraft.SourceGenerator/`.

## MSBuild Customization

| Property | Default | Description |
|----------|---------|-------------|
| `LinqraftRecordGenerate` | `false` | Generate `record` instead of `class` |
| `LinqraftPropertyAccessor` | `Default` | `GetAndSet` / `GetAndInit` / `GetAndInternalSet` |
| `LinqraftHasRequired` | `true` | Emit `required` keyword on properties |
| `LinqraftArrayNullabilityRemoval` | `true` | Replace nullable collections with non-nullable + empty fallback |
| `LinqraftNestedDtoUseHashNamespace` | `true` | Use `LinqraftGenerated_{Hash}` namespace for nested DTOs |
| `LinqraftCommentOutput` | `All` | `All` / `SummaryOnly` / `None` |
| `LinqraftGlobalUsing` | `true` | Emit `global using Linqraft;` |

## Quick Reference

```
dotnet add package Linqraft          # install (dev-only dependency)
```

Minimum requirements: C# 12.0, .NET SDK 8.0.400+, Visual Studio 2022 17.11+.

## Further Reading

- `docs/library/usage-patterns.md` — full pattern catalog
- `docs/library/projection-helpers.md` — helper details and constraints
- `docs/library/mapping-methods.md` — reusable mapping API
- `docs/library/local-variable-capture.md` — capture semantics
- `docs/library/global-properties.md` — all MSBuild properties
- `docs/analyzers/README.md` — analyzer rule list and code-fix catalog
- `examples/Linqraft.ApiSample` — API integration example
- `examples/Linqraft.Sample` — basic usage examples
