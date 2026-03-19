# Projection Hooks

Projection hooks are explicit no-op marker methods that let you ask Linqraft to rewrite a specific part of a generated projection body.

They follow this flow:

`anonymous projection -> hook marker -> generated *Expr rewrite`

## Available Hooks

### `AsLeftJoin()`

Use `AsLeftJoin()` when you want a nullable navigation access to be emitted as an explicit left-join-style null guard inside generated projection code.

```csharp
var result = dbContext.Orders
    .SelectExpr<Order, OrderRowDto>(order => new
    {
        CustomerName = order.Customer.AsLeftJoin().Name,
    })
    .ToListAsync();
```

The generated projection behaves like:

```csharp
CustomerName = order.Customer != null ? order.Customer.Name : null
```

This is useful when the provider would otherwise translate a navigation access into a more restrictive join shape than you want.

### `AsProjectable()`

Use `AsProjectable()` when a selector references a computed instance property or method that should be inlined into the generated projection.

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
    .SelectExpr<Order, OrderRowDto>(order => new
    {
        FirstLargeItemProductName = order.FirstLargeItemProductName.AsProjectable(),
    })
    .ToListAsync();
```

Linqraft rewrites the hook as though the property body had been written directly inside the selector.

## Important Constraints

### Hooks only work inside generated projection contexts

Projection hooks are only recognized inside:

- `SelectExpr(...)`
- `SelectManyExpr(...)`
- `GroupByExpr(...)`
- `LinqraftKit.Generate(...)`

Using them outside those contexts triggers analyzer error [LQRE003](../analyzers/LQRE003.md).

### `AsProjectable()` bodies must not be recursive

`AsProjectable()` can inline instance properties and methods, but recursive expansions are rejected.

For example, this is unsupported:

```csharp
public int Recursive => this.Recursive.AsProjectable();
```

Linqraft detects recursive `AsProjectable()` expansion and stops generation with a clear error message instead of recursing forever.

### Hook names must be unique

If you customize `ProjectionHooks`, each hook method name must be unique. Duplicate method names are rejected during generation.

## Custom Hooks

The built-in options expose `ProjectionHooks`, so custom generator implementations can replace or extend the default hook list.

```csharp
public override IReadOnlyList<LinqraftProjectionHookDefinition> ProjectionHooks =>
[
    new("InlineProjectable", LinqraftProjectionHookKind.Projectable, "CustomProjectionHooks"),
];
```

## See Also

- [Usage Patterns](usage-patterns.md)
- [Local Variable Capture](local-variable-capture.md)
