---
description: Refactors existing LINQ Select projections and hand-written DTOs to Linqraft UseLinqraft().Select<TDto>(...) with on-demand DTO generation
input:
  - file_path
---

# Refactor Existing Projections to Linqraft

Refactor C# LINQ projection code in ${input:file_path} to use the Linqraft
source generator. Linqraft auto-generates DTO classes from anonymous-object
selector bodies and rewrites `?.` into expression-tree-safe null guards at
compile time.

## Context

- Refer to the `linqraft` skill (`.apm/skills/linqraft/SKILL.md`) for API
  surface and usage guidelines.
- The `Linqraft` NuGet package must be installed in the target project. If it
  is not, run `dotnet add package Linqraft` first.

## Task

### 1. Identify refactoring candidates

Scan the target file for LINQ projections that match any of these patterns:

| Pattern | Example |
|---------|---------|
| Select into hand-written DTO | `.Select(x => new OrderDto { Id = x.Id, Name = x.Customer.Name })` |
| Manual null-check ternaries | `x.Customer != null ? x.Customer.Name : null` |
| Nested Select with hand-written nested DTO | `.Select(x => new { Items = x.Items.Select(i => new ItemDto { ... }) })` |

Do **not** refactor anonymous `Select` calls that do not use null-propagation
and do not need a named DTO type.

### 2. Choose the target pattern

| Scenario | Target |
|----------|--------|
| DTO used as API response, method return type, or shared within solution | **Explicit DTO**: `.UseLinqraft().Select<TDto>(x => new { ... })` |
| DTO exists with custom attributes/interfaces the code depends on | **Pre-existing DTO**: `.UseLinqraft().Select(x => new ExistingDto { ... })` — keep the class |

Default to **Explicit DTO** unless there is a clear reason to keep the existing class.

### 3. Apply the transformation

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

#### Transformation rules

1. **Add `.UseLinqraft()` before `.Select`**.
2. **Replace `new ExistingDto { ... }` with `new { ... }`** and move the DTO
   name to the generic parameter: `.Select<OrderDto>(...)`.
   For nested objects, use `new { ... }` — Linqraft generates nested DTOs
   automatically.
3. **Replace manual null-check ternaries with `?.`**:
   `x.Nav != null ? x.Nav.Prop : null` → `x.Nav?.Prop`
4. **Simplify member access** — use implicit names where possible:
   `Id = o.Id` → `o.Id`
5. **Capture local variables** — if the selector references locals, add:
   ```csharp
   .Select<TDto>(
       x => new { ... },
       capture: () => (local1, local2)
   )
   ```
6. **Preserve all other query operators** (`Where`, `OrderBy`, `Take`, etc.)
   — only transform the projection.

### 4. Clean up hand-written DTOs

If the DTO was written solely for this projection:

- **Delete the DTO class.** Linqraft generates it as a `partial` class.
- If the DTO has custom methods, attributes, or interfaces, keep only a
  `partial` declaration with those additions:
  ```csharp
  public partial class OrderDto : IValidatableObject
  {
      public IEnumerable<ValidationResult> Validate(ValidationContext ctx) { ... }
  }
  ```

If the DTO is referenced outside the current project or carries public API
attributes, keep the class and use the **Pre-existing DTO** pattern instead.

### 5. Verify

1. `dotnet build` — Linqraft runs at compile time; errors surface immediately.
2. Run existing tests to confirm behaviour is unchanged.

## Success Criteria

- All identified candidates are converted.
- Hand-written DTO classes that are no longer needed are removed.
- The project builds with zero errors and zero new warnings.
- Existing tests pass.
