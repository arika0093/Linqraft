# Library Comparison

Mapping and projection are common tasks in .NET development. Here's how Linqraft compares to other popular libraries.

## Summary Table

| Library    | DTO Definition | Generation  | Customization | Reverse | License    | Runtime Deps |
| ---------- | -------------- | ----------- | ------------- | ------- | ---------- | ------------ |
| AutoMapper | Manual         | From class  | Config-based  | Yes     | Paid (15+) | Yes          |
| Mapster    | Manual         | From class  | Config-based  | Yes     | MIT        | Yes          |
| Mapperly   | Manual         | From class  | Code/Attr     | Yes     | Apache 2.0 | No           |
| Facet      | Semi-auto      | From class  | Attributes    | Yes     | MIT        | Yes          |
| Linqraft   | Auto           | From query  | Inline        | No      | Apache 2.0 | No           |

## AutoMapper

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

## Mapster

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

## Mapperly

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

## Facet

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

## Linqraft

Linqraft takes a fundamentally different approach:

**Query-based generation** instead of class-based:
```csharp
// Traditional approach: Define DTO, configuration, then query
public class OrderDto { /* properties */ }
var config = new MapperConfiguration( /* mapping rules */ );
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
