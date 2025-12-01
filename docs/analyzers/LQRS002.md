# LQRS002 - SelectToSelectExprAnonymousAnalyzer

**Severity:** Info  
**Category:** Design  
**Default:** Enabled

## Description
Detects `System.Linq` `Select` calls performed on `IQueryable<T>` whose selector returns an anonymous object. These calls can be converted to `SelectExpr` (Linqraft) to gain the library's projection behavior and better type handling for query translation.

## When It Triggers
- The invocation is a `Select(...)` method from `System.Linq` (symbol name `Select`).
- The receiver is an `IQueryable<T>` (or type implementing `IQueryable<T>`).
- The selector lambda returns an anonymous object (`new { ... }`).

## When It Doesn't Trigger
- Calls on `IEnumerable<T>`/in-memory sequences (the analyzer checks the invocation target's type).
- Selectors that do not create an anonymous type.
- Cases with named object creations are handled by `LQRS003`.

## Code Fixes
`SelectToSelectExprAnonymousCodeFixProvider` can offer conversions such as:
- Convert `Select(...)` → `SelectExpr(...)` preserving the anonymous projection.
- Convert `Select(...)` → `SelectExpr<TSource, TDto>(...)` with a generated DTO type when that is preferable.

### Automatic Ternary Null Check Simplification
As of version 0.5.0, the code fix also **automatically simplifies ternary null check patterns** (previously handled by LQRS004). When converting `Select` to `SelectExpr`, patterns like:

```csharp
prop = x.A != null ? new { x.A.B } : null
```

are automatically converted to:

```csharp
prop = new { B = x.A?.B }
```

This provides more concise code using null-conditional operators. See [LQRS004](LQRS004.md) for details on this transformation.

## Example
Before:
```csharp
var result = query.Select(x => new  // LQRS002
{
    x.Id,
    x.Name,
    x.Price
});
```

After (anonymous pattern):
```csharp
var result = query.SelectExpr(x => new
{
    x.Id,
    x.Name,
    x.Price
});
```

After (explicit DTO pattern):
```csharp
var result = query.SelectExpr<Product, ResultDto_XXXXXXXX>(x => new
{
    x.Id,
    x.Name,
    x.Price
});
```

### With Ternary Null Check Simplification
Before:
```csharp
var result = query.Select(x => new
{
    ChildData = x.Child != null ? new { x.Child.Name } : null
});
```

After (with automatic simplification):
```csharp
var result = query.SelectExpr(x => new
{
    ChildData = new { Name = x.Child?.Name }
});
```
