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

For detailed benchmark code, see [Linqraft.Benchmark](../../examples/Linqraft.Benchmark).  
For comparisons with other mapping libraries, see the [Library Comparison](./library-comparison.md) guide.

## Next Steps

* [Installation](./installation.md) - Get started with Linqraft
* [Usage Patterns](./usage-patterns.md) - Learn different usage patterns
* [Library Comparison](./library-comparison.md) - Compare with other mapping libraries
