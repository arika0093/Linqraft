
# <img width="24" src="./assets/linqraft.png" /> Linqraft 

[![NuGet Version](https://img.shields.io/nuget/v/Linqraft?style=flat-square&logo=NuGet&color=0080CC)](https://www.nuget.org/packages/Linqraft/) ![GitHub Actions Workflow Status](https://img.shields.io/github/actions/workflow/status/arika0093/Linqraft/test.yaml?branch=main&label=Test&style=flat-square) [![DeepWiki](https://img.shields.io/badge/DeepWiki-arika0093%2FLinqraft-blue.svg?logo=data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAACwAAAAyCAYAAAAnWDnqAAAAAXNSR0IArs4c6QAAA05JREFUaEPtmUtyEzEQhtWTQyQLHNak2AB7ZnyXZMEjXMGeK/AIi+QuHrMnbChYY7MIh8g01fJoopFb0uhhEqqcbWTp06/uv1saEDv4O3n3dV60RfP947Mm9/SQc0ICFQgzfc4CYZoTPAswgSJCCUJUnAAoRHOAUOcATwbmVLWdGoH//PB8mnKqScAhsD0kYP3j/Yt5LPQe2KvcXmGvRHcDnpxfL2zOYJ1mFwrryWTz0advv1Ut4CJgf5uhDuDj5eUcAUoahrdY/56ebRWeraTjMt/00Sh3UDtjgHtQNHwcRGOC98BJEAEymycmYcWwOprTgcB6VZ5JK5TAJ+fXGLBm3FDAmn6oPPjR4rKCAoJCal2eAiQp2x0vxTPB3ALO2CRkwmDy5WohzBDwSEFKRwPbknEggCPB/imwrycgxX2NzoMCHhPkDwqYMr9tRcP5qNrMZHkVnOjRMWwLCcr8ohBVb1OMjxLwGCvjTikrsBOiA6fNyCrm8V1rP93iVPpwaE+gO0SsWmPiXB+jikdf6SizrT5qKasx5j8ABbHpFTx+vFXp9EnYQmLx02h1QTTrl6eDqxLnGjporxl3NL3agEvXdT0WmEost648sQOYAeJS9Q7bfUVoMGnjo4AZdUMQku50McDcMWcBPvr0SzbTAFDfvJqwLzgxwATnCgnp4wDl6Aa+Ax283gghmj+vj7feE2KBBRMW3FzOpLOADl0Isb5587h/U4gGvkt5v60Z1VLG8BhYjbzRwyQZemwAd6cCR5/XFWLYZRIMpX39AR0tjaGGiGzLVyhse5C9RKC6ai42ppWPKiBagOvaYk8lO7DajerabOZP46Lby5wKjw1HCRx7p9sVMOWGzb/vA1hwiWc6jm3MvQDTogQkiqIhJV0nBQBTU+3okKCFDy9WwferkHjtxib7t3xIUQtHxnIwtx4mpg26/HfwVNVDb4oI9RHmx5WGelRVlrtiw43zboCLaxv46AZeB3IlTkwouebTr1y2NjSpHz68WNFjHvupy3q8TFn3Hos2IAk4Ju5dCo8B3wP7VPr/FGaKiG+T+v+TQqIrOqMTL1VdWV1DdmcbO8KXBz6esmYWYKPwDL5b5FA1a0hwapHiom0r/cKaoqr+27/XcrS5UwSMbQAAAABJRU5ErkJggg==)](https://deepwiki.com/arika0093/Linqraft)

Simplifies Select query expressions for EntityFrameworkCore (EF Core) by providing automatic DTO generation and support for null-propagating expressions.

[English](./README.md) | [Japanese](./README.ja.md)

## Problem

When querying a table that has many related tables, using `Include` / `ThenInclude` quickly makes code hard to read and maintain.

- The Include-based style becomes verbose and hard to follow.
- Forgetting an `Include` can lead to runtime `NullReferenceException`s that are hard to detect at compile time.
- Fetching entire object graphs is often wasteful and hurts performance.

```csharp
// ⚠️ unreadable, inefficient, and error-prone
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

A better approach is to project into DTOs and select only the fields you need:

```csharp
// ✅️ readable and efficient
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

This yields better performance because only the required data is fetched. But this style has drawbacks:

- If you want to pass the result to other methods or return it from APIs, you usually must define DTO classes manually.
- When child objects can be null, the expression APIs don't support the `?.` operator directly, forcing verbose null checks using ternary operators.

Because null-propagation isn't supported inside expression trees, code often becomes verbose:

```csharp
// 🤔 too ugly code with lots of null checks
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
        Items = o.OrderItems.Select(oi => new OrderItemDto
        {
            ProductName = oi.Product != null ? oi.Product.Name : null,
            Quantity = oi.Quantity
        }).ToList()
    })
    .ToListAsync();

// 🤔 you must define DTO classes manually
public class OrderDto
{
    public int Id { get; set; }
    public string? CustomerName { get; set; }
    public string? CustomerCountry { get; set; }
    public string? CustomerCity { get; set; }
    public List<OrderItemDto> Items { get; set; } = [];
}
public class OrderItemDto
{
    public string? ProductName { get; set; }
    public int Quantity { get; set; }
}
```

## Features

Linqraft is a Roslyn Source Generator that addresses the problems above. Using this library you can write concise selectors with null-propagation and optionally generate DTO classes automatically.

```csharp
using Linqraft;

// ✨️ auto-generated DTOs, with null-propagation support
var orders = await dbContext.Orders
    // Order: input entity type
    // OrderDto: output DTO type (auto-generated)
    .SelectExpr<Order, OrderDto>(o => new
    {
        Id = o.Id,
        CustomerName = o.Customer?.Name,
        CustomerCountry = o.Customer?.Address?.Country?.Name,
        CustomerCity = o.Customer?.Address?.City?.Name,
        Items = o.OrderItems.Select(oi => new
        {
            ProductName = oi.Product?.Name,
            Quantity = oi.Quantity
        }).ToList(),
    })
    .ToListAsync();
```

By specifying `OrderDto` as the generic parameter for `SelectExpr`, DTO types are generated automatically from the anonymous-type selector. That means you don't need to manually declare `OrderDto` or `OrderItemDto`.
for example, the generated code looks like this:

<details>
<summary>Generated code example</summary>

```csharp
// <auto-generated />
#nullable enable
#pragma warning disable IDE0060
#pragma warning disable CS8601
#pragma warning disable CS8603
#pragma warning disable CS8604

using System;
using System.Linq;
using System.Collections.Generic;
using Tutorial;

namespace Linqraft
{
    file static partial class GeneratedExpression
    {
        [global::System.Runtime.CompilerServices.InterceptsLocationAttribute(1, "9IBuY2cLVnfIVhZ8DH1V8UkEAABUdXRvcmlhbENhc2VUZXN0LmNz")]
        public static IQueryable<TResult> SelectExpr_8C5CFBF9_FA0FABCE<TIn, TResult>(
            this IQueryable<TIn> query,
            Func<TIn, object> selector) where TResult : global::Tutorial.OrderDto
        {
            var matchedQuery = query as object as IQueryable<global::Tutorial.Order>;
            var converted = matchedQuery.Select(s => new global::Tutorial.OrderDto
            {
                Id = s.Id,
                CustomerName = s.Customer != null ? (string?)s.Customer.Name : null,
                CustomerCountry = s.Customer != null && s.Customer.Address != null && s.Customer.Address.Country != null ? (string?)s.Customer.Address.Country.Name : null,
                CustomerCity = s.Customer != null && s.Customer.Address != null && s.Customer.Address.City != null ? (string?)s.Customer.Address.City.Name : null,
                Items = s.OrderItems.Select(oi => new OrderItemDto_DE33EA40 {
                    ProductName = oi.Product != null ? (string?)oi.Product.Name : null,
                    Quantity = oi.Quantity,
                }).ToList()
            });
            return converted as object as IQueryable<TResult>;
        }

    }
}

namespace Tutorial
{
    internal partial class OrderItemDto_DE33EA40
    {
        public required string? ProductName { get; set; }
        public required int Quantity { get; set; }
    }

    internal partial class OrderDto
    {
        public required int Id { get; set; }
        public required string? CustomerName { get; set; }
        public required string? CustomerCountry { get; set; }
        public required string? CustomerCity { get; set; }
        public required global::System.Collections.Generic.List<Tutorial.OrderItemDto_DE33EA40> Items { get; set; }
    }
}
```

</details>

## Usage
### Prerequisites
This library uses [C# interceptors](https://learn.microsoft.com/en-us/dotnet/csharp/whats-new/csharp-12#interceptors) internally, so use **C# 12 or later**.  

<details>
<summary>.NET 7 or below setup</summary>

Set the `LangVersion` property and use [Polysharp](https://github.com/Sergio0694/PolySharp/) to enable C# latest features.

```xml
<Project>
    <PropertyGroup>
        <LangVersion>12.0</LangVersion>
    </PropertyGroup>
    <ItemGroup>
        <PackageReference Include="Polysharp" Version="1.*" />
    </ItemGroup>
</Project>
```

</details>

### Installation

Install `Linqraft` from NuGet:

```bash
dotnet add package Linqraft
```

## Examples
### Anonymous pattern

Use `SelectExpr` without generics to get an anonymous-type projection:

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

### Explicit DTO pattern
If you want to change the result to a DTO class, simply specify the generics as follows.

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

You can extend the generated DTO classes as needed since they are output as `partial` classes.

```csharp
// extend generated DTO class if needed
public partial class OrderDto
{
    public string GetDisplayName() => $"{Id}: {CustomerName}";
}
```

Similarly, you can use only the auto-generation feature by specifying `IEnumerable` types.

```csharp
var orders = MySampleList
    .SelectExpr<Order, OrderDto>(o => new
    {
        Id = o.Id,
        CustomerName = o.Customer?.Name,
        // ...
    })
    .ToList();
```

> [!TIP]
> If you want to use the auto-generated type information, you can navigate to the generated code (for example via F12 in your editor) by placing the cursor on the `OrderDto` class.
> and then you can copy it or use it as you like.


### Pre-existing DTO pattern
If you already have DTO classes and want to use them directly, call `SelectExpr` without generics and construct your DTO type in the selector:

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

## Performance

<details>
<summary>Benchmark Results</summary>

```
BenchmarkDotNet v0.15.7, Windows 11 (10.0.26200.7171/25H2/2025Update/HudsonValley2)
Intel Core i7-14700F 2.10GHz, 1 CPU, 28 logical and 20 physical cores
.NET SDK 10.0.100-rc.2.25502.107
  [Host]     : .NET 9.0.10 (9.0.10, 9.0.1025.47515), X64 RyuJIT x86-64-v3
  DefaultJob : .NET 9.0.10 (9.0.10, 9.0.1025.47515), X64 RyuJIT x86-64-v3

| Method                        | Mean       | Error    | StdDev   | Ratio | RatioSD | Rank | Gen0    | Gen1   | Allocated | Alloc Ratio |
|------------------------------ |-----------:|---------:|---------:|------:|--------:|-----:|--------:|-------:|----------:|------------:|
| 'Traditional Manual DTO'      |   962.2 us |  7.11 us |  6.65 us |  0.92 |    0.01 |    1 | 13.6719 | 1.9531 | 245.06 KB |        1.00 |
| 'Linqraft Auto-Generated DTO' |   968.6 us |  7.40 us |  6.92 us |  0.92 |    0.01 |    1 | 13.6719 | 1.9531 | 245.09 KB |        1.00 |
| 'Linqraft Anonymous'          | 1,030.7 us |  4.64 us |  4.34 us |  0.98 |    0.01 |    2 | 13.6719 | 1.9531 | 244.92 KB |        1.00 |
| 'Traditional Anonymous'       | 1,047.7 us | 16.51 us | 15.44 us |  1.00 |    0.02 |    2 | 13.6719 | 1.9531 | 246.14 KB |        1.00 |
```

</details>

Compared to the manual approach, the performance is nearly identical.
for more details, see [Linqraft.Benchmark](./examples/Linqraft.Benchmark) for details.

## Troubleshooting
### CS8072 Error
Sometimes, immediately building after changes may result in error `CS8072` (null-propagation operator cannot be used in expression tree lambdas).
In such cases, rebuilding the project resolves the issue.
If the issue persists, it may be due to null-propagation operators being incorrectly included in the generated source code. In that case, please feel free to open an issue!

## Notes
The translation of anonymous-type selectors into Select-compatible expressions is done by source generation and some heuristics. Very complex expressions or certain C# constructs may not be supported. If you encounter unsupported cases, please open an issue with a minimal repro.

## License
This project is licensed under the Apache License 2.0.
