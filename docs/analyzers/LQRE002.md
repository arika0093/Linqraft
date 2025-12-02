# LQRE002 - GroupByAnonymousKeyAnalyzer

**Severity:** Error  
**Category:** Usage  
**Default:** Enabled

## Description
Detects `GroupBy` calls that use an anonymous type as the key selector when followed by `SelectExpr`. This pattern is problematic because the generated code cannot properly handle anonymous types in the input type parameter.

## When It Triggers
- When `GroupBy` is called with an anonymous type key selector (e.g., `GroupBy(x => new { x.Prop1, x.Prop2 })`)
- And the result is used with `SelectExpr` (either directly or after intermediate LINQ methods like `Where`)

## Why This Is An Error
When using `SelectExpr` after `GroupBy` with an anonymous type key, the source generator cannot properly convert the input type because anonymous types cannot be referenced by name in the generated code.

For example, given this code:
```csharp
var result = dbContext.Entities
    .GroupBy(e => new { e.CategoryId, e.CategoryType })
    .SelectExpr(g => new
    {
        CategoryId = g.Key.CategoryId,
        CategoryType = g.Key.CategoryType,
        Count = g.Count(),
    })
    .ToList();
```

The generated code would look like this:
```csharp
public static IQueryable<TResult> SelectExpr_xxx<TIn, TResult>(
    this IQueryable<TIn> query, Func<TIn, TResult> selector)
{
    // TIn is inferred as anonymous type here (compile error)
    var matchedQuery = query as object as IQueryable<IGrouping</*anonymous type*/, Entity>>;
    // ...
}
```

Since anonymous types cannot be referenced by name in code, this results in a compile error.

## Code Fix
`GroupByAnonymousKeyCodeFixProvider` provides a fix that:
1. Converts the anonymous type in the `GroupBy` key selector to a named class
2. Generates the DTO class definition and adds it to the current file

The code fix automatically:
- Infers a meaningful class name based on the source entity type (e.g., `EntityGroupKey`)
- Creates a partial class with all the properties from the anonymous type
- Replaces the anonymous type instantiation with the named type

## Example
Before:
```csharp
var result = dbContext.Entities
    .GroupBy(e => new { e.CategoryId, e.CategoryType }) // LQRE002 error
    .SelectExpr(g => new
    {
        CategoryId = g.Key.CategoryId,
        CategoryType = g.Key.CategoryType,
        Count = g.Count(),
    })
    .ToList();
```

After applying the code fix:
```csharp
var result = dbContext.Entities
    .GroupBy(e => new EntityGroupKey { CategoryId = e.CategoryId, CategoryType = e.CategoryType })
    .SelectExpr(g => new
    {
        CategoryId = g.Key.CategoryId,
        CategoryType = g.Key.CategoryType,
        Count = g.Count(),
    })
    .ToList();

public partial class EntityGroupKey
{
    public required int CategoryId { get; set; }
    public required string CategoryType { get; set; }
}
```

## Related
- [LQRF001](./LQRF001.md) - Convert anonymous types to DTO classes (general case)
- [Known Issues in README](../README.md#known-issues) - Documentation about this limitation
