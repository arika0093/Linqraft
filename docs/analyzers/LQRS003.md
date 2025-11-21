# LQRS003 - SelectToSelectExprNamedAnalyzer

**Severity:** Info  
**Category:** Design  
**Default:** Enabled

## Description
Detects `IQueryable.Select()` calls with named/predefined type projections that can be converted to `SelectExpr`.

## When It Triggers
1. `Select()` is called on `IQueryable<T>`.
2. Lambda body creates a named object (`new SomeDto { ... }`).

## When It Doesn't Trigger
- `IEnumerable.Select()` calls.
- `Select()` without object creation.
- `Select()` with anonymous type (handled by LQRS002).

## Code Fixes
`SelectToSelectExprNamedCodeFixProvider`:
1. Convert to `SelectExpr<T, TDto>` (explicit DTO pattern).
2. Convert to `SelectExpr` (predefined DTO pattern).

## Example (predefined DTO pattern)
**Before:**
```csharp
var result = query.Select(x => new ProductDto  // LQRS003
{
    Id = x.Id,
    Name = x.Name
});
```
**After:**
```csharp
var result = query.SelectExpr(x => new ProductDto
{
    Id = x.Id,
    Name = x.Name
});
```
**After (explicit DTO pattern):**
```csharp
var result = query.SelectExpr<Product, ResultDto_XXXXXXXX>(x => new ProductDto
{
    Id = x.Id,
    Name = x.Name
});
```
