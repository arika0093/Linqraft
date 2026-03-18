# LQRE003 - ProjectionHookUsageAnalyzer

**Severity:** Error  
**Category:** Usage  
**Default:** Enabled

## Description
Detects Linqraft projection hooks such as `AsLeftJoin()` and `AsProjectable()` when they are used outside generated projection contexts.

These hook methods are intentionally no-op markers. They only have meaning when Linqraft rewrites a `SelectExpr`, `SelectManyExpr`, `GroupByExpr`, or `LinqraftKit.Generate(...)` body.

## When It Triggers
- When a Linqraft projection hook is invoked in regular application code
- And the invocation is **not** inside:
  - `SelectExpr(...)`
  - `SelectManyExpr(...)`
  - `GroupByExpr(...)`
  - `LinqraftKit.Generate(...)`

## Why This Is An Error
Outside a generated projection, these hook methods do not change query semantics by themselves. Allowing them in normal code would make the call site look meaningful even though Linqraft will not rewrite it.

This analyzer keeps hook usage explicit and prevents accidental no-op calls from leaking into application logic.

## Example
Before:
```csharp
var query = dbContext.Orders
    .Select(order => order.Customer.AsLeftJoin().Name); // LQRE003
```

After:
```csharp
var query = dbContext.Orders
    .SelectExpr(order => new
    {
        CustomerName = order.Customer.AsLeftJoin().Name,
    });
```

## Related
- [Projection Hooks](../library/projection-hooks.md)
- [LQRE001](./LQRE001.md)
