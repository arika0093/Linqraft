# LQRS001 - SelectExprToTypedAnalyzer

**Severity:** Hidden  
**Category:** Design  
**Default:** Enabled

## Description
Detects `SelectExpr` calls without explicit type arguments that can be converted to typed versions (`SelectExpr<T, TDto>`) for better type safety.

## When It Triggers
1. A `SelectExpr()` method is called without type arguments.
2. The lambda body contains an anonymous type.

## Code Fix
`SelectExprToTypedCodeFixProvider`:
- Convert to typed `SelectExpr<T, TDto>` (adds type arguments)

## Example
**Before:**
```csharp
var result = query.SelectExpr(x => new  // LQRS001
{
    x.Id,
    x.Name,
    x.Price
});
```
**After:**
```csharp
var result = query.SelectExpr<Product, ResultDto_XXXXXXXX>(x => new
{
    x.Id,
    x.Name,
    x.Price
});
```

## DTO Naming Convention
1. Variable name: `var products = ...` → `ProductsDto_HASH`
2. Assignment target: `products = ...` → `ProductsDto_HASH`
3. Method name: `GetProducts()` → `ProductsDto_HASH` (strips "Get")
4. Fallback: `ResultDto_HASH`

Hash suffix (8 chars, A-Z0-9) from property names via FNV-1a.
