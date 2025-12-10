# Performance

This guide covers Linqraft's performance characteristics.

## Performance Benchmarks

Linqraft's performance is nearly identical to manually-written projections. This is because:

1. **No runtime overhead**: Code is generated at compile-time using Source Generators
2. **Native Expression Trees**: Generated code uses standard LINQ `Select` expressions
3. **Zero dependencies**: No mapping framework is loaded or executed at runtime

## Benchmark Results

The following benchmark compares Linqraft with popular mapping libraries and manual approaches:

### Environment

```
BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.7171/25H2/2025Update/HudsonValley2)
Intel Core i7-14700F 2.10GHz, 1 CPU, 28 logical and 20 physical cores
.NET SDK 10.0.100
  [Host]     : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v3
  DefaultJob : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v3
```

### IEnumerable

**.NET 10**
```
| Method                        | DataCount | Mean      | Error     | StdDev    | Median    | Ratio | RatioSD | Rank | Gen0   | Gen1   | Allocated | Alloc Ratio |
|------------------------------ |---------- |----------:|----------:|----------:|----------:|------:|--------:|-----:|-------:|-------:|----------:|------------:|
| 'Traditional Manual DTO'      | 100       |  1.785 us | 0.0343 us | 0.0320 us |  1.779 us |  0.81 |    0.02 |    1 | 0.9785 | 0.0572 |   16.5 KB |        1.00 |
| 'Linqraft Auto-Generated DTO' | 100       |  2.196 us | 0.0439 us | 0.0555 us |  2.191 us |  1.00 |    0.03 |    2 | 0.9766 | 0.0572 |   16.5 KB |        1.00 |
| 'Traditional Anonymous'       | 100       |  2.219 us | 0.0429 us | 0.0422 us |  2.223 us |  1.01 |    0.03 |    2 | 0.9766 | 0.0572 |   16.5 KB |        1.00 |
| 'Linqraft Manual DTO'         | 100       |  2.285 us | 0.0509 us | 0.1500 us |  2.326 us |  1.04 |    0.07 |    2 | 0.9766 | 0.0572 |   16.5 KB |        1.00 |
| 'Linqraft Anonymous'          | 100       |  2.324 us | 0.0462 us | 0.1117 us |  2.300 us |  1.06 |    0.06 |    2 | 0.9766 | 0.0572 |   16.5 KB |        1.00 |
| 'Mapperly Map'                | 100       |  2.649 us | 0.0485 us | 0.0981 us |  2.630 us |  1.21 |    0.05 |    3 | 1.3504 | 0.1106 |  22.75 KB |        1.38 |
| 'AutoMapper Map'              | 100       |  4.479 us | 0.0768 us | 0.0681 us |  4.474 us |  2.04 |    0.06 |    4 | 1.7014 | 0.1678 |   28.7 KB |        1.74 |
| 'Mapster Adapt'               | 100       | 36.286 us | 0.3275 us | 0.2903 us | 36.346 us | 16.53 |    0.43 |    5 | 1.3428 | 0.0610 |  22.71 KB |        1.38 |
```

**.NET 10 NativeAOT**
```
| Method                        | DataCount | Mean     | Error     | StdDev    | Ratio | RatioSD | Rank | Gen0   | Gen1   | Allocated | Alloc Ratio |
|------------------------------ |---------- |---------:|----------:|----------:|------:|--------:|-----:|-------:|-------:|----------:|------------:|
| 'Traditional Manual DTO'      | 100       | 3.485 us | 0.0157 us | 0.0131 us |  0.99 |    0.01 |    1 | 0.9651 | 0.0534 |   16.3 KB |        1.00 |
| 'Linqraft Auto-Generated DTO' | 100       | 3.532 us | 0.0577 us | 0.0540 us |  1.00 |    0.02 |    1 | 0.9651 | 0.0534 |   16.3 KB |        1.00 |
| 'Linqraft Anonymous'          | 100       | 3.552 us | 0.0693 us | 0.0825 us |  1.01 |    0.03 |    1 | 0.9651 | 0.0534 |   16.3 KB |        1.00 |
| 'Linqraft Manual DTO'         | 100       | 3.612 us | 0.0714 us | 0.1047 us |  1.02 |    0.03 |    1 | 0.9651 | 0.0534 |   16.3 KB |        1.00 |
| 'Traditional Anonymous'       | 100       | 3.652 us | 0.0688 us | 0.1358 us |  1.03 |    0.04 |    1 | 0.9651 | 0.0534 |   16.3 KB |        1.00 |
| 'Mapperly Map'                | 100       | 6.452 us | 0.0532 us | 0.0444 us |  1.83 |    0.03 |    2 | 1.6556 | 0.1373 |  28.02 KB |        1.72 |
```

