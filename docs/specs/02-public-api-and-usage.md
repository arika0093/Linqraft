# 02. Public API and Usage Contracts

## 1. Public authoring surface

## 1.1 `SelectExpr` extension family

The documented user experience centers on `SelectExpr`.
The currently emitted support surface defines the following overload families for both `IQueryable<T>` and `IEnumerable<T>`:

```csharp
public static IQueryable<TResult> SelectExpr<TIn, TResult>(
    this IQueryable<TIn> query,
    Func<TIn, TResult> selector)
    where TIn : class;

public static IQueryable<TResult> SelectExpr<TIn, TResult>(
    this IQueryable<TIn> query,
    Func<TIn, object> selector)
    where TIn : class;

public static IQueryable<TResult> SelectExpr<TIn, TResult>(
    this IQueryable<TIn> query,
    Func<TIn, TResult> selector,
    object capture)
    where TIn : class;

public static IQueryable<TResult> SelectExpr<TIn, TResult>(
    this IQueryable<TIn> query,
    Func<TIn, object> selector,
    object capture)
    where TIn : class;
```

Equivalent overloads exist for `IEnumerable<T>`.

From the caller's perspective, the important supported call forms are:

- `query.SelectExpr(x => new { ... })`
- `query.SelectExpr<TSource, TDto>(x => new { ... })`
- `query.SelectExpr(x => new ExistingDto { ... })`
- `query.SelectExpr(..., capture: new { ... })`

The clone MUST preserve those call shapes.

## 1.2 Mapping-generation surface

The documented mapping-method feature exposes two authoring surfaces:

### Helper-class approach

```csharp
[LinqraftMappingGenerate]
internal class OrderMappingDeclare : LinqraftMappingDeclare<Order>
{
    protected override void DefineMapping()
    {
        Source.SelectExpr<Order, OrderDto>(o => new { o.Id, o.CustomerName });
    }
}
```

Contract:

- the class MUST inherit from `LinqraftMappingDeclare<T>`
- the class MUST have `[LinqraftMappingGenerate]`
- `DefineMapping()` MUST contain exactly one documented `SelectExpr` mapping definition
- consumers use the generated projection method, not `DefineMapping()` itself

### Static partial class approach

```csharp
public static partial class OrderQueries
{
    [LinqraftMappingGenerate("ProjectToDto")]
    internal static IQueryable<OrderDto> Template(this IQueryable<Order> source) =>
        source.SelectExpr<Order, OrderDto>(o => new { o.Id, o.CustomerName });
}
```

Contract:

- the containing class MUST be `static` and `partial`
- the template method MUST be marked with `[LinqraftMappingGenerate]`
- the class MUST be top-level rather than nested
- the template method MUST contain at least one `SelectExpr` call

## 2. The three core `SelectExpr` patterns

## 2.1 Anonymous pattern

### Authoring form

```csharp
var result = query.SelectExpr(x => new
{
    x.Id,
    CustomerName = x.Customer?.Name,
});
```

### Contract

- the caller does not supply explicit generic type arguments
- the selector returns an anonymous object
- the result type is anonymous and therefore not suitable as a public method contract
- null-conditional authoring MUST be supported
- the pattern SHOULD remain ideal for quick queries, experiments, and one-off local projections

### Intended trade-off

Pros:

- lowest ceremony
- no DTO name to invent
- good for local or exploratory code

Cons:

- anonymous result cannot be referenced as an API contract
- weaker reuse and discoverability than explicit DTOs

## 2.2 Explicit DTO pattern

### Authoring form

```csharp
var result = query.SelectExpr<Order, OrderDto>(o => new
{
    o.Id,
    CustomerName = o.Customer?.Name,
    Items = o.Items.Select(i => new
    {
        i.ProductName,
        i.Quantity,
    }),
});
```

### Contract

- the caller provides both source and result type parameters
- the selector body is still authored as an anonymous shape
- the clone MUST generate the named root DTO (`OrderDto`) from that shape
- nested anonymous objects and nested anonymous collection items MUST generate nested DTO types
- the returned projection MUST be strongly typed as the named DTO
- generated DTOs MUST be partial and reusable outside the originating query

### Intended use

This is the primary pattern for:

- API responses
- service return types
- reusable application-level DTOs
- Swagger/OpenAPI-visible contracts

## 2.3 Pre-existing DTO pattern

### Authoring form

```csharp
var result = query.SelectExpr(o => new OrderDto
{
    Id = o.Id,
    CustomerName = o.Customer?.Name,
});
```

### Contract

