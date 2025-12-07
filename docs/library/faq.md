# Frequently Asked Questions

Common questions and answers about Linqraft.

## General Questions

### Does Linqraft only work with Entity Framework?

No. Linqraft works with any LINQ provider that supports `IEnumerable<T>` and/or `IQueryable<T>`.

**Works with:**
* Entity Framework Core
* Entity Framework 6
* LINQ to SQL
* In-memory collections (`List<T>`, `Array`, etc.)
* Any custom LINQ provider

**Examples:**

```csharp
// Entity Framework Core
var orders = await dbContext.Orders
    .SelectExpr<Order, OrderDto>(o => new { o.Id, o.CustomerName })
    .ToListAsync();

// In-memory collection
var orders = myList
    .SelectExpr<Order, OrderDto>(o => new { o.Id, o.CustomerName })
    .ToList();

// LINQ to Objects
var filtered = items
    .Where(x => x.IsActive)
    .SelectExpr<Item, ItemDto>(x => new { x.Id, x.Name })
    .ToArray();
```

### Can the generated DTOs be used elsewhere?

**Yes**, generated DTOs are regular C# classes and can be used anywhere:

* API response models
* Swagger/OpenAPI documentation
* Function return types
* Method parameters
* Variables
* Serialization/deserialization
* Unit tests

**Example:**

```csharp
// API controller
[HttpGet]
public async Task<ActionResult<List<OrderDto>>> GetOrders()
{
    var orders = await dbContext.Orders
        .SelectExpr<Order, OrderDto>(o => new { o.Id, o.CustomerName })
        .ToListAsync();

    return Ok(orders); // OrderDto serialized to JSON
}

// Service method
public class OrderService
{
    public List<OrderDto> GetRecentOrders()
    {
        return dbContext.Orders
            .Where(o => o.CreatedAt > DateTime.Now.AddDays(-7))
            .SelectExpr<Order, OrderDto>(o => new { /* ... */ })
            .ToList();
    }
}

// Unit test
[Fact]
public void OrderDto_Should_Have_Correct_Properties()
{
    var dto = new OrderDto
    {
        Id = 1,
        CustomerName = "Test Customer"
    };

    Assert.Equal(1, dto.Id);
    Assert.Equal("Test Customer", dto.CustomerName);
}
```

For a complete API example, see [Linqraft.ApiSample](../../examples/Linqraft.ApiSample).

### Can I access the generated code?

**Yes**, there are two ways to view generated code:

#### Method 1: Go to Definition (F12)

Place your cursor on the generated DTO class name and press F12 (or "Go to Definition" in your IDE).

```csharp
var orders = dbContext.Orders
    .SelectExpr<Order, OrderDto>(o => new { /* ... */ })
    //                  ^^^^^^^^
    //                  F12 here
    .ToList();
```

Your IDE will navigate to the generated source code.

#### Method 2: Output to Files

Add these settings to your `.csproj` file:

```xml
<Project>
  <PropertyGroup>
    <!-- Enable emitting generated files -->
    <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>

    <!-- Output path for generated files -->
    <CompilerGeneratedFilesOutputPath>Generated</CompilerGeneratedFilesOutputPath>
  </PropertyGroup>
</Project>
```

After building, generated files will be in the `Generated/` folder:

```
YourProject/
├── Generated/
│   ├── Linqraft.SourceGenerator/
│   │   ├── SelectExpr_HASH1.g.cs
│   │   ├── SelectExpr_HASH2.g.cs
│   │   └── ...
├── Program.cs
└── YourProject.csproj
```

## Getting Help

### Where can I report bugs or request features?

Please use the GitHub issue tracker:
https://github.com/arika0093/Linqraft/issues

Include:
* Your `SelectExpr` usage code
* Expected behavior
* Actual behavior
* Generated code (if relevant)

### Where can I find examples?

* [Linqraft.Sample](../../examples/Linqraft.Sample) - Basic usage examples
* [Linqraft.ApiSample](../../examples/Linqraft.ApiSample) - API integration example
* [Linqraft.Benchmark](../../examples/Linqraft.Benchmark) - Performance benchmarks
* [Online Playground](https://arika0093.github.io/Linqraft/playground/) - Try it in your browser

## Next Steps

* [Installation](./installation.md) - Get started with Linqraft
* [Usage Patterns](./usage-patterns.md) - Learn different usage patterns
* [Customization](./customization.md) - Customize generated code
* [Performance](./performance.md) - Performance benchmarks and comparisons
