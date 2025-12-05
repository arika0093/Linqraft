# How to Run
## Prerequisites
- .NET 9.0 SDK or later

## Benchmark Run

To measure actual performance:
```bash
cd examples/Linqraft.Benchmark
dotnet run -c Release
```

## Benchmark Patterns

The benchmark compares the following patterns:

### Baseline
1. **Traditional Anonymous** - Traditional LINQ Select with anonymous type (baseline)
2. **Traditional Manual DTO** - Traditional LINQ Select with manually defined DTOs

### Linqraft
3. **Linqraft Anonymous** - Linqraft SelectExpr with anonymous type
4. **Linqraft Auto-Generated DTO** - Linqraft SelectExpr with auto-generated DTOs
5. **Linqraft Manual DTO** - Linqraft SelectExpr with manually defined DTOs

### Third-Party Mapping Libraries
6. **AutoMapper ProjectTo** - AutoMapper's IQueryable projection with `ProjectTo<T>()`
7. **Mapperly Projection** - Mapperly's source-generated projection with `ProjectToDto()`
8. **Mapster ProjectToType** - Mapster's IQueryable projection with `ProjectToType<T>()`
9. **Facet ToFacetsAsync** - Facet's source-generated DTO projection with `ToFacetsAsync<TSource, TFacet>()`

## Library Notes

### AutoMapper (v15+)
- Uses profile-based configuration
- Requires `MapperConfiguration` with `ILoggerFactory` parameter (v15 breaking change)
- Best for complex mapping scenarios with extensive customization

### Mapperly (v4+)
- Source generator - generates mapping code at compile time
- Zero runtime overhead for mappings
- Requires `[Mapper]` attribute on mapper class

### Mapster (v7+)
- Runtime configuration with `TypeAdapterConfig`
- Supports both compile-time and runtime mapping
- Good balance between flexibility and performance

### Facet (v5+)
- Source generator that creates DTOs and projections from domain models
- Uses `[Facet]` attribute on partial record/class with `NestedFacets` for nested objects
- Automatic navigation property loading with EF Core
- Uses `ToFacetsAsync<TSource, TFacet>()` for better performance
