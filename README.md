# EFCore.ExprGenerator

Simplifies the writing of Select queries in EntityFrameworkCore (EFCore) by providing automatic DTO class generation and support for nullable expressions.

[English](./README.md) | [Japanese](./README.ja.md)

## Problem
Consider an example of retrieving data from a table with many related tables in EFCore.

Using `Include` and `ThenInclude` quickly makes the code complex and reduces readability.  
Additionally, forgetting to include a related table can result in a `NullReferenceException` at runtime, which is difficult to detect.  
Furthermore, retrieving all data can lead to performance issues.

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

A more ideal approach is to use DTOs (Data Transfer Objects) to selectively retrieve only the necessary data.

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

This method has significant performance advantages as it retrieves only the necessary data. However, it has the following drawbacks:

* While anonymous types can be used, you need to manually define DTO classes if you want to pass them to other functions or use them as return values.
* If there are child objects with nullable properties, you need to write verbose code using ternary operators.

Due to the nature of expressions, nullable operators cannot be used within them, so you cannot write something like `o.Customer?.Name`. Instead, the code tends to look like this:

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
EFCore.ExprGenerator is a Source Generator designed to solve the above problems.  
In the example above, you can write the following:

```csharp
var orders = await dbContext.Orders
    // Order: input entity type
    // OrderDto: output DTO type (auto-generated)
    .SelectExpr<Order, OrderDto>(o => new
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

By specifying `OrderDto` as the generic argument for `SelectExpr`, the related DTO classes are automatically generated.  
Since the code is automatically generated from the anonymous type selector, there is no need to manually define `OrderDto` or `OrderItemDto`.  
For example, the following methods and classes are generated:

<details>
<summary>Generated Code Example</summary>

```csharp
// TODO
```

</details>

## Usage
### Installation
Install `EFCore.ExprGenerator` from NuGet.

```
dotnet add package EFCore.ExprGenerator --prerelease
```

Then, enable the interceptor in your csproj file.

```xml
<Project>
  <PropertyGroup>
    <!-- add EFCore.ExprGenerator to the InterceptorsPreviewNamespaces -->
    <InterceptorsPreviewNamespaces>$(InterceptorsPreviewNamespaces);EFCore.ExprGenerator</InterceptorsPreviewNamespaces>
  </PropertyGroup>
</Project>
```

### Example Usage
Use the `SelectExpr` method as follows:

```csharp
var orders = await dbContext.Orders
    // Order: input entity type
    // OrderDto: output DTO type (auto-generated)
    .SelectExpr<Order, OrderDto>(o => new
    {
        Id = o.Id,
        CustomerName = o.Customer?.Name,
        // ...
    })
    .ToListAsync();
```

It is also possible to use an existing DTO class without using the auto-generation feature. In this case, you need to use it without specifying the generic argument.

```csharp
var orders = await dbContext.Orders
    .SelectExpr(o => new OrderDto
    {
        Id = o.Id,
        CustomerName = o.Customer?.Name,
        // ...
    })
    .ToListAsync();

// your existing DTO class
public class OrderDto { /* ... */ }
```

If you pass an anonymous type without specifying generics, the anonymous type is returned as is.

```csharp
var orders = await dbContext.Orders
    .SelectExpr(o => new
    {
        Id = o.Id,
        CustomerName = o.Customer?.Name,
        // ...
    })
    .ToListAsync();
var firstOrder = orders.First();
Console.WriteLine(firstOrder.GetType().Name); // -> anonymous type
```

## License
This project is licensed under the Apache License 2.0.
