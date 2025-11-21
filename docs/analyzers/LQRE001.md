# LQRE001 - LocalVariableCaptureAnalyzer

**Severity:** Error  
**Category:** Usage  
**Default:** Enabled

## Description
Ensures complete isolation of `SelectExpr` selector expressions by requiring external variable/member references to be passed through the `capture` parameter.

## Captured Elements Required
- Local variables (non-const)
- Method parameters
- Instance fields/properties (including `this.Property`)
- Static fields/properties (`ClassName.Member`)
- Const fields (class-level constants)

Const local variables do not need capture.

## When It Triggers
Any required external reference is used inside a `SelectExpr` lambda but omitted from `capture`.

## Code Fix
`LocalVariableCaptureCodeFixProvider`:
- Add or update `capture:` argument including all missing variables (merged & alphabetically sorted).

## Example
**Before:**
```csharp
var multiplier = 10;
var result = query.SelectExpr(x => new  // LQRE001
{
    x.Id,
    AdjustedPrice = x.Price * multiplier
});
```
**After:**
```csharp
var multiplier = 10;
var result = query.SelectExpr(x => new
{
    x.Id,
    AdjustedPrice = x.Price * multiplier
}, capture: new { multiplier });
```
