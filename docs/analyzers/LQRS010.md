# LQRS010 - ProjectionToLinqraftMappingAnalyzer

**Severity:** Hidden  
**Category:** Design  
**Default:** Enabled

## Description
Detects Linqraft projection calls that can be extracted into reusable `[LinqraftMapping]` declarations.

This applies to typed fluent projections such as `UseLinqraft().Select<TDto>(...)`, typed `SelectExpr`/`SelectManyExpr`/`GroupByExpr` calls, and predefined DTO initializers such as `new SomeDto { ... }`.

## When It Triggers
- `UseLinqraft().Select<TDto>(...)`
- `SelectExpr<TSource, TDto>(...)`
- `SelectManyExpr<...>(...)`
- `GroupByExpr<...>(...)`
- A supported projection whose selector creates a predefined DTO instance such as `new SomeDto { ... }`

## When It Doesn't Trigger
- Projections that are already inside a `[LinqraftMapping]` method
- Anonymous fluent forms without an explicit result type, such as `UseLinqraft().Select(x => new { ... })`

## Code Fix
The code fix converts the caller to `ProjectTo...(...)` form and adds a new mapping declaration file in the same folder.

Captured outer values become ordinary mapping-method parameters instead of `capture:`.

## Example
Before:
```csharp
var result = query
    .UseLinqraft()
    .Select<MyDto>(x => new { x.Id, x.Name })
    .ToList();
```

After:
```csharp
var result = query
    .ProjectToMyDto()
    .ToList();

public static partial class MyDtoMappingExtensions
{
    /// <summary>
    /// This method is a dummy declaration for implementing the generated projection extension method by Linqraft.
    /// </summary>
    [LinqraftMapping]
    internal static IQueryable<MyDto> ProjectToMyDto(this LinqraftMapper<QueryType> source) =>
        source.Select<MyDto>(x => new { x.Id, x.Name });
}
```
