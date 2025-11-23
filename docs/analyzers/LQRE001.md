# LQRE001 - LocalVariableCaptureAnalyzer

**Severity:** Error  
**Category:** Usage  
**Default:** Enabled

## Description
Reports usage of local variables or outer-scope members inside a `SelectExpr` selector when they are not provided via the `capture` parameter. This helps avoid accidental closures and makes the selector body independent from outer mutable state.

## What Needs To Be Captured
- Local variables from an outer scope (non-const locals).
- Parameters from an outer scope (not the lambda parameters).
- Instance fields or properties accessed via `this` or instance references when they are not effectively constant.
- Static fields/properties when they are not compile-time constants.

Notes:
- Local `const` variables and enum members do not need to be captured.
- Public/internal `const` fields (compile-time constants) are accessible without capture.

## When It Triggers
When a `SelectExpr(...)` invocation's lambda body references any of the above elements but the invocation does not include a `capture` argument (or the referenced names are missing from the `capture` object).

## Code Fix Behavior
`LocalVariableCaptureCodeFixProvider` will:
- Compute the full set of required capture names (including already-captured ones).
- Create or update the `capture:` anonymous object argument with the missing names.
- For member-access expressions that require a local alias (e.g. `this.Member` or complex expressions), it creates local declarations (e.g. `var captured_X = this.Member;`) and inserts them before the invocation.

## Example
Before:
```csharp
var multiplier = 10;
var result = query.SelectExpr(x => new  // LQRE001
{
    x.Id,
    AdjustedPrice = x.Price * multiplier
});
```

After (code fix adds capture):
```csharp
var multiplier = 10;
var result = query.SelectExpr(x => new
{
    x.Id,
    AdjustedPrice = x.Price * multiplier
}, capture: new { multiplier });
```
