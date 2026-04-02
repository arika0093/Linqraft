---
name: Linqraft
description: Generates DTO classes from LINQ projection selectors at compile time. Reference when generating DTOs or when UseLinqraft() appears in code.
---

# Linqraft

## Overview

Linqraft is a C# Roslyn Source Generator. Install the `Linqraft` NuGet package, write a `.UseLinqraft().Select<TDto>(...)` expression with an anonymous-object body, and Linqraft generates the corresponding DTO partial class at compile time. No hand-written DTO files are needed.

Linqraft also rewrites C# null-propagation operators (`?.`) into expression-tree-safe ternary guards — something standard LINQ expression trees do not support.

The generated code is a zero-dependency compile-time artifact. Nothing from Linqraft ships in the published application.

## Guidelines

### When to use

Use `UseLinqraft().Select<TDto>(...)` when you want a **disposable DTO** — a type whose shape is defined by the projection query and that is **not directly exposed outside the solution**. Typical cases:

- Internal API response shapes behind a controller or service boundary
- View models for Razor / Blazor pages
- DTOs projected from `IQueryable<T>` (EF Core, LINQ to SQL) or `IEnumerable<T>`
- Queries that traverse nullable navigation properties (`o.Customer?.Address?.City`)

### When NOT to use

- **Anonymous `Select` that does not use `?.`** — If the projection needs neither a named DTO type nor null-propagation rewriting, a plain `.Select(x => new { ... })` is simpler. Do not add `UseLinqraft()`.
- **Public API contract types** — DTOs that carry serialization attributes (`[JsonPropertyName]`), implement interfaces, or are shared across assemblies should be hand-maintained.

## Examples

### Basic explicit DTO projection

```csharp
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

Linqraft generates `OrderDto` (and a nested `ItemsDto`) as partial classes. The top-level DTO lives in the caller's namespace; nested DTOs go into a `LinqraftGenerated_{Hash}` sub-namespace.

### Local variable capture

Selectors are rewritten into generated methods. Reference local variables through a `capture:` delegate:

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

### Extending generated DTOs

Generated DTOs are `partial`. Add methods, interfaces, or attributes in a separate file:

```csharp
public partial class OrderDto : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext ctx) { /* ... */ }
}
```

### Inspecting generated code

Add the following to your `.csproj`, then build:

```xml
<PropertyGroup>
  <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
  <CompilerGeneratedFilesOutputPath>Generated</CompilerGeneratedFilesOutputPath>
</PropertyGroup>
```

Generated files appear under `Generated/Linqraft.SourceGenerator/`. After inspection, remove the two properties from `.csproj` and delete the `Generated/` folder.

## Installation

```bash
dotnet add package Linqraft
```

Requires C# 12.0 or later. On .NET 8+, it works out of the box.

For projection helpers, reusable mapping methods, MSBuild customization, and other advanced topics, see the [Linqraft README](https://github.com/arika0093/Linqraft).
