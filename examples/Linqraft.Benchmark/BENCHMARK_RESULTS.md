# Benchmark Results

## Test Environment
- **OS**: Ubuntu 24.04.3 LTS (Noble Numbat)
- **CPU**: AMD EPYC 7763, 1 CPU, 4 logical and 2 physical cores
- **.NET**: .NET 9.0.10 (9.0.1025.47515), X64 RyuJIT AVX2
- **Test Data**: 100 records with nested relationships

## Benchmark Results

| Method                        | Mean     | Error     | StdDev    | Ratio | Rank | Gen0    | Allocated | Alloc Ratio |
|------------------------------ |---------:|----------:|----------:|------:|-----:|--------:|----------:|------------:|
| 'Traditional Manual DTO'      | 1.635 ms | 0.0081 ms | 0.0076 ms |  0.89 |    1 | 11.7188 | 244.79 KB |        1.00 |
| 'Linqraft Auto-Generated DTO' | 1.651 ms | 0.0105 ms | 0.0098 ms |  0.90 |    1 | 11.7188 | 245.23 KB |        1.00 |
| 'Linqraft Anonymous'          | 1.778 ms | 0.0059 ms | 0.0055 ms |  0.97 |    2 | 11.7188 | 244.41 KB |        0.99 |
| 'Traditional Anonymous'       | 1.834 ms | 0.0092 ms | 0.0086 ms |  1.00 |    3 | 11.7188 | 245.99 KB |        1.00 |

## Summary

### Performance Rankings (Fastest to Slowest)

1. **Traditional Manual DTO** (1.635 ms) - Fastest ✨
2. **Linqraft Auto-Generated DTO** (1.651 ms) - Virtually identical to #1 (0.98% slower)
3. **Linqraft Anonymous** (1.778 ms) - 8.7% slower than #1
4. **Traditional Anonymous** (1.834 ms) - Baseline (slowest)

### Key Findings

#### 🚀 Performance
- **Linqraft Auto-Generated DTO is just as fast as Traditional Manual DTO** (difference: ~16 microseconds or 0.98%)
- Both Linqraft and Traditional approaches show similar performance when comparing like-for-like patterns
- Anonymous type patterns are slightly slower than explicit DTO patterns (~3-9%)

#### 💾 Memory Allocation
- All four patterns show **nearly identical memory allocation** (~245 KB)
- No significant memory overhead from using Linqraft

#### ✨ Code Quality Benefits
While Linqraft shows comparable performance, it provides significant code quality benefits:

**Traditional approach (verbose null checks)**:
```csharp
ChildId = c.Child != null ? c.Child.Id : null,
Child3ChildId = s.Child3 != null && s.Child3.Child != null ? s.Child3.Child.Id : null,
```

**Linqraft approach (clean null-conditional operators)**:
```csharp
ChildId = c.Child?.Id,
Child3ChildId = s.Child3?.Child?.Id,
```

## Conclusion

**Linqraft provides the same performance as traditional EF Core Select while offering:**
- ✅ Cleaner, more readable code with null-conditional operators (`?.`)
- ✅ Automatic DTO generation (no manual class definitions needed)
- ✅ Zero performance penalty
- ✅ Identical memory footprint

You get better developer experience at no performance cost! 🎉
