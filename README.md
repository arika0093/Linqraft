# EFCore.ExprGenerator

EFCore.ExprGenerator is a source generator that makes writing Select queries in Entity Framework Core (EF Core) more concise and enables the use of nullable expressions in selectors.

[English](./README.md) | [Japanese](./README.ja.md)

## Problem
Consider fetching data from a table that has many related tables in EF Core.

Using `Include` and `ThenInclude` quickly makes the code verbose and hard to read. If you forget an `Include`, you can encounter a `NullReferenceException` at runtime and it is difficult to detect. Also, `Include` retrieves all related data which can be inefficient for performance.

```csharp
var orders = await dbContext.Orders
    .Include(o => o.Customer)
        .ThenInclude(c => c.Address)
            .ThenInclude(a => a.Country)
    .Include(o => o.Customer)
        .ThenInclude(c => c.Address)
            .ThenInclude(a => a.City)
    .Include(o => o.OrderItems)
        .ThenInclude(oi => oi.Product)
    .ToListAsync();
```

A better approach is to use DTOs (Data Transfer Objects) and select only the data you need.

```csharp
var orders = await dbContext.Orders
    .Select(o => new OrderDto
    {
        Id = o.Id,
        CustomerName = o.Customer.Name,
        CustomerCountry = o.Customer.Address.Country.Name,
        CustomerCity = o.Customer.Address.City.Name,
        Items = o.OrderItems.Select(oi => new OrderItemDto
        {
            ProductName = oi.Product.Name,
            Quantity = oi.Quantity
        }).ToList()
    })
    .ToListAsync();
```

This approach is much more efficient because it retrieves only the needed columns. However, it has drawbacks:

- While you can use anonymous types, you must define DTO classes manually when you need to pass results between methods or return them.
- If child objects have nullable properties, you end up writing verbose ternary expressions to guard each access.

Because nullable operators are not supported inside expression trees, you cannot write `o.Customer?.Name` directly in many selectors. The code often becomes:

```csharp
var orders = await dbContext.Orders
    .Select(o => new OrderDto
    {
        Id = o.Id,
        CustomerName = o.Customer != null ? o.Customer.Name : null,
        CustomerCountry = o.Customer != null && o.Customer.Address != null && o.Customer.Address.Country != null
            ? o.Customer.Address.Country.Name
            : null,
        CustomerCity = o.Customer != null && o.Customer.Address != null && o.Customer.Address.City != null
            ? o.Customer.Address.City.Name
            : null,
        Items = o.OrderItems != null
            ? o.OrderItems.Select(oi => new OrderItemDto
            {
                ProductName = oi.Product != null ? oi.Product.Name : null,
                Quantity = oi.Quantity
            }).ToList()
            : new List<OrderItemDto>()
    })
    .ToListAsync();
```

## Features
EFCore.ExprGenerator is a Source Generator designed to solve the issues above. With it you can write selectors like this:

```csharp
var orders = await dbContext.Orders
    .SelectExpr(o => new
    {
        Id = o.Id,
        CustomerName = o.Customer?.Name,
        CustomerCountry = o.Customer?.Address?.Country?.Name,
        CustomerCity = o.Customer?.Address?.City?.Name,
        Items = o.OrderItems?.Select(oi => new
        {
            ProductName = oi.Product?.Name,
            Quantity = oi.Quantity
        })
    })
    .ToListAsync();
```

Note that you don't need to define `OrderDto` or `OrderItemDto`. The source generator automatically generates code from the anonymous selector. For example it will generate a method like:

<details>
<summary>Generated method example</summary>

```csharp
namespace EFCore.ExprGenerator.Sample;
internal static class GeneratedExpression
{
    /// <summary>
    /// generated method
    /// </summary>
    public static IQueryable<OrderDto_D03CE9AC> SelectExpr<TResult>(
        this IQueryable<Order> query,
        Func<Order, TResult> selector)
    {
        return query.Select(s => new OrderDto_D03CE9AC
        {
            Id = s.Id,
            CustomerName = s.Customer != null ? s.Customer.Name : default,
            CustomerCountry = s.Customer != null && s.Customer.Address != null && s.Customer.Address.Country != null ? s.Customer.Address.Country.Name : default,
            CustomerCity = s.Customer != null && s.Customer.Address != null && s.Customer.Address.City != null ? s.Customer.Address.City.Name : default,
            Items = s.OrderItems != null ? s.OrderItems.Select(oi => new OrderItemDto_34ADD7E8
                {
                    ProductName = oi.Product != null ? oi.Product.Name : default,
                    Quantity = oi.Quantity
                })
                : default,
        });
    }
}

public class OrderItemDto_34ADD7E8
{
    public required string? ProductName { get; set; }
    public required int Quantity { get; set; }
}

public class OrderDto_D03CE9AC
{
    public required int Id { get; set; }
    public required string? CustomerName { get; set; }
    public required string? CustomerCountry { get; set; }
    public required string? CustomerCity { get; set; }
    public required IEnumerable<OrderItemDto_34ADD7E8> Items { get; set; }
}
```

</details>

## Usage
### Installation
Install `EFCore.ExprGenerator` from NuGet:

```
dotnet add package EFCore.ExprGenerator --prerelease
```

### Example
Use the `SelectExpr` method with an anonymous selector:

```csharp
var orders = await dbContext.Orders
    .SelectExpr(o => new
    {
        Id = o.Id,
        CustomerName = o.Customer?.Name,
        // ...
    })
    .ToListAsync();
```
