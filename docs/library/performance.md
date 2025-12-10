# Performance

This guide covers Linqraft's performance characteristics.

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
| 'Linqraft Manual DTO'         |   858.5 us |  5.51 us |  5.16 us |  0.99 |    0.01 |    1 | 13.6719 | 1.9531 | 232.23 KB |        1.00 |
| 'Linqraft Auto-Generated DTO' |   865.0 us |  5.77 us |  5.40 us |  1.00 |    0.01 |    1 | 13.6719 | 1.9531 | 232.38 KB |        1.00 |
| 'Mapperly Projection'         |   869.1 us |  5.65 us |  5.28 us |  1.00 |    0.01 |    1 | 13.6719 | 1.9531 | 244.44 KB |        1.05 |
| 'Mapster ProjectToType'       |   873.6 us |  4.78 us |  4.47 us |  1.01 |    0.01 |    1 | 13.6719 | 1.9531 | 236.59 KB |        1.02 |
| 'AutoMapper ProjectTo'        |   884.0 us |  2.50 us |  2.34 us |  1.02 |    0.01 |    1 | 13.6719 | 1.9531 | 237.44 KB |        1.02 |
| 'Traditional Manual DTO'      |   885.2 us |  6.69 us |  6.26 us |  1.02 |    0.01 |    1 | 13.6719 | 1.9531 | 245.61 KB |        1.06 |
| 'Traditional Anonymous'       |   952.6 us |  7.22 us |  6.75 us |  1.10 |    0.01 |    2 | 13.6719 | 1.9531 |    247 KB |        1.06 |
| 'Linqraft Anonymous'          |   955.3 us |  6.22 us |  5.82 us |  1.10 |    0.01 |    2 | 13.6719 | 1.9531 | 245.25 KB |        1.06 |
| 'Facet ToFacetsAsync'         | 2,062.3 us | 12.25 us | 11.46 us |  2.38 |    0.02 |    3 | 31.2500 | 3.9063 | 541.55 KB |        2.33 |

For detailed benchmark code, see [Linqraft.Benchmark](../../examples/Linqraft.Benchmark).  
For comparisons with other mapping libraries, see the [Library Comparison](./library-comparison.md) guide.

## Next Steps

* [Installation](./installation.md) - Get started with Linqraft
* [Usage Patterns](./usage-patterns.md) - Learn different usage patterns
* [Library Comparison](./library-comparison.md) - Compare with other mapping libraries
