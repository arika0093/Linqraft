# Projection Helpers

Projection helpers are exposed through the `IProjectionHelper` selector parameter so you can ask Linqraft to rewrite a specific fragment inside a generated projection body.

After support code is generated, matching extension methods are also available, but the examples below use the `helper` parameter so nullable navigation members do not need `!`.

## Available Helpers

### `helper.AsLeftJoin(value)`

Use `helper.AsLeftJoin(value)` when you want a nullable navigation access to be emitted as an explicit left-join-style null guard inside generated projection code.

```csharp
var result = dbContext.Orders
    .UseLinqraft()
    .Select<OrderRowDto>((order, helper) => new
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

### `helper.AsInnerJoin(value)`

Use `helper.AsInnerJoin(value)` when the generated query should behave like an `INNER JOIN` for the helper target.

```csharp
var result = dbContext.Orders
    .UseLinqraft()
    .Select<OrderRowDto>((order, helper) => new
    {
        CustomerName = helper.AsInnerJoin(order.Customer).Name,
    })
    .ToListAsync();
```

Linqraft rewrites the query so rows where the helper target is `null` are filtered out before the generated projection runs.

### `helper.AsInline(value)`

Use `helper.AsInline(value)` when a selector references a computed instance property or method that should be inlined into the generated projection.

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
    .UseLinqraft()
    .Select<OrderRowDto>((order, helper) => new
    {
        FirstLargeItemProductName = helper.AsInline(order.FirstLargeItemProductName),
    })
    .ToListAsync();
```

Linqraft rewrites the helper call as though the property body had been written directly inside the selector.

### `helper.AsProjection<TDto>(value)`

Use `helper.AsProjection<TDto>(value)` when you want a nested member to become a DTO explicitly instead of exposing the original entity or complex type.

```csharp
var result = dbContext.Orders
    .UseLinqraft()
    .Select<OrderRowDto>((order, helper) => new
    {
        order.Id,
        Customer = helper.AsProjection<CustomerSummaryDto>(order.Customer),
    })
    .ToListAsync();
```

If you omit the generic argument, Linqraft uses `[SourceTypeName]Dto`:

```csharp
var result = dbContext.Orders
    .UseLinqraft()
    .Select<OrderRowDto>((order, helper) => new
    {
        order.Id,
        Customer = helper.AsProjection(order.Customer),
    })
    .ToListAsync();
```

In that example, `Customer` becomes `CustomerDto`.

`AsProjection` currently copies scalar, enum, string, and value-type members from the source object. Navigation and collection members are not expanded automatically.

### `helper.Project<T>(value).Select(...)`

Use `Project(...).Select(...)` when you want to shape a nested projection without repeating the full member path in every selected member.

```csharp
var result = dbContext.Orders
    .UseLinqraft()
    .Select<OrderRowDto>((order, helper) => new
    {
        order.Id,
        Customer = helper
            .Project<Customer>(order.Customer)
            .Select(customer => new { customer.Id, customer.Name }),
    })
    .ToListAsync();
```

The generic argument is a naming hint for the generated DTO. In the example above, Linqraft generates `CustomerDto`.

If you omit the generic argument, Linqraft uses the destination member name:

```csharp
var result = dbContext.Orders
    .UseLinqraft()
    .Select<OrderRowDto>((order, helper) => new
    {
        order.Id,
        SelectedCustomer = helper.Project(order.Customer).Select(customer => new
        {
            customer.Id,
            customer.Name,
        }),
    })
    .ToListAsync();
```

In that case, Linqraft generates `SelectedCustomerDto`.

## Important Constraints

### Helpers are available through the generated selector helper

`UseLinqraft().Select(...)`, `UseLinqraft().SelectMany(...)`, and `UseLinqraft().GroupBy(...)` provide an `IProjectionHelper` instance as the selector's second parameter when you need helper rewrites:

```csharp
query.UseLinqraft().Select((entity, helper) => new
{
    Name = helper.AsLeftJoin(entity.Child).Name,
});
```

### `helper.AsInline(value)` bodies must not be recursive

`helper.AsInline(value)` can inline instance properties and methods, but recursive expansions are rejected.

For example, this is unsupported:

```csharp
public int Recursive(IProjectionHelper helper) => helper.AsInline(this.Recursive(helper));
```

Linqraft detects recursive `AsInline` expansion and stops generation with a clear error message instead of recursing forever.

### Helper names must be unique

If you customize `ProjectionHooks`, each helper method name must be unique. Duplicate method names are rejected during generation.

## Custom Helpers

The built-in options expose `ProjectionHooks`, so custom generator implementations can replace or extend the default helper list.

```csharp
public override IReadOnlyList<LinqraftProjectionHookDefinition> ProjectionHooks =>
[
    new("InlineProjectable", LinqraftProjectionHookKind.Projectable),
];
```

## See Also

- [Usage Patterns](usage-patterns.md)
- [Local Variable Capture](local-variable-capture.md)
