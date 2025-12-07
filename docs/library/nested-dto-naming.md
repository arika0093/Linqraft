# Nested DTO Naming

Nested DTOs (generated from anonymous types inside the selector) can have their naming strategy customized.

## Hash Namespace (Default)

By default, nested DTOs are placed in a hash-suffixed namespace to avoid naming conflicts:

```csharp
namespace MyProject
{
    public partial class OrderDto
    {
        public required List<global::MyProject.LinqraftGenerated_DE33EA40.ItemsDto> Items { get; set; }
    }
}

namespace MyProject.LinqraftGenerated_DE33EA40
{
    public partial class ItemsDto
    {
        public required string? ProductName { get; set; }
    }
}
```

**Pros:**
* Clean class names
* Avoids namespace pollution
* Easy to find in namespace explorer

**Cons:**
* Longer fully-qualified names
* More namespaces

## Hash Class Name

Set `LinqraftNestedDtoUseHashNamespace` to `false` to use hash-suffixed class names instead:

```xml
<PropertyGroup>
  <LinqraftNestedDtoUseHashNamespace>false</LinqraftNestedDtoUseHashNamespace>
</PropertyGroup>
```

Generated code:

```csharp
namespace MyProject
{
    public partial class OrderDto
    {
        public required List<global::MyProject.ItemsDto_DE33EA40> Items { get; set; }
    }

    public partial class ItemsDto_DE33EA40
    {
        public required string? ProductName { get; set; }
    }
}
```

**Pros:**
* All DTOs in the same namespace
* Shorter fully-qualified names

**Cons:**
* Hash-suffixed class names
* More classes in the same namespace

## How Nested DTO Names Are Generated

Nested DTOs are named based on:

1. The property name in the parent DTO
2. A hash suffix to avoid conflicts

**Example:**
```csharp
.SelectExpr<Order, OrderDto>(o => new
{
    Items = o.OrderItems.Select(oi => new { oi.ProductName })
})

// Generated:
// - LinqraftGenerated_HASH.ItemsDto
// or
// - ItemsDto_HASH (if LinqraftNestedDtoUseHashNamespace = false)
```
