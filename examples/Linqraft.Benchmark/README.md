# Linqraft Performance Benchmark

This project benchmarks the performance difference between using Linqraft and the traditional EF Core `.Select` method.

## Benchmark Results

**📊 [See detailed benchmark results](./BENCHMARK_RESULTS.md)**

### Summary

| Method                        | Mean     | Ratio | Allocated |
|------------------------------ |---------:|------:|----------:|
| Traditional Manual DTO        | 1.635 ms |  0.89 | 244.79 KB |
| **Linqraft Auto-Generated DTO** | **1.651 ms** |  **0.90** | **245.23 KB** |
| Linqraft Anonymous            | 1.778 ms |  0.97 | 244.41 KB |
| Traditional Anonymous         | 1.834 ms |  1.00 | 245.99 KB |

**Key findings:**
- ✅ Linqraft auto-generated DTOs have almost the same performance as traditional manual DTOs (difference: 0.98%)
- ✅ Memory allocation is also nearly identical (~245 KB)
- ✅ Achieves more readable code with no performance penalty


## Benchmark Patterns

The following four patterns are compared:

1. **Traditional Anonymous**: Requires verbose null checks
2. **Traditional Manual DTO**: Manual DTO definition + verbose null checks
3. **Linqraft Anonymous**: Can use null-conditional operator
4. **Linqraft Auto-Generated DTO**: DTO class is auto-generated + null-conditional operator


### Code Comparison

**Traditional (verbose null checks):**
```csharp
ChildId = c.Child != null ? c.Child.Id : null,
Child3ChildId = s.Child3 != null && s.Child3.Child != null ? s.Child3.Child.Id : null,
```

**Using Linqraft (concise null-conditional operator):**
```csharp
ChildId = c.Child?.Id,
Child3ChildId = s.Child3?.Child?.Id,
```


## How to Run

### Prerequisites
- .NET 9.0 SDK or later

### Test Run (Functionality Check)

First, verify that all four patterns work correctly:
```bash
cd examples/Linqraft.Benchmark
dotnet run --test
```

### Benchmark Run

To measure actual performance:
```bash
cd examples/Linqraft.Benchmark
dotnet run -c Release
```

Results are saved in `BenchmarkDotNet.Artifacts/results/`.


## Test Data

The benchmark uses 100 sample records. Each record includes:
- Parent class (`SampleClass`)
- List of child classes (2 `SampleChildClass` instances)
- Nullable child class (`SampleChildClass2`) - present 50% of the time
- Required child class (`SampleChildClass3`)
- Further nested child classes (`SampleChildChildClass`, `SampleChildChildClass2`)

All documentation and comments are in English.


