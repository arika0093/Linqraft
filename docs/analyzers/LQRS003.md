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

- **Convert to SelectExpr<T, TDto> (convert all to anonymous)**
  - Converts the `Select` method to a generic `SelectExpr<TSource, TDto>` and **converts the named object creation into an anonymous object**. This variant recursively converts nested object creations as well (deep conversion). It also runs the ternary-null simplifier on the generated anonymous structures.

- **Convert to SelectExpr<T, TDto> (convert root only to anonymous)**
  - Similar to the above, but converts only the reported (root) object creation to an anonymous object; nested object creations are left as-is. Produces a generic `SelectExpr<TSource, TDto>` and applies the ternary-null simplifier to the root anonymous creation.

- **Convert to SelectExpr (use predefined classes)**
  - Replaces the method name `Select` with `SelectExpr` (no generic type arguments) and preserves the existing named DTO type in the selector. This variant also simplifies ternary-null checks inside the selector lambda but does not convert named object creation into anonymous objects.

### Automatic Ternary Null Check Simplification
As of version 0.5.0, all three code fix options **automatically simplify ternary null check patterns** (previously handled by LQRS004). When converting `Select` to `SelectExpr`, patterns like:

```csharp
prop = x.A != null ? new { x.A.B } : null
```

are automatically converted to:

```csharp
prop = new { B = x.A?.B }
```

This provides more concise code using null-conditional operators. See [LQRS004](LQRS004.md) for details on this transformation.

These three options map directly to the code-fix implementations: `ConvertToSelectExprExplicitDtoAllAsync`, `ConvertToSelectExprExplicitDtoRootOnlyAsync`, and `ConvertToSelectExprPredefinedDtoAsync`.

## Examples
The examples below show the concrete transformations performed by each fix.

1) Convert to `SelectExpr<T, TDto>` (all)

Before:
```csharp
var result = query
    .SelectMany(g => g.Items)
    .Select(x => new ProductDto // diagnostic reported here
    {
        Id = x.Id,
        Name = x.Name,
        Details = new ProductDetailDto { Desc = x.Detail.Desc }
    })
    .Select(p => new WrapperDto { Item = p });
```

After (all fix):
```csharp
var result = query
    .SelectMany(g => g.Items)
    .SelectExpr<Product, ResultDto_HASH>(x => new // root converted to anonymous
    {
        Id = x.Id,
        Name = x.Name,
        Details = new { Desc = x.Detail.Desc } // nested also converted to anonymous
    })
    .SelectExpr(p => new WrapperDto { Item = p });
```

Notes:
- Both the root `ProductDto` and the nested `ProductDetailDto` are converted to anonymous initializers (deep conversion).
- The fixer inserts generic type arguments on the `SelectExpr` call using a generated DTO name.

2) Convert to `SelectExpr<T, TDto>` (root-only)

Before:
```csharp
var result = query
    .Where(p => p.IsActive)
    .Select(x => new ProductDto // diagnostic reported here
    {
        Id = x.Id,
        Name = x.Name,
        Details = new ProductDetailDto { Desc = x.Detail.Desc }
    })
    .ToList();
```

After (root-only fix):
```csharp
var result = query
    .Where(p => p.IsActive)
    .SelectExpr<Product, ResultDto_HASH>(x => new // root converted to anonymous
    {
        Id = x.Id,
        Name = x.Name,
        Details = new ProductDetailDto { Desc = x.Detail.Desc } // nested remains named
    })
    .ToList();
```

Notes:
- The named `ProductDto` initializer is converted to an anonymous initializer at the root only.
- A DTO name (e.g. `ResultDto_HASH`) is generated for the generic type argument; the code-fix inserts type arguments but replaces the selector body with an anonymous object.

3) Convert to `SelectExpr` (predefined)

Before:
```csharp
// ProductDto already exists in the project
var result = query.Select(x => new ProductDto
{
    Id = x.Id,
    Name = x.Name,
    Details = new ProductDetailDto { Desc = x.Detail.Desc }
});
```

After (predefined fix):
```csharp
var result = query.SelectExpr(x => new ProductDto // named DTO preserved
{
    Id = x.Id,
    Name = x.Name,
    Details = new ProductDetailDto { Desc = x.Detail.Desc }
});
```

Notes:
- This variant preserves the named DTO (`ProductDto`) and only changes the method to `SelectExpr`.
- Ternary-null simplifications inside the lambda are still applied where the simplifier can transform patterns safely.

### With Ternary Null Check Simplification

Before:
```csharp
var result = query.Select(x => new ProductDto
{
    Id = x.Id,
    ChildData = x.Child != null ? x.Child.Name : null
});
```

After (with automatic simplification):
```csharp
var result = query.SelectExpr(x => new ProductDto
{
    Id = x.Id,
    ChildData = x.Child?.Name
});
```
