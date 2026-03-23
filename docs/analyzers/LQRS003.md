# LQRS003 - SelectToSelectExprNamedAnalyzer

**Severity:** Hidden  
**Category:** Design  
**Default:** Enabled

## Description
Detects `System.Linq` `Select` invocations on `IQueryable<T>` whose selector creates an instance of a named type (e.g. `new SomeDto { ... }`). Such projections can be converted to Linqraft's `UseLinqraft().Select(...)` forms to benefit from Linqraft projection semantics.

## When It Triggers
- The `Select` method is invoked from `System.Linq`.
- The receiver expression is `IQueryable<T>` (or type implementing it).
- The selector lambda returns a named object (an `ObjectCreationExpressionSyntax`, not an anonymous object).

## When It Doesn't Trigger
- Calls on `IEnumerable<T>`.
- Selectors that do not create an object.
- Anonymous-type projections (handled by `LQRS002`).
- Named selectors that already contain a simplifiable null ternary are surfaced as [LQRS006](./LQRS006.md) instead.

## Code Fixes
`SelectToSelectExprNamedCodeFixProvider` registers three distinct fixes (titles shown are from the provider):

- **Convert to UseLinqraft().Select<TDto>()**
  - Converts the `Select` method to a generic `UseLinqraft().Select<TDto>()` call and **converts the named object creation into an anonymous object**. This variant recursively converts nested object creations as well (deep conversion). It also runs the ternary-null simplifier on the generated anonymous structures, converting patterns like `x.A != null ? x.A.B : null` to `x.A?.B`.

- **Convert to UseLinqraft().Select<TDto>() (strict)**
  - Similar to the above, but **does NOT apply ternary null check simplification**. This preserves the original ternary patterns (e.g., `x.A != null ? x.A.B : null` remains unchanged). Useful when you want to maintain the exact nullability structure of the original code. You can apply [LQRS004](LQRS004.md) manually afterward if needed.

- **Convert to UseLinqraft().Select() (use predefined classes)**
  - Replaces the method name `Select` with `UseLinqraft().Select` (no generic type arguments) and preserves the existing named DTO type in the selector. This variant **does NOT apply ternary null check simplification**, preserving the original ternary patterns. You can apply [LQRS004](LQRS004.md) manually afterward if needed.

All three conversions also add the required `capture:` entries automatically when the selector uses outer variables.

### Automatic Ternary Null Check Simplification
Selectors that contain these patterns are reported as [LQRS006](./LQRS006.md), but they reuse the same code-fix family.

The first code fix option **automatically simplifies ternary null check patterns**. When converting `Select` to `UseLinqraft().Select(...)`, patterns like:

```csharp
prop = x.A != null ? x.A.B : null
```

are automatically converted to:

```csharp
prop = x.A?.B
```

This provides more concise code using null-conditional operators. The second option (strict) and third option (predefined) intentionally preserve the original ternary patterns. You can use [LQRS004](LQRS004.md) to manually apply the transformation afterward.

These three options map directly to the code-fix implementations: `ConvertToSelectExprExplicitDtoAsync`, `ConvertToSelectExprExplicitDtoStructAsync`, and `ConvertToSelectExprPredefinedDtoAsync`.

## Examples
The examples below show the concrete transformations performed by each fix.

1) Convert to `UseLinqraft().Select<TDto>()` (with ternary simplification)

Before:
```csharp
var result = query
    .SelectMany(g => g.Items)
    .Select(x => new ProductDto // diagnostic reported here
    {
        Id = x.Id,
        Name = x.Name,
        DetailDesc = x.Detail != null ? x.Detail.Desc : null
    })
    .ToList();
```

After:
```csharp
var result = query
    .SelectMany(g => g.Items)
    .UseLinqraft().Select<ResultDto_HASH>(x => new // root converted to anonymous
    {
        Id = x.Id,
        Name = x.Name,
        DetailDesc = x.Detail?.Desc // ternary simplified to null-conditional
    })
    .ToList();
```

Notes:
- The named `ProductDto` initializer is converted to an anonymous initializer.
- Ternary null check patterns are simplified to null-conditional operators.
- A DTO name (e.g. `ResultDto_HASH`) is generated for the generic type argument.

2) Convert to `UseLinqraft().Select<TDto>()` (strict - no ternary simplification)

Before:
```csharp
var result = query
    .Where(p => p.IsActive)
    .Select(x => new ProductDto // diagnostic reported here
    {
        Id = x.Id,
        Name = x.Name,
        DetailDesc = x.Detail != null ? x.Detail.Desc : null
    })
    .ToList();
```

After (struct fix):
```csharp
var result = query
    .Where(p => p.IsActive)
    .UseLinqraft().Select<ResultDto_HASH>(x => new // root converted to anonymous
    {
        Id = x.Id,
        Name = x.Name,
        DetailDesc = x.Detail != null ? x.Detail.Desc : null // ternary preserved
    })
    .ToList();
```

Notes:
- The named `ProductDto` initializer is converted to an anonymous initializer.
- Ternary null check patterns are **preserved** - no simplification is applied.
- Use this option when you need to maintain the exact nullability structure.

3) Convert to `UseLinqraft().Select()` (predefined)

Before:
```csharp
// ProductDto already exists in the project
var result = query.Select(x => new ProductDto
{
    Id = x.Id,
    Name = x.Name,
    DetailDesc = x.Detail != null ? x.Detail.Desc : null
});
```

After (predefined fix):
```csharp
var result = query.UseLinqraft().Select(x => new ProductDto // named DTO preserved
{
    Id = x.Id,
    Name = x.Name,
    DetailDesc = x.Detail != null ? x.Detail.Desc : null // ternary preserved
});
```

Notes:
- This variant preserves the named DTO (`ProductDto`) and only changes the call to `UseLinqraft().Select(...)`.
- Ternary null check patterns are **preserved** - no simplification is applied.
- You can apply [LQRS004](LQRS004.md) manually afterward if you want the simplified form.
