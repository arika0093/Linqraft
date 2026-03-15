# LQRW004 - AnonymousCaptureDelegatePatternAnalyzer

**Severity:** Warning  
**Category:** Usage  
**Default:** Enabled

## Description
Reports Linqraft calls that still use the legacy anonymous-object capture pattern such as `capture: new { threshold }`.

The delegate capture pattern, for example `capture: () => threshold` or `capture: () => (threshold, offset)`, is the preferred form because generated code can unpack it without runtime reflection and therefore stays compatible with NativeAOT scenarios.

## When It Triggers
- The invocation is a Linqraft capture-aware API such as `SelectExpr`, `SelectManyExpr`, `GroupByExpr`, or `LinqraftKit.Generate`.
- The `capture` argument is provided as an anonymous object.

## Code Fix
The code fix rewrites the anonymous-object capture into the delegate capture pattern while preserving the captured expressions and order.

## Example

Before:
```csharp
var threshold = 100;
var offset = 5;

var result = query.SelectExpr(
    x => new
    {
        x.Id,
        Score = x.Value + offset > threshold,
    },
    capture: new { threshold, offset });
```

After:
```csharp
var threshold = 100;
var offset = 5;

var result = query.SelectExpr(
    x => new
    {
        x.Id,
        Score = x.Value + offset > threshold,
    },
    capture: () => (threshold, offset));
```
