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
`SelectToSelectExprNamedCodeFixProvider` can offer conversions such as:
- Convert to `SelectExpr(...)` preserving the named object creation.
- Convert to `SelectExpr<TSource, TDto>(...)` with an explicit DTO type argument when appropriate.

## Example
Before:
```csharp
var result = query.Select(x => new ProductDto  // LQRS003
{
    Id = x.Id,
    Name = x.Name
});
```

After (predefined DTO pattern):
```csharp
var result = query.SelectExpr(x => new ProductDto
{
    Id = x.Id,
    Name = x.Name
});
```

After (explicit DTO pattern):
```csharp
var result = query.SelectExpr<Product, ResultDto_XXXXXXXX>(x => new ProductDto
{
    Id = x.Id,
    Name = x.Name
});
```