### IQueryable

**.NET 10**
```
| Method                        | DataCount | Mean     | Error   | StdDev  | Ratio | Rank | Gen0   | Gen1   | Allocated | Alloc Ratio |
|------------------------------ |---------- |---------:|--------:|--------:|------:|-----:|-------:|-------:|----------:|------------:|
| 'Linqraft Auto-Generated DTO' | 100       | 874.5 us | 5.16 us | 4.83 us |  1.00 |    1 | 1.9531 | 0.9766 |  46.68 KB |        1.00 |
| 'Mapperly Projection'         | 100       | 874.8 us | 4.77 us | 4.46 us |  1.00 |    1 | 1.9531 |      - |  58.66 KB |        1.26 |
| 'Linqraft Manual DTO'         | 100       | 885.5 us | 7.50 us | 6.64 us |  1.01 |    1 | 1.9531 | 0.9766 |  46.68 KB |        1.00 |
| 'AutoMapper ProjectTo'        | 100       | 886.0 us | 2.47 us | 2.19 us |  1.01 |    1 | 1.9531 |      - |  46.92 KB |        1.01 |
| 'Mapster ProjectToType'       | 100       | 890.4 us | 5.91 us | 5.53 us |  1.02 |    1 | 1.9531 | 0.9766 |  46.17 KB |        0.99 |
| 'Traditional Manual DTO'      | 100       | 896.7 us | 6.66 us | 6.23 us |  1.03 |    1 | 1.9531 |      - |  59.85 KB |        1.28 |
| 'Traditional Anonymous'       | 100       | 967.5 us | 8.23 us | 6.87 us |  1.11 |    2 | 1.9531 |      - |  59.66 KB |        1.28 |
| 'Linqraft Anonymous'          | 100       | 978.3 us | 6.68 us | 6.25 us |  1.12 |    2 | 1.9531 |      - |  60.21 KB |        1.29 |

```

**.NET 10 NativeAOT**
```
| Method                        | DataCount | Mean     | Error   | StdDev  | Ratio | Rank | Gen0   | Gen1   | Allocated | Alloc Ratio |
|------------------------------ |---------- |---------:|--------:|--------:|------:|-----:|-------:|-------:|----------:|------------:|
| 'Linqraft Manual DTO'         | 100       | 135.8 us | 1.09 us | 0.91 us |  0.99 |    1 | 7.3242 | 0.7324 | 125.55 KB |        1.00 |
| 'Linqraft Auto-Generated DTO' | 100       | 136.9 us | 0.53 us | 0.44 us |  1.00 |    1 | 7.3242 | 0.7324 | 125.23 KB |        1.00 |
| 'Traditional Anonymous'       | 100       | 149.0 us | 1.20 us | 1.12 us |  1.09 |    2 | 8.0566 | 0.7324 | 135.85 KB |        1.08 |
| 'Linqraft Anonymous'          | 100       | 149.7 us | 1.49 us | 1.25 us |  1.09 |    2 | 7.8125 | 0.9766 | 135.68 KB |        1.08 |
| 'Mapperly Projection'         | 100       | 160.7 us | 1.00 us | 0.89 us |  1.17 |    3 | 8.5449 | 0.9766 | 146.64 KB |        1.17 |
| 'Traditional Manual DTO'      | 100       | 167.8 us | 1.75 us | 1.64 us |  1.23 |    4 | 8.7891 | 0.9766 | 148.86 KB |        1.19 |
```


### EFCore(SQLite)


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
