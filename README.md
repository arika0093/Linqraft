
<div align="center">
  <img width="150px" src="./assets/linqraft_512.png" />

  # Linqraft

  [![NuGet Version](https://img.shields.io/nuget/v/Linqraft?style=flat-square&logo=NuGet&color=0080CC)](https://www.nuget.org/packages/Linqraft/) ![GitHub Actions Workflow Status](https://img.shields.io/github/actions/workflow/status/arika0093/Linqraft/test.yaml?branch=main&label=Test&style=flat-square) [![DeepWiki](https://img.shields.io/badge/DeepWiki-Linqraft-blue.svg?logo=data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAACwAAAAyCAYAAAAnWDnqAAAAAXNSR0IArs4c6QAAA05JREFUaEPtmUtyEzEQhtWTQyQLHNak2AB7ZnyXZMEjXMGeK/AIi+QuHrMnbChYY7MIh8g01fJoopFb0uhhEqqcbWTp06/uv1saEDv4O3n3dV60RfP947Mm9/SQc0ICFQgzfc4CYZoTPAswgSJCCUJUnAAoRHOAUOcATwbmVLWdGoH//PB8mnKqScAhsD0kYP3j/Yt5LPQe2KvcXmGvRHcDnpxfL2zOYJ1mFwrryWTz0advv1Ut4CJgf5uhDuDj5eUcAUoahrdY/56ebRWeraTjMt/00Sh3UDtjgHtQNHwcRGOC98BJEAEymycmYcWwOprTgcB6VZ5JK5TAJ+fXGLBm3FDAmn6oPPjR4rKCAoJCal2eAiQp2x0vxTPB3ALO2CRkwmDy5WohzBDwSEFKRwPbknEggCPB/imwrycgxX2NzoMCHhPkDwqYMr9tRcP5qNrMZHkVnOjRMWwLCcr8ohBVb1OMjxLwGCvjTikrsBOiA6fNyCrm8V1rP93iVPpwaE+gO0SsWmPiXB+jikdf6SizrT5qKasx5j8ABbHpFTx+vFXp9EnYQmLx02h1QTTrl6eDqxLnGjporxl3NL3agEvXdT0WmEost648sQOYAeJS9Q7bfUVoMGnjo4AZdUMQku50McDcMWcBPvr0SzbTAFDfvJqwLzgxwATnCgnp4wDl6Aa+Ax283gghmj+vj7feE2KBBRMW3FzOpLOADl0Isb5587h/U4gGvkt5v60Z1VLG8BhYjbzRwyQZemwAd6cCR5/XFWLYZRIMpX39AR0tjaGGiGzLVyhse5C9RKC6ai42ppWPKiBagOvaYk8lO7DajerabOZP46Lby5wKjw1HCRx7p9sVMOWGzb/vA1hwiWc6jm3MvQDTogQkiqIhJV0nBQBTU+3okKCFDy9WwferkHjtxib7t3xIUQtHxnIwtx4mpg26/HfwVNVDb4oI9RHmx5WGelRVlrtiw43zboCLaxv46AZeB3IlTkwouebTr1y2NjSpHz68WNFjHvupy3q8TFn3Hos2IAk4Ju5dCo8B3wP7VPr/FGaKiG+T+v+TQqIrOqMTL1VdWV1DdmcbO8KXBz6esmYWYKPwDL5b5FA1a0hwapHiom0r/cKaoqr+27/XcrS5UwSMbQAAAABJRU5ErkJggg==)](https://deepwiki.com/arika0093/Linqraft)

  *Write projections easily with **on-demand DTO** generation*.
</div>

## Features
### Overview
Linqraft is a Roslyn Source Generator for easily writing `IQueryable` projections.

* **Query-based** automatic DTO generation
  * You can freely define DTO structures in the query without predefining them.
  * Based on anonymous types, "what you see is what you get" declarations.
  * Supports nested DTOs, collections, and calculated fields.
* Null-propagation operator support (`?.`) in Expression Trees
  * No more need to write `o.Customer != null ? o.Customer.Name : null`.
* Explicit projection hooks
  * `AsLeftJoin()` and `AsProjectable()` can opt a selector fragment into a generated rewrite.
* Zero-dependency
  * No runtime dependencies are required since it uses Source Generators and Interceptors.

With Linqraft, you can write queries like this:

```csharp
var orders = await dbContext.Orders
    // Order: input entity type
    // OrderDto: output DTO type (auto-generated)
    .SelectExpr<Order, OrderDto>(o => new
    {
        // can use inferred member names
        o.Id,
        // null-propagation supported
        // you can create flattened structures easily
        CustomerName = o.Customer?.Name,
        // also works for nested objects
        CustomerCountry = o.Customer?.Address?.Country?.Name,
        CustomerCity = o.Customer?.Address?.City?.Name,
        // you can use anonymous types inside. great for grouping
        CustomerInfo = new
        {
            Email = o.Customer?.EmailAddress,
            Phone = o.Customer?.PhoneNumber,
        },
        // calculated fields? no problem!
        LatestOrderDate = o.OrderItems.Max(oi => oi.OrderDate),
        TotalAmount = o.OrderItems.Sum(oi => oi.Quantity * oi.UnitPrice),
        // collections available
        Items = o.OrderItems.Select(oi => new
        {
            // of course, features work here too
            ProductName = oi.Product?.Name,
            oi.Quantity
        }),
    })
    .ToListAsync();
```

By specifying `OrderDto` as the generic parameter for `SelectExpr`, DTO types are generated **automatically** from the anonymous-type selector.
That means **you don't need to manually declare** `OrderDto` or `OrderItemDto`. 

for example, the generated code looks like this:

<details>
<summary>Generated code example</summary>

```csharp
using System.Linq;
namespace Linqraft
{
    internal static partial class SelectExprExtensions
    {
        [global::System.Runtime.CompilerServices.InterceptsLocationAttribute(1, "zoTX6YMzp8cIf98imvJzs8YBAABUdXRvcmlhbENhc2VUZXN0LmNz")]
        public static global::System.Linq.IQueryable<TResult> SelectExpr_55AF867EC668046B<TIn, TResult>(this global::System.Linq.IQueryable<TIn> query, global::System.Func<TIn, object> selector) where TIn : class
        {
            var converted = ((global::System.Linq.IQueryable<global::Tutorial.Order>)(object)query).Select(o => new global::Tutorial.OrderDto() {
                Id = o.Id,
                CustomerName = o.Customer != null ? (global::System.String?)(o.Customer.Name) : null,
                CustomerCountry = o.Customer != null && o.Customer.Address != null && o.Customer.Address.Country != null ? (global::System.String?)(o.Customer.Address.Country.Name) : null,
                CustomerCity = o.Customer != null && o.Customer.Address != null && o.Customer.Address.City != null ? (global::System.String?)(o.Customer.Address.City.Name) : null,
                CustomerInfo = new global::Tutorial.LinqraftGenerated_2B64B4DD.CustomerInfoDto {
                    Email = o.Customer != null ? (global::System.String?)(o.Customer.EmailAddress) : null,
                    Phone = o.Customer != null ? (global::System.String?)(o.Customer.PhoneNumber) : null,
                },
                LatestOrderDate = global::System.Linq.Enumerable.Max(
                    o.OrderItems,
                    oi => oi.OrderDate
                ),
                TotalAmount = global::System.Linq.Enumerable.Sum(
                    o.OrderItems,
                    oi => oi.Quantity * oi.UnitPrice
                ),
                Items = global::System.Linq.Enumerable.Select(
                    o.OrderItems,
                    oi => new global::Tutorial.LinqraftGenerated_67EDED21.ItemsDto {
                        ProductName = oi.Product != null ? (global::System.String?)(oi.Product.Name) : null,
                        Quantity = oi.Quantity,
                    }
                ),
            });
            return (global::System.Linq.IQueryable<TResult>)(object)converted;
        }
    }
}

namespace Tutorial
{
    public partial class OrderDto
    {
        public required global::System.Int32 Id { get; set; }
        public required global::System.String? CustomerName { get; set; }
        public required global::System.String? CustomerCountry { get; set; }
        public required global::System.String? CustomerCity { get; set; }
        public required global::Tutorial.LinqraftGenerated_2B64B4DD.CustomerInfoDto CustomerInfo { get; set; }
        public required global::System.DateTime LatestOrderDate { get; set; }
        public required global::System.Decimal TotalAmount { get; set; }
        public required global::System.Collections.Generic.IEnumerable<global::Tutorial.LinqraftGenerated_67EDED21.ItemsDto> Items { get; set; }
    }
}

namespace Tutorial.LinqraftGenerated_2B64B4DD
{
    [global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]
    [global::Linqraft.LinqraftAutoGeneratedDtoAttribute]
    public partial class CustomerInfoDto
    {
        public required global::System.String? Email { get; set; }
        public required global::System.String? Phone { get; set; }
    }
}

namespace Tutorial.LinqraftGenerated_67EDED21
{
    [global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]
    [global::Linqraft.LinqraftAutoGeneratedDtoAttribute]
    public partial class ItemsDto
    {
        public required global::System.String? ProductName { get; set; }
        public required global::System.Int32 Quantity { get; set; }
    }
}
```

</details>

### Drop-in Replacement Analyzers

[Analyzers](./docs/analyzers/README.md) are provided to replace existing Select code with Linqraft. The replacement is completed in an instant.

### Projection Hooks

Linqraft also supports explicit projection hooks for cases where you want the generator to rewrite part of a selector body:

```csharp
var rows = dbContext.Orders
    .SelectExpr(order => new
    {
        CustomerName = order.Customer.AsLeftJoin().Name,
        FirstLargeItem = order.FirstLargeItemProductName.AsProjectable(),
    });
```

- `AsLeftJoin()` rewrites a nullable navigation access as an explicit null-guarded access.
- `AsProjectable()` inlines a computed instance property or method into the generated projection.

See [Projection Hooks](./docs/library/projection-hooks.md) for details and constraints.

## Quick Start
### Single File Example

For .NET 10 and later, simply create the following code as `sample.cs` and run `dotnet sample.cs`.

<details>
<summary>Sample code (sample.cs)</summary>

```csharp
#:package Linqraft@*
#:property NoWarn=$(NoWarn);IL2026;IL3050

using Linqraft;

var result = SampleData
    .Orders.SelectExpr<SampleOrder, SampleOrderRowDto>(order => new
    {
        order.Id,
        CustomerName = order.Customer?.Name,
        CustomerCountry = order.Customer?.Address?.Country,
        TotalQuantity = order.Items.Sum(item => item.Quantity),
        Items = order.Items.Select(item => new
        {
            item.ProductName,
            item.Quantity,
            IsRecent = item.ShipDate >= new DateOnly(2026, 2, 15),
        }),
    })
    .ToList();

foreach (var row in result)
{
    Console.WriteLine($"Order {row.Id} for {row.CustomerName} from {row.CustomerCountry}");
    foreach (var item in row.Items)
    {
        Console.WriteLine(
            $"  - {item.ProductName}: Quantity = {item.Quantity}, IsRecent = {item.IsRecent}"
        );
    }
}

// -- declaration of sample data classes --
public sealed class SampleOrder
{
    public int Id { get; set; }
    public SampleCustomer? Customer { get; set; }
    public List<SampleOrderItem> Items { get; set; } = [];
}

public sealed class SampleCustomer
{
    public string Name { get; set; } = string.Empty;
    public SampleAddress? Address { get; set; }
}

public sealed class SampleAddress
{
    public string Country { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
}

public sealed class SampleOrderItem
{
    public string ProductName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public DateOnly ShipDate { get; set; }
}

public static class SampleData
{
    public static IQueryable<SampleOrder> Orders => _orders.AsQueryable();
    private static List<SampleOrder> _orders =>
        [
            new()
            {
                Id = 1001,
                Customer = new SampleCustomer
                {
                    Name = "Ada",
                    Address = new SampleAddress { Country = "UK", City = "London" },
                },
                Items =
                [
                    new()
                    {
                        ProductName = "Keyboard",
                        Quantity = 1,
                        ShipDate = new DateOnly(2026, 3, 15),
                    },
                    new()
                    {
                        ProductName = "Mouse",
                        Quantity = 2,
                        ShipDate = new DateOnly(2026, 2, 25),
                    },
                ],
            },
            new()
            {
                Id = 1002,
                Customer = new SampleCustomer
                {
                    Name = "Grace",
                    Address = new SampleAddress { Country = "US", City = "New York" },
                },
                Items =
                [
                    new()
                    {
                        ProductName = "Monitor",
                        Quantity = 1,
                        ShipDate = new DateOnly(2026, 1, 20),
                    },
                ],
            },
        ];
}
```

</details>


### Prerequisites
Linqraft works out of the box with **.NET 8 and later**. For .NET 7(C# 11) and below, additional setup is required.  
For detailed installation instructions, see the [Installation Guide](./docs/library/installation.md).

### Installation

Install `Linqraft` from NuGet:

```bash
dotnet add package Linqraft
```

## Basic Usage

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

Specify the generics to generate a DTO class:

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

### Pre-existing DTO pattern

Use your existing DTO classes:

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

### 1:1 DTO pattern

1:1 DTO generation for non-IEnumerable/IQueryable scenarios is also supported.

```csharp
var myDto = LinqraftKit.Generate<MyDto>(
    o => new MyDto
    {
        Foo = foo,
        Bar = bar,
    },
    capture: () => (foo, bar)
);
```


For more usage patterns and examples, see the [Usage Patterns Guide](./docs/library/usage-patterns.md).

## Documentation

### Getting Started

* **[Installation](./docs/library/installation.md)** - Prerequisites, installation, and setup
* **[Usage Patterns](./docs/library/usage-patterns.md)** - Anonymous, Explicit DTO, and Pre-existing DTO patterns

### Usage Guides

* **[Local Variable Capture](./docs/library/local-variable-capture.md)** - Using local variables in SelectExpr
* **[Array Nullability Removal](./docs/library/array-nullability.md)** - Automatic null handling for collections
* **[Partial Classes](./docs/library/partial-classes.md)** - Extending generated DTOs
* **[Nested DTO Naming](./docs/library/nested-dto-naming.md)** - Customizing nested DTO names
* **[Nested SelectExpr](./docs/library/nested-selectexpr.md) (beta)** - Using nested SelectExpr for reusable DTOs
* **[Mapping Methods](./docs/library/mapping-methods.md) (beta)** - Generating mapping methods to class instead of interceptors
* **[Auto-Generated Comments](./docs/library/auto-generated-comments.md)** - XML documentation generation
* **[Global Properties](./docs/library/global-properties.md)** - MSBuild configuration options

### Performance & Comparison

* **[Performance](./docs/library/performance.md)** - Benchmarks
* **[Library Comparison](./docs/library/library-comparison.md)** - Comparisons with AutoMapper, Mapster, Mapperly, and Facet

### Help

* **[FAQ](./docs/library/faq.md)** - Common questions
* **[Analyzers](./docs/analyzers/README.md)** - Code analyzers and fixes

## Examples

Example projects are available in the [examples](./examples) folder:

* [Linqraft.Sample](./examples/Linqraft.Sample) - Basic usage examples (with EFCore)
* [Linqraft.MinimumSample](./examples/Linqraft.MinimumSample) - Minimal working example
* [Linqraft.ApiSample](./examples/Linqraft.ApiSample) - API integration example

## License
This project is licensed under the Apache License 2.0.
