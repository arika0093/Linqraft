# LQRS003 - SelectToSelectExprNamedAnalyzer

**Severity:** Info  
**Category:** Design  
**Default:** Enabled

## Description
Detects `System.Linq` `Select` invocations on `IQueryable<T>` whose selector creates an instance of a named type (e.g. `new SomeDto { ... }`). Such projections can be converted to Linqraft's `SelectExpr` to benefit from Linqraft projection semantics.

## When It Triggers
- The `Select` method is invoked from `System.Linq`.
- The receiver expression is `IQueryable<T>` (or type implementing it).
- The selector lambda returns a named object (an `ObjectCreationExpressionSyntax`, not an anonymous object).

## When It Doesn't Trigger
- Calls on `IEnumerable<T>`.
- Selectors that do not create an object.
- Anonymous-type projections (handled by `LQRS002`).

## Code Fixes
`SelectToSelectExprNamedCodeFixProvider` registers three distinct fixes (titles shown are from the provider):

- **Convert to SelectExpr<T, TDto>**
  - Converts the `Select` method to a generic `SelectExpr<TSource, TDto>` and **converts the named object creation into an anonymous object**. This variant recursively converts nested object creations as well (deep conversion). It also runs the ternary-null simplifier on the generated anonymous structures, converting patterns like `x.A != null ? x.A.B : null` to `x.A?.B`.

- **Convert to SelectExpr<T, TDto> (strict)**
  - Similar to the above, but **does NOT apply ternary null check simplification**. This preserves the original ternary patterns (e.g., `x.A != null ? x.A.B : null` remains unchanged). Useful when you want to maintain the exact nullability structure of the original code.

- **Convert to SelectExpr (use predefined classes)**
  - Replaces the method name `Select` with `SelectExpr` (no generic type arguments) and preserves the existing named DTO type in the selector. This variant also simplifies ternary-null checks inside the selector lambda.

### Automatic Ternary Null Check Simplification
As of version 0.5.0, the first and third code fix options **automatically simplify ternary null check patterns** (previously handled by LQRS004). When converting `Select` to `SelectExpr`, patterns like:

```csharp
prop = x.A != null ? x.A.B : null
```

are automatically converted to:

```csharp
prop = x.A?.B
```

This provides more concise code using null-conditional operators. The second option (strict) intentionally preserves the original ternary patterns. See [LQRS004](LQRS004.md) for details on this transformation.

These three options map directly to the code-fix implementations: `ConvertToSelectExprExplicitDtoAsync`, `ConvertToSelectExprExplicitDtoStructAsync`, and `ConvertToSelectExprPredefinedDtoAsync`.

## Examples
The examples below show the concrete transformations performed by each fix.

1) Convert to `SelectExpr<T, TDto>` (with ternary simplification)

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
    .SelectExpr<Product, ResultDto_HASH>(x => new // root converted to anonymous
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

2) Convert to `SelectExpr<T, TDto>` (struct - no ternary simplification)

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
    .SelectExpr<Product, ResultDto_HASH>(x => new // root converted to anonymous
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

3) Convert to `SelectExpr` (predefined)

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
var result = query.SelectExpr(x => new ProductDto // named DTO preserved
{
    Id = x.Id,
    Name = x.Name,
    DetailDesc = x.Detail?.Desc // ternary simplified
});
```

Notes:
- This variant preserves the named DTO (`ProductDto`) and only changes the method to `SelectExpr`.
- Ternary-null simplifications inside the lambda are applied.
