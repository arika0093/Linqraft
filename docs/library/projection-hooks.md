# Projection Hooks

Projection hooks are exposed through the `IProjectionHelper` selector parameter so you can ask Linqraft to rewrite a specific part of a generated projection body.

They follow this flow:

`anonymous projection -> helper hook -> generated *Expr rewrite`

## Available Hooks

### `helper.AsLeftJoin(value)`

Use `helper.AsLeftJoin(value)` when you want a nullable navigation access to be emitted as an explicit left-join-style null guard inside generated projection code.

```csharp
var result = dbContext.Orders
    .SelectExpr<Order, OrderRowDto>((order, helper) => new
    {
        CustomerName = helper.AsLeftJoin(order.Customer).Name,
    })
    .ToListAsync();
```

The generated projection behaves like:

```csharp
CustomerName = order.Customer != null ? order.Customer.Name : null
```

This is useful when the provider would otherwise translate a navigation access into a more restrictive join shape than you want.

### `helper.AsProjectable(value)`

Use `helper.AsProjectable(value)` when a selector references a computed instance property or method that should be inlined into the generated projection.

```csharp
public sealed class Order
{
    public List<OrderItem> Items { get; set; } = [];

    public string? FirstLargeItemProductName => this
        .Items.Where(item => item.Quantity >= 2)
        .OrderBy(item => item.Id)
        .Select(item => item.ProductName)
        .FirstOrDefault();
}

var result = dbContext.Orders
    .SelectExpr<Order, OrderRowDto>((order, helper) => new
    {
        FirstLargeItemProductName = helper.AsProjectable(order.FirstLargeItemProductName),
    })
    .ToListAsync();
```

Linqraft rewrites the hook as though the property body had been written directly inside the selector.

## Important Constraints

### Hooks are available through the generated selector helper

`SelectExpr(...)`, `SelectManyExpr(...)`, and `GroupByExpr(...)` provide an `IProjectionHelper` instance as the selector's second parameter when you need hook rewrites:

```csharp
query.SelectExpr((entity, helper) => new
{
    Name = helper.AsLeftJoin(entity.Child).Name,
});
```

### `helper.AsProjectable(value)` bodies must not be recursive

`helper.AsProjectable(value)` can inline instance properties and methods, but recursive expansions are rejected.

For example, this is unsupported:

```csharp
public int Recursive(IProjectionHelper helper) => helper.AsProjectable(this.Recursive(helper));
```

Linqraft detects recursive `AsProjectable` expansion and stops generation with a clear error message instead of recursing forever.

### Hook names must be unique

If you customize `ProjectionHooks`, each hook method name must be unique. Duplicate method names are rejected during generation.

## Custom Hooks

The built-in options expose `ProjectionHooks`, so custom generator implementations can replace or extend the default hook list.

```csharp
public override IReadOnlyList<LinqraftProjectionHookDefinition> ProjectionHooks =>
[
    new("InlineProjectable", LinqraftProjectionHookKind.Projectable),
];
```

## See Also

- [Usage Patterns](usage-patterns.md)
- [Local Variable Capture](local-variable-capture.md)
