---
description: >-
  Refactors existing LINQ Select projections and hand-written DTOs to use
  Linqraft's UseLinqraft().Select<TDto>(...) pattern with on-demand DTO
  generation and null-propagation support.
input:
  - file_path
---

# Refactor Existing Projections to Linqraft

You are refactoring C# LINQ projection code to use the Linqraft source generator.
Linqraft auto-generates DTO classes from anonymous-object selector bodies and
translates `?.` null-propagation into expression-tree-safe ternary guards at
compile time.

## Context

- Target file: ${input:file_path}
- Linqraft NuGet package is already installed (or will be added by the caller).
- Refer to `.apm/SKILL.md` for full Linqraft API surface and constraints.

## Step 1 — Identify Refactoring Candidates

Scan the target file (and related files if needed) for LINQ projections that
match any of these patterns:

### Pattern A: Anonymous Select without UseLinqraft

```csharp
// BEFORE
var result = query.Select(x => new { x.Id, x.Name }).ToList();
```

### Pattern B: Select into hand-written DTO

```csharp
// BEFORE
var result = query.Select(x => new OrderDto { Id = x.Id, Name = x.Customer.Name }).ToList();
```

### Pattern C: Manual null-check ternaries in Select

```csharp
// BEFORE
var result = query.Select(x => new {
    CustomerName = x.Customer != null ? x.Customer.Name : null,
    City = x.Customer != null && x.Customer.Address != null ? x.Customer.Address.City : null,
}).ToList();
```

### Pattern D: Nested Select projections

```csharp
// BEFORE
var result = query.Select(x => new {
    x.Id,
    Items = x.OrderItems.Select(oi => new { oi.ProductName, oi.Quantity }),
}).ToList();
```

## Step 2 — Determine the Target Pattern

For each candidate, decide which Linqraft pattern to apply:

| Scenario | Target Pattern |
|----------|---------------|
| One-off query, result not returned from method | **Anonymous**: `.UseLinqraft().Select(x => new { ... })` |
| DTO used as API response, method return type, or shared | **Explicit DTO**: `.UseLinqraft().Select<TDto>(x => new { ... })` |
| DTO already exists with attributes/interfaces the code depends on | **Pre-existing DTO**: `.UseLinqraft().Select(x => new ExistingDto { ... })` |
| Projection reused across multiple callers | **Mapping method**: `[LinqraftMapping]` with `LinqraftMapper<T>` |

Default to the **Explicit DTO** pattern unless there is a clear reason to choose
another.

## Step 3 — Apply the Transformation

### 3a. Convert the Select call

```csharp
// BEFORE
var orders = await dbContext.Orders
    .Select(o => new OrderDto
    {
        Id = o.Id,
        CustomerName = o.Customer != null ? o.Customer.Name : null,
        Items = o.OrderItems.Select(oi => new OrderItemDto
        {
            ProductName = oi.Product != null ? oi.Product.Name : null,
            Quantity = oi.Quantity,
        }),
    })
    .ToListAsync();

// AFTER
var orders = await dbContext.Orders
    .UseLinqraft()
    .Select<OrderDto>(o => new
    {
        o.Id,
        CustomerName = o.Customer?.Name,
        Items = o.OrderItems.Select(oi => new
        {
            ProductName = oi.Product?.Name,
            oi.Quantity,
        }),
    })
    .ToListAsync();
```

### Key transformation rules

1. **Add `.UseLinqraft()` before `.Select`** (or `.SelectMany` / `.GroupBy`).
2. **Replace `new ExistingDto { ... }` with `new { ... }`** and move the DTO
   name to the generic parameter: `.Select<OrderDto>(...)`.
   - For nested anonymous objects, just use `new { ... }` — Linqraft generates
     nested DTOs automatically.
3. **Replace manual null-check ternaries with `?.`** :
   - `x.Nav != null ? x.Nav.Prop : null` → `x.Nav?.Prop`
   - Deep chains: `x.A != null && x.A.B != null ? x.A.B.C : null` → `x.A?.B?.C`
4. **Simplify member access** — use implicit member names when possible:
   - `Id = o.Id` → `o.Id`
   - `Quantity = oi.Quantity` → `oi.Quantity`
5. **Handle local variables** — if the selector references local variables,
   add a `capture:` parameter:
   ```csharp
   .Select<TDto>(
       x => new { ... },
       capture: () => (localVar1, localVar2)
   )
   ```

### 3b. Remove or simplify the hand-written DTO

If the DTO was hand-written solely for this projection:

- **Delete the DTO class** — Linqraft will generate it as a `partial` class.
- If the DTO has custom methods, attributes, or interfaces, keep a `partial`
  declaration with only those additions:
  ```csharp
  // Keep only custom extensions
  public partial class OrderDto : IValidatableObject
  {
      public IEnumerable<ValidationResult> Validate(ValidationContext ctx) { ... }
  }
  ```

If the DTO is shared across multiple files or projects:

- Use the **Pre-existing DTO** pattern instead (keep the class, use
  `UseLinqraft().Select(o => new OrderDto { ... })`).

### 3c. Apply projection helpers where beneficial

| Before | After |
|--------|-------|
| Navigation access that should be LEFT JOIN | `helper.AsLeftJoin(o.Nav).Prop` |
| Navigation that must not be null (INNER JOIN) | `helper.AsInnerJoin(o.Nav).Prop` |
| Computed property/method that should be server-side | `helper.AsInline(o.ComputedProp)` |
| Nested entity → flat DTO of scalar members | `helper.AsProjection<TDto>(o.Nav)` |
| Shaped nested projection | `helper.Project(o.Nav).Select(n => new { ... })` |

To use helpers, change the selector to accept a second parameter:

```csharp
.Select<OrderRow>((o, helper) => new
{
    CustomerName = helper.AsLeftJoin(o.Customer).Name,
    FirstBigItem = helper.AsInline(o.FirstLargeItemProductName),
})
```

## Step 4 — Verify

1. **Build the project** (`dotnet build`). Linqraft runs at compile time; build
   errors surface immediately.
2. **Check generated DTOs** — press F12 on the DTO name in the IDE, or enable
   `EmitCompilerGeneratedFiles` to inspect on disk.
3. **Run existing tests** to confirm behavior is unchanged.
4. **Review nullability** — Linqraft auto-removes nullability from collection
   properties when safe (configurable via `LinqraftArrayNullabilityRemoval`).
   Verify that downstream code does not rely on nullable collections.

## Rules

- Do NOT introduce `UseLinqraft()` on projections that are already plain
  LINQ-to-Objects after materialization (post-`.ToList()`).
- Do NOT delete DTOs that are referenced outside the projection scope (API
  contracts, serialization, other assemblies).
- Do NOT use `SelectExpr<TSource, TDto>(...)` for new code — prefer
  `UseLinqraft().Select<TDto>(...)`.
- PRESERVE all `Where`, `OrderBy`, `Take`, `Skip`, and other query operators
  — only transform the projection (Select/SelectMany/GroupBy).
- When the selector references local variables, ALWAYS add `capture: () => (...)`
  with the delegate form, not the obsolete anonymous-object form.
