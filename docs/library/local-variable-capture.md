# Local Variable Capture

Local variables cannot be used directly inside `SelectExpr` because the selector is translated into a separate method. To use local variables, you must explicitly capture them.

## Problem

```csharp
var threshold = 100;
var converted = dbContext.Entities
    .SelectExpr<Entity, EntityDto>(x => new {
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
    .SelectExpr<Entity, EntityDto>(
        x => new {
            x.Id,
            IsExpensive = x.Price > threshold,
            DoubledValue = x.Value * multiplier,
            Description = x.Name + suffix,
        },
        capture: () => (threshold, multiplier, suffix)
    );
```

Anonymous-object captures such as `capture: new { threshold, multiplier, suffix }` are still supported for existing code, but the delegate form is the recommended option.

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

Linqraft provides an analyzer that automatically detects uncaptured local variables and suggests a code fix:

![Local variable capture error](../../assets/local-variable-capture-err.png)

Simply apply the suggested fix to add the capture parameter automatically.
