# LQRS005 - SelectToSelectExprAnonymousNullTernaryAnalyzer

**Severity:** Info  
**Category:** Design  
**Default:** Enabled

## Description
Detects `IQueryable<T>.Select(...)` calls whose selector returns an anonymous object and already contains a simplifiable null ternary. This is the surfaced companion to [LQRS002](./LQRS002.md): the conversion is the same, but the analyzer is raised as `Info` because the migration can immediately replace the null ternary with a cleaner null-conditional form.

## When It Triggers
- The invocation is a `Select(...)` call on `IQueryable<T>`.
- The selector returns an anonymous object (`new { ... }`).
- The selector body contains a simplifiable null ternary such as `x.Child != null ? new { x.Child.Id } : null`.

## Code Fixes
LQRS005 reuses the same code-fix family as [LQRS002](./LQRS002.md):
- **Convert to SelectExpr**
- **Convert to SelectExpr<TSource, TDto>**

Those fixes also:
- simplify the matching null ternary automatically
- add any required `capture:` entries automatically when outer variables are referenced

## Example
Before:
```csharp
var result = query.Select(x => new
{
    ChildData = x.Child != null ? new { x.Child.Name } : null
});
```

After:
```csharp
var result = query.SelectExpr(x => new
{
    ChildData = new { Name = x.Child?.Name }
});
```