- the selector explicitly creates a user-defined DTO type
- the clone MUST NOT generate that DTO type
- the clone MUST still apply null-conditional support for `IQueryable`
- the DTO remains fully controlled by the user, including attributes, implemented interfaces, and accessibility

### Intended use

This pattern is for scenarios where:

- DTO types already exist
- DTOs are shared across project boundaries
- consumers require fine-grained manual control over the DTO declaration

## 3. Behavior matrix

| Dimension | Anonymous pattern | Explicit DTO pattern | Pre-existing DTO pattern |
| --- | --- | --- | --- |
| Named root DTO generated | No | Yes | No |
| Named root DTO provided by user | No | Yes, by generic argument | Yes, by object creation |
| Nested anonymous DTO generation | N/A or internal-only | Yes | Only for nested anonymous projections inside the object graph |
| Suitable for public API return types | No | Yes | Yes |
| Null-conditional support | Yes | Yes | Yes |
| Best for | quick local projections | reusable generated DTOs | manual DTO ownership |

## 4. `IQueryable<T>` vs `IEnumerable<T>`

The clone MUST support both receivers.

### `IQueryable<T>`

- the key requirement is expression-tree-compatible output
- null-conditional authoring MUST be rewritten into an explicit form the provider can translate
- this is the main path for EF Core and similar providers

### `IEnumerable<T>`

- the authoring surface MUST remain available
- null-conditionals already execute natively in compiled delegates, so no provider translation is required
- the docs treat this as a convenience parity feature rather than the primary scenario

## 5. Captured values contract

Local variables and outer-scope members are not implicitly closed over in the documented `SelectExpr` model.
Instead, callers MUST provide a `capture:` object when the selector depends on non-constant outer values:

```csharp
var threshold = 100;
var suffix = " units";

var result = query.SelectExpr<Entity, EntityDto>(
    x => new
    {
        x.Id,
        IsExpensive = x.Price > threshold,
        Label = x.Name + suffix,
    },
    capture: new { threshold, suffix });
```

The clone MUST preserve these rules:

- non-const locals from outer scope require capture
- outer parameters require capture
- non-constant instance members and static members require capture
- compile-time constants do not require capture
- analyzers MUST help the user add missing captures and remove unnecessary ones

## 6. Nested `SelectExpr` contract (beta)

The documentation explicitly supports using `SelectExpr` inside another `SelectExpr` for nested reusable DTOs.

### Authoring form

```csharp
var result = query.SelectExpr<Order, OrderDto>(o => new
{
    o.Id,
    Items = o.OrderItems.SelectExpr<OrderItem, OrderItemDto>(i => new
    {
        i.ProductName,
        i.Quantity,
    }),
});

internal partial class OrderDto;
internal partial class OrderItemDto;
```

### Required behavior

- nested explicit DTO generation MUST be supported
- the user MUST declare empty partial class declarations for all explicit DTOs involved in the nested chain
- the clone MUST use those declarations to determine the target nested DTO types and their generation location
- this workflow SHOULD be considered beta-compatible behavior rather than the default nested projection style

### Use guidance

Nested `SelectExpr` SHOULD be used when the nested DTO must be:

- reusable across multiple queries
- directly referenceable
- extendable through partial classes

Ordinary nested `Select` SHOULD remain the simpler choice when the nested DTO is only an internal implementation detail.

## 7. Partial-type extensibility contract

Generated DTOs are intended to behave as ordinary C# types.
Consumers MUST be able to extend them with:

- partial methods and helper methods
- interfaces
- custom attributes
- predeclared properties that suppress generator emission for the same property name

This is a first-class contract, not an incidental implementation detail.

## 8. Mapping-method contract

The mapping-method feature is an alternative to interceptor-based call-site replacement.
A compatible clone MUST preserve the following documented expectations:

- it is suitable for EF Core compiled queries and precompiled queries
- the helper-class approach is the recommended path for simple one-mapping-per-entity scenarios
- the static-partial-class approach is intended for users who want more control over the containing class or multiple mappings per class
- the default generated method name is `ProjectTo{EntityName}` unless overridden by `[LinqraftMappingGenerate("CustomName")]`
- generated mapping methods SHOULD inherit the accessibility of the declaring class

## 9. Documentation note on overload naming

Some warning documents refer to a `SelectExpr<TResult>()` convenience form as an escape hatch for avoiding direct use of hash-generated nested DTO types.
The consistently documented primary usage contracts remain:

- inferred call form: `SelectExpr(selector)`
- explicit DTO call form: `SelectExpr<TSource, TResult>(selector)`

A compatible clone SHOULD preserve those primary forms even if secondary convenience overloads are revised or clarified during the refactor.
