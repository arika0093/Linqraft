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

Pass local variables as an anonymous object through the `capture` parameter:

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
        capture: new { threshold, multiplier, suffix }
    );
```

## Generated Code

```csharp
public static IQueryable<TResult> SelectExpr_HASH<TIn, TResult>(
    this IQueryable<TIn> query, Func<TIn, TResult> selector, object captureParam)
{
    var matchedQuery = query as object as IQueryable<Entity>;
    dynamic captureObj = captureParam;
    int threshold = captureObj.threshold;
    int multiplier = captureObj.multiplier;
    string suffix = captureObj.suffix;

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
