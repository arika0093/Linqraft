# LQRS006 - SelectToSelectExprNamedNullTernaryAnalyzer

**Severity:** Info  
**Category:** Design  
**Default:** Enabled

## Description
Detects `IQueryable<T>.Select(...)` calls whose selector returns a named object creation and already contains a simplifiable null ternary. This is the surfaced companion to [LQRS003](./LQRS003.md): the same conversion options are available, but the analyzer is raised as `Info` because the migration can immediately simplify the null ternary.

## When It Triggers
- The invocation is a `Select(...)` call on `IQueryable<T>`.
- The selector returns a named object creation (`new SomeDto { ... }`).
- The selector body contains a simplifiable null ternary.

## Code Fixes
LQRS006 reuses the same code-fix family as [LQRS003](./LQRS003.md):
- **Convert to UseLinqraft().Select<TDto>()**
- **Convert to UseLinqraft().Select<TDto>() (strict)**
- **Convert to UseLinqraft().Select() (use predefined classes)**

The first option simplifies the null ternary automatically. All three options also add any required `capture:` entries automatically when outer variables are referenced.

## Example
Before:
```csharp
var result = query.Select(x => new ProductDto
{
    ChildData = x.Child != null ? new ChildDto { Name = x.Child.Name } : null
});
```

After (`Convert to UseLinqraft().Select<TDto>()`):
```csharp
var result = query.UseLinqraft().Select<ProductDto>(x => new
{
    ChildData = new { Name = x.Child?.Name }
});
```
