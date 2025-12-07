# Performance

This guide covers Linqraft's performance characteristics and best practices.

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

For comparisons with other mapping libraries, see the [Library Comparison](./library-comparison.md) guide.

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
* [Library Comparison](./library-comparison.md) - Compare with other mapping libraries
