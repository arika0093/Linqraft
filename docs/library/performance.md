# Performance & Comparisons

This guide covers Linqraft's performance characteristics and comparisons with other mapping libraries.

## Table of Contents

* [Performance Benchmarks](#performance-benchmarks)
* [Comparison with Other Libraries](#comparison-with-other-libraries)
* [Performance Considerations](#performance-considerations)

## Performance Benchmarks

Linqraft's performance is nearly identical to manually-written projections. This is because:

1. **No runtime overhead**: Code is generated at compile-time using Source Generators
2. **Native Expression Trees**: Generated code uses standard LINQ `Select` expressions
3. **Zero dependencies**: No mapping framework is loaded or executed at runtime

### Benchmark Results

The following benchmark compares Linqraft with popular mapping libraries and manual approaches:

```
BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.7171/25H2/2025Update/HudsonValley2)
Intel Core i7-14700F 2.10GHz, 1 CPU, 28 logical and 20 physical cores
.NET SDK 10.0.100
  [Host]     : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v3
  DefaultJob : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v3
```

| Method                        | Mean       | Error    | StdDev   | Ratio | RatioSD | Rank | Gen0    | Gen1   | Allocated | Alloc Ratio |
|------------------------------ |-----------:|---------:|---------:|------:|--------:|-----:|--------:|-------:|----------:|------------:|
| Mapperly Projection           |   877.9 us |  6.96 us |  6.51 us |  0.98 |    0.01 |    1 | 13.6719 | 1.9531 | 244.69 KB |        1.00 |
| Mapster ProjectToType         |   881.6 us |  6.13 us |  5.73 us |  0.98 |    0.01 |    1 | 13.6719 | 1.9531 | 236.59 KB |        0.96 |
| AutoMapper ProjectTo          |   887.2 us |  5.97 us |  5.59 us |  0.99 |    0.01 |    1 | 13.6719 | 1.9531 | 237.38 KB |        0.97 |
| **Linqraft Manual DTO**       | **893.5 us** |  **3.05 us** |  **2.70 us** |  **0.99** |    **0.01** |    **1** | **13.6719** | **1.9531** | **245.97 KB** |        **1.00** |
| Traditional Manual DTO        |   898.2 us |  5.92 us |  5.24 us |  1.00 |    0.01 |    1 | 13.6719 | 1.9531 | 245.63 KB |        1.00 |
| **Linqraft Auto-Generated DTO** | **900.2 us** |  **7.49 us** |  **7.01 us** |  **1.00** |    **0.01** |    **1** | **13.6719** | **1.9531** | **245.78 KB** |        **1.00** |
| **Linqraft Anonymous**        | **971.7 us** | **19.31 us** | **20.67 us** |  **1.08** |    **0.02** |    **2** | **13.6719** | **1.9531** | **245.36 KB** |        **1.00** |
| Traditional Anonymous         |   984.4 us | 16.56 us | 19.08 us |  1.09 |    0.02 |    2 | 13.6719 | 1.9531 | 247.29 KB |        1.01 |
| Facet ToFacetsAsync           | 2,086.8 us |  9.59 us |  8.50 us |  2.32 |    0.02 |    3 | 31.2500 | 3.9063 | 541.53 KB |        2.20 |

### Key Takeaways

1. **Linqraft Auto-Generated DTO performs identically to manual code** (900.2 us vs 898.2 us)
2. **No memory overhead**: Allocated memory is the same as manual approaches (~245 KB)
3. **Comparable to other source generators**: Similar performance to Mapperly, Mapster, and AutoMapper
4. **Anonymous types have slight overhead**: Due to compiler-generated type handling (~8% slower)

For detailed benchmark code, see [Linqraft.Benchmark](../../examples/Linqraft.Benchmark).

## Comparison with Other Libraries

Mapping and projection are common tasks in .NET development. Here's how Linqraft compares to other popular libraries.

### Summary Table

| Library    | DTO Definition | Generation  | Customization | Reverse | License    | Runtime Deps |
| ---------- | -------------- | ----------- | ------------- | ------- | ---------- | ------------ |
| AutoMapper | Manual         | From class  | Config-based  | Yes     | Paid (15+) | Yes          |
| Mapster    | Manual         | From class  | Config-based  | Yes     | MIT        | Yes          |
| Mapperly   | Manual         | From class  | Code/Attr     | Yes     | Apache 2.0 | No           |
| Facet      | Semi-auto      | From class  | Attributes    | Yes     | MIT        | Yes          |
| **Linqraft** | **Auto**     | **From query** | **Inline**  | **No**  | **Apache 2.0** | **No**   |

### AutoMapper

[AutoMapper](https://automapper.io/) is one of the most popular mapping libraries in the .NET ecosystem.

**How it works:**
* Define DTOs manually
* Configure mapping rules using `MapperConfiguration`
* Use `ProjectTo<TDestination>()` for projections

**Example:**
```csharp
// Define DTO manually
public class OrderDto
{
    public int Id { get; set; }
    public string CustomerName { get; set; }
}

// Configure mapping
var config = new MapperConfiguration(cfg =>
{
    cfg.CreateMap<Order, OrderDto>()
        .ForMember(dest => dest.CustomerName, opt => opt.MapFrom(src => src.Customer.Name));
});

// Use ProjectTo
var orders = dbContext.Orders
    .ProjectTo<OrderDto>(config)
    .ToList();
```

**Pros:**
* Highly configurable
* Large community and ecosystem
* Supports complex mapping scenarios

**Cons:**
* Configuration is not type-safe
* DTO must be pre-defined
* **Paid license required for commercial use from version 15 onward**
* Runtime dependency

**Linqraft vs AutoMapper:**
* Linqraft: DTOs generated from queries, inline customization, no runtime deps, free
* AutoMapper: DTOs pre-defined, config-based customization, runtime deps, paid (v15+)

### Mapster

[Mapster](https://github.com/MapsterMapper/Mapster) is a fast and flexible object mapping library.

**How it works:**
* Define DTOs manually (or generate with `Mapster.Tool`)
* Configure mappings if needed
* Use `ProjectToType<TDestination>()` for projections

**Example:**
```csharp
// Define DTO manually
public class OrderDto
{
    public int Id { get; set; }
    public string CustomerName { get; set; }
}

// Optional: Configure custom mapping
TypeAdapterConfig<Order, OrderDto>
    .NewConfig()
    .Map(dest => dest.CustomerName, src => src.Customer.Name);

// Use ProjectToType
var orders = dbContext.Orders
    .ProjectToType<OrderDto>()
    .ToList();
```

**Pros:**
* Fast performance
* Can generate DTOs with `Mapster.Tool` (separate process)
* Flexible configuration

**Cons:**
* DTO must be pre-defined (or generated separately)
* Manual configuration for complex structures
* Runtime dependency

**Linqraft vs Mapster:**
* Linqraft: DTOs auto-generated inline, no separate tool needed, no runtime deps
* Mapster: DTOs pre-defined or generated separately, runtime deps

### Mapperly

[Mapperly](https://mapperly.riok.app/) is a modern source generator-based mapping library.

**How it works:**
* Define DTOs manually
* Create a mapper interface with `[Mapper]` attribute
* Mapperly generates the implementation at compile-time

**Example:**
```csharp
// Define DTO manually
public class OrderDto
{
    public int Id { get; set; }
    public string CustomerName { get; set; }
}

// Define mapper interface
[Mapper]
public partial class OrderMapper
{
    // Must explicitly specify property mappings for flattening
    [MapProperty(nameof(Order.Customer.Name), nameof(OrderDto.CustomerName))]
    public partial IQueryable<OrderDto> MapToDto(IQueryable<Order> orders);
}

// Use mapper
var mapper = new OrderMapper();
var orders = mapper.MapToDto(dbContext.Orders).ToList();
```

**Pros:**
* Source generator-based (no runtime overhead)
* Generated code is easy to read
* Type-safe

**Cons:**
* DTO must be pre-defined
* Customization requires defining methods or attributes
* [Property mappings must be explicitly specified](https://mapperly.riok.app/docs/configuration/flattening/) for flattening

**Linqraft vs Mapperly:**
* Linqraft: DTOs auto-generated from queries, inline customization
* Mapperly: DTOs pre-defined, method/attribute-based customization, explicit property mapping

### Facet

[Facet](https://github.com/Tim-Maes/Facet) is a feature-rich DTO generation library.

**How it works:**
* Automatically generates DTOs from existing types
* Control generation with `Include`/`Exclude` attributes
* Provides EF Core extensions for CRUD operations

**Example:**
```csharp
// Define entity with attributes
public class Order
{
    public int Id { get; set; }

    [Include]
    public string CustomerName { get; set; }

    [Exclude]
    public string InternalField { get; set; }

    [NestedFacets]
    public List<OrderItem> Items { get; set; }
}

// Use ToFacetsAsync
var orders = await dbContext.Orders.ToFacetsAsync<OrderFacet>();
```

**Pros:**
* Multiple DTOs per entity
* EF Core extensions for CRUD
* Feature-rich

**Cons:**
* Configuration can be complex
* Must explicitly control generation with attributes
* Nested objects require explicit `NestedFacets` attribute
* Performance overhead (see benchmark: 2x slower)

**Linqraft vs Facet:**
* Linqraft: Query-based, minimal configuration, better performance
* Facet: Class-based, attribute-heavy configuration, more features (CRUD extensions)

### Linqraft's Unique Approach

Linqraft takes a fundamentally different approach:

**Query-based generation** instead of class-based:
```csharp
// Traditional approach: Define DTO first, then map
public class OrderDto { /* properties */ }
var orders = dbContext.Orders.ProjectTo<OrderDto>();

// Linqraft approach: Define query first, DTO is generated
var orders = dbContext.Orders
    .SelectExpr<Order, OrderDto>(o => new { /* define structure here */ });
```

**Benefits:**
1. **Flexible structures**: Not constrained by entity structure
2. **Computed fields**: Easy to add calculated properties
3. **Inline customization**: No separate configuration needed
4. **Zero runtime dependencies**: All code generated at compile-time

**Trade-offs:**
1. **No reverse mapping**: Can't convert DTO back to entity (by design)
2. **Not for shared DTOs**: Query-based generation isn't suitable for DTOs shared across projects
   * Workaround: Use Linqraft in API layer, generate shared types from OpenAPI schema
3. **Requires C# 12+**: Due to interceptor feature

## Performance Considerations

### When to Use Linqraft

Linqraft performs best when:

1. **Projecting from database queries**
   ```csharp
   // Excellent performance - generated at compile-time
   var orders = await dbContext.Orders
       .SelectExpr<Order, OrderDto>(o => new { o.Id, o.CustomerName })
       .ToListAsync();
   ```

2. **Nested projections**
   ```csharp
   // Efficient nested projections
   .SelectExpr<Order, OrderDto>(o => new
   {
       o.Id,
       Items = o.OrderItems.Select(oi => new { oi.ProductName, oi.Quantity })
   })
   ```

3. **Computed fields**
   ```csharp
   // No performance penalty for calculations
   .SelectExpr<Order, OrderDto>(o => new
   {
       o.Id,
       TotalAmount = o.OrderItems.Sum(oi => oi.Quantity * oi.UnitPrice)
   })
   ```

### Performance Tips

1. **Use explicit DTO pattern for APIs**
   ```csharp
   // Better for APIs - type-safe, no anonymous type overhead
   .SelectExpr<Order, OrderDto>(o => new { /* ... */ })
   ```

2. **Leverage null-propagation for cleaner SQL**
   ```csharp
   // Cleaner than manual null checks
   CustomerName = o.Customer?.Name
   ```

3. **Enable array nullability removal**
   ```csharp
   // Reduces null checks in your code
   <LinqraftArrayNullabilityRemoval>true</LinqraftArrayNullabilityRemoval>
   ```

4. **Use local variable capture sparingly**
   ```csharp
   // Minimal overhead, but use only when necessary
   .SelectExpr<Entity, EntityDto>(
       x => new { IsExpensive = x.Price > threshold },
       capture: new { threshold }
   )
   ```

## Conclusion

Linqraft provides:
* **Identical performance** to manual projections
* **Zero runtime overhead** with compile-time generation
* **No memory allocation overhead**
* **Query-based flexibility** not available in traditional mappers

Choose Linqraft when you need flexible, query-based DTO generation with zero runtime dependencies and excellent performance.

## Next Steps

* [Installation](./installation.md) - Get started with Linqraft
* [Usage Patterns](./usage-patterns.md) - Learn different usage patterns
* [Customization](./customization.md) - Customize generated code
