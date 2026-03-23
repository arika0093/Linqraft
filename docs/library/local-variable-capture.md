# Local Variable Capture

Local variables cannot be used directly inside a Linqraft projection because the selector is translated into a separate generated method. To use local variables, you must explicitly capture them.

## Problem

```csharp
var threshold = 100;
var converted = dbContext.Entities
    .UseLinqraft()
    .Select<EntityDto>(x => new
    {
        x.Id,
        IsExpensive = x.Price > threshold, // ERROR: Cannot access local variable
    });
```

## Solution: Use Capture Parameter

For new code, prefer a delegate capture so the generated code can stay NativeAOT-safe:

```csharp
var threshold = 100;
var multiplier = 2;
var suffix = " units";

var converted = dbContext.Entities
    .UseLinqraft()
    .Select<EntityDto>(
        x => new
        {
            x.Id,
            IsExpensive = x.Price > threshold,
            DoubledValue = x.Value * multiplier,
            Description = x.Name + suffix,
        },
        capture: () => (threshold, multiplier, suffix)
    );
```

Anonymous-object captures such as `capture: new { threshold, multiplier, suffix }` are still supported for existing code, but they are now obsolete. Use the delegate form for new code and let the analyzer/code fix migrate older call sites.

Linqraft intentionally keeps capture transport separate from the selector delegate itself. While captured locals are visible through `selector.Target` on a normal JIT runtime, NativeAOT trimming can remove the compiler-generated closure fields, so relying on reflection over the selector delegate is not stable enough for generated code.

## Generated Code

```csharp
public static IQueryable<TResult> SelectExpr_HASH<TIn, TResult>(
    this IQueryable<TIn> query, Func<TIn, TResult> selector, Func<object> capture)
{
    var matchedQuery = query as object as IQueryable<Entity>;
    var captureValue = ((int, int, string))capture();
    var threshold = captureValue.Item1;
    var multiplier = captureValue.Item2;
    var suffix = captureValue.Item3;

    var converted = matchedQuery.Select(x => new EntityDto
    {
        Id = x.Id,
        IsExpensive = x.Price > threshold,
        DoubledValue = x.Value * multiplier,
        Description = x.Name + suffix,
    });
    return converted as object as IQueryable<TResult>;
}
```

## Analyzer Support

Linqraft provides analyzers that automatically detect uncaptured local variables and legacy anonymous-object captures:

![Local variable capture error](../../assets/local-variable-capture-err.png)

Simply apply the suggested fix to add the capture parameter automatically. If you already have `capture: new { ... }`, the warning fixer rewrites it to `capture: () => (...)`.
