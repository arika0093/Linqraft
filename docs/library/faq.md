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

## Usage Questions

### How do I use local variables in SelectExpr?

Local variables cannot be used directly because `SelectExpr` is translated into a separate method. Use the `capture` parameter:

```csharp
var threshold = 100;
var multiplier = 2;

var converted = dbContext.Entities
    .SelectExpr<Entity, EntityDto>(
        x => new {
            x.Id,
            IsExpensive = x.Price > threshold,
            DoubledValue = x.Value * multiplier,
        },
        capture: new { threshold, multiplier } // Pass variables here
    );
```

Linqraft provides an analyzer that detects uncaptured variables and suggests a code fix automatically.

See [Customization - Local Variable Capture](./customization.md#local-variable-capture) for details.

### Can I use async methods in SelectExpr?

**No**, `SelectExpr` generates Expression Trees which don't support async/await.

This is a limitation of Expression Trees in general, not specific to Linqraft.

**Instead:**
1. Load data first with `SelectExpr`
2. Then perform async operations in memory

```csharp
// ❌ Won't work
var orders = await dbContext.Orders
    .SelectExpr<Order, OrderDto>(async o => new
    {
        o.Id,
        Data = await SomeAsyncMethod(o.Id) // ERROR
    })
    .ToListAsync();

// ✅ Load data first, then process
var orders = await dbContext.Orders
    .SelectExpr<Order, OrderDto>(o => new { o.Id })
    .ToListAsync();

foreach (var order in orders)
{
    order.Data = await SomeAsyncMethod(order.Id);
}
```

### Why can't I use `var` for the generic parameters?

C# doesn't support type inference for method generic parameters when using lambda expressions.

```csharp
// ❌ Won't work - can't infer types
var orders = dbContext.Orders
    .SelectExpr(o => new { o.Id });

// ✅ Anonymous pattern - no generics needed
var orders = dbContext.Orders
    .SelectExpr(o => new { o.Id });

// ✅ Explicit DTO pattern - specify types
var orders = dbContext.Orders
    .SelectExpr<Order, OrderDto>(o => new { o.Id });
```

Use the [anonymous pattern](./usage-patterns.md#anonymous-pattern) if you don't need a named type.

### Can I use SelectExpr with GroupBy?

**Yes**, but you need to use the explicit DTO pattern:

```csharp
var grouped = await dbContext.Orders
    .GroupBy(o => o.CustomerId)
    .SelectExpr<IGrouping<int, Order>, CustomerOrderSummaryDto>(g => new
    {
        CustomerId = g.Key,
        OrderCount = g.Count(),
        TotalAmount = g.Sum(o => o.TotalAmount),
    })
    .ToListAsync();
```

### Can I use SelectExpr multiple times in a chain?

**Yes**, you can chain multiple `SelectExpr` calls:

```csharp
var result = dbContext.Orders
    .SelectExpr<Order, OrderDto>(o => new
    {
        o.Id,
        o.CustomerName,
        TotalAmount = o.OrderItems.Sum(oi => oi.UnitPrice * oi.Quantity),
    })
    .Where(dto => dto.TotalAmount > 100)
    .SelectExpr<OrderDto, OrderSummaryDto>(dto => new
    {
        dto.Id,
        dto.CustomerName,
    })
    .ToList();
```

However, for database queries, it's usually better to combine operations in a single `SelectExpr` for better SQL generation.

## Customization Questions

### Can I make some DTO properties internal?

**Yes**, pre-define the property in a partial class:

```csharp
// Pre-define the property with desired accessibility
public partial class OrderDto
{
    internal string InternalData { get; set; }
}

// Use SelectExpr - the property won't be generated
var orders = dbContext.Orders
    .SelectExpr<Order, OrderDto>(o => new
    {
        o.Id,
        InternalData = o.InternalField, // Uses your pre-defined property
    })
    .ToList();
```

See [Customization - Property Accessibility Control](./customization.md#property-accessibility-control) for details.

### Can I generate records instead of classes?

**Yes**, set the `LinqraftRecordGenerate` property:

```xml
<PropertyGroup>
  <LinqraftRecordGenerate>true</LinqraftRecordGenerate>
</PropertyGroup>
```

This will generate records instead of classes:

```csharp
// Generated as record
public partial record OrderDto
{
    public required int Id { get; init; }
    public required string CustomerName { get; init; }
}
```

### Can I disable the `required` keyword?

**Yes**, set the `LinqraftHasRequired` property:

```xml
<PropertyGroup>
  <LinqraftHasRequired>false</LinqraftHasRequired>
</PropertyGroup>
```

Generated DTOs will not have the `required` keyword:

```csharp
public partial class OrderDto
{
    public int Id { get; set; } // No 'required'
    public string CustomerName { get; set; }
}
```

### How do I disable null removal for arrays?

By default, Linqraft removes nullability from array-type properties for convenience. To disable:

```xml
<PropertyGroup>
  <LinqraftArrayNullabilityRemoval>false</LinqraftArrayNullabilityRemoval>
</PropertyGroup>
```

See [Customization - Array Nullability Removal](./customization.md#array-nullability-removal) for details.

## Troubleshooting

### The generated code is not visible

Try these steps:

1. **Clean and rebuild**
   ```bash
   dotnet clean
   dotnet build --no-incremental
   ```

2. **Restart your IDE**
   Sometimes Visual Studio or Rider needs a restart to recognize generated code.

3. **Check SDK version**
   Ensure you have .NET 8.0.400 SDK or later:
   ```bash
   dotnet --version
   ```

4. **Enable file output** (for debugging)
   ```xml
   <PropertyGroup>
     <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
     <CompilerGeneratedFilesOutputPath>Generated</CompilerGeneratedFilesOutputPath>
   </PropertyGroup>
   ```

### Build errors: "Interceptor location not found"

This usually means the source generator didn't run. Try:

1. Clean and rebuild:
   ```bash
   dotnet clean
   dotnet build --no-incremental
   ```

2. Ensure C# 12.0 is enabled:
   ```xml
   <PropertyGroup>
     <LangVersion>12.0</LangVersion>
   </PropertyGroup>
   ```

3. Check that you're using .NET 8.0.400 SDK or later

### IntelliSense doesn't recognize generated DTOs

This is usually a temporary IDE issue. Try:

1. Rebuild the project: `dotnet build`
2. Restart your IDE
3. Close and reopen the solution

### Generated code has compilation errors

This might indicate a bug in Linqraft. Please:

1. Check the generated code (F12 or output to files)
2. Report the issue at https://github.com/arika0093/Linqraft/issues
3. Include:
   * Your `SelectExpr` usage
   * The generated code
   * Error messages

## Performance Questions

### Is Linqraft slower than manual Select?

**No**. Linqraft generates the same code as you would write manually. Performance is identical.

See the [Performance benchmarks](./performance.md#performance-benchmarks) for detailed comparisons.

### Does Linqraft add runtime dependencies?

**No**. Linqraft is a compile-time-only tool:

* Uses Source Generators (compile-time)
* Uses Interceptors (compile-time)
* Zero runtime dependencies
* No DLLs loaded at runtime

Your deployed application has no Linqraft dependencies.

### Does it work with AOT (Native AOT)?

**Yes**, Linqraft generates regular C# code that is fully compatible with Native AOT compilation.

Since all code is generated at compile-time and doesn't use reflection or runtime code generation, it works seamlessly with AOT.

## Compatibility Questions

### What versions of .NET are supported?

**SDK:** .NET 8.0.400 or later (for compilation)

**Runtime:** Any .NET version that supports C# 12.0:
* .NET 8.0+
* .NET 7.0 (with `LangVersion` set to 12.0 and PolySharp)
* .NET 6.0 (with `LangVersion` set to 12.0 and PolySharp)

See [Installation - Prerequisites](./installation.md#prerequisites) for details.

### Can I use Linqraft with .NET Framework?

**Not recommended**. While technically possible with:
* C# 12.0 language features
* PolySharp for missing APIs

It's easier and better to use .NET 6+ which has native support.

### Does it work with Blazor/MAUI/Unity?

**Yes**, Linqraft works with:
* Blazor (Server and WebAssembly)
* MAUI
* WPF
* WinForms
* Console applications
* Any .NET application that supports C# 12.0

**Unity:** Not officially tested, but should work if Unity's C# version supports C# 12.0.

## Advanced Questions

### Can I convert DTOs back to entities?

**No**, reverse mapping is not supported. This is an intentional design decision because:

1. **Query-based generation creates ambiguity**
   ```csharp
   // How would you reverse this?
   TotalAmount = o.OrderItems.Sum(oi => oi.Quantity * oi.UnitPrice)
   ```

2. **Flattened structures lose information**
   ```csharp
   // Where would this go in the entity?
   CustomerName = o.Customer?.Name
   ```

3. **Computed fields can't be reversed**

**Alternative:** Use Linqraft for read operations and EF Core for writes:

```csharp
// Read with Linqraft
var orders = dbContext.Orders
    .SelectExpr<Order, OrderDto>(o => new { /* ... */ })
    .ToList();

// Write with EF Core
var newOrder = new Order
{
    CustomerId = dto.CustomerId,
    // ... map properties manually
};
dbContext.Orders.Add(newOrder);
await dbContext.SaveChangesAsync();
```

### Can I share generated DTOs across projects?

Query-based generation makes this challenging because DTOs are tied to the queries where they're defined.

**Recommended approaches:**

1. **Use Linqraft in the API layer** and generate shared types from OpenAPI schema:
   ```
   API Project (uses Linqraft)
      ↓ generates OpenAPI
   Shared DTOs (generated from OpenAPI schema)
      ↓ used by
   Client Projects
   ```

2. **Use Pre-existing DTO pattern** for shared types:
   ```csharp
   // In shared project
   public class OrderDto { /* ... */ }

   // In API project
   var orders = dbContext.Orders
       .SelectExpr(o => new OrderDto { /* ... */ })
       .ToList();
   ```

3. **Copy generated DTOs** to a shared project (not recommended for frequent changes)

### How are nested DTO class names generated?

Nested DTOs (from anonymous types) are named based on:

1. The property name in the parent DTO
2. A hash suffix to avoid conflicts

**Example:**
```csharp
.SelectExpr<Order, OrderDto>(o => new
{
    Items = o.OrderItems.Select(oi => new { oi.ProductName })
    //      ^^^^^ Property name
})

// Generated:
// - LinqraftGenerated_HASH.ItemsDto
// or
// - ItemsDto_HASH (if LinqraftNestedDtoUseHashNamespace = false)
```

See [Customization - Nested DTO Naming](./customization.md#nested-dto-naming) for details.

### Can I customize the generated namespace?

**Yes**, for DTOs in the global namespace, use `LinqraftGlobalNamespace`:

```xml
<PropertyGroup>
  <LinqraftGlobalNamespace>MyProject.Dtos</LinqraftGlobalNamespace>
</PropertyGroup>
```

For other namespaces, DTOs are generated in the same namespace as the source entity.

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

### How can I contribute?

Contributions are welcome! See the repository for:
* [Development guidelines](../../CLAUDE.md)
* [Contributing guide](../../CONTRIBUTING.md)
* [Code of conduct](../../CODE_OF_CONDUCT.md)

## Next Steps

* [Installation](./installation.md) - Get started with Linqraft
* [Usage Patterns](./usage-patterns.md) - Learn different usage patterns
* [Customization](./customization.md) - Customize generated code
* [Performance](./performance.md) - Performance benchmarks and comparisons
