# LQRS002 - SelectToSelectExprAnonymousAnalyzer

**Severity:** Info  
**Category:** Design  
**Default:** Enabled

## Description
Detects `IQueryable.Select()` calls with anonymous type projections that can be converted to `SelectExpr` for better performance and type safety.

## When It Triggers
1. `Select()` is called on `IQueryable<T>`.
2. Lambda body creates an anonymous type (`new { ... }`).

## When It Doesn't Trigger
- `IEnumerable.Select()` calls.
- `Select()` without anonymous type.
- `Select()` with named type (handled by LQRS003).

## Code Fixes
`SelectToSelectExprAnonymousCodeFixProvider`:
1. Convert to `SelectExpr` (anonymous pattern).
2. Convert to `SelectExpr<T, TDto>` (explicit DTO pattern).

## Example
**Before:**
```csharp
var result = query.Select(x => new  // LQRS002
{
    x.Id,
    x.Name,
    x.Price
});
```
**After (anonymous pattern):**
```csharp
var result = query.SelectExpr(x => new
{
    x.Id,
    x.Name,
    x.Price
});
```
**After (explicit DTO pattern):**
```csharp
var result = query.SelectExpr<Product, ResultDto_XXXXXXXX>(x => new
{
    x.Id,
    x.Name,
    x.Price
});
```
