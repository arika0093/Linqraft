# Array Nullability Removal

For convenience, Linqraft automatically removes nullability from array-type properties in generated DTOs.

## Why?

With array types, there's rarely a need to distinguish between `null` and `[]` (empty array). Removing nullability simplifies your code by eliminating unnecessary null checks like `dto.Items ?? []`.

## Rules

Nullability is removed when **all** of the following conditions are met:

1. The expression does **not** contain a ternary operator (`? :`)
2. The type is `IEnumerable<T>` or derived (List, Array, etc.)
3. The expression uses null-conditional access (`?.`)
4. The expression contains a `Select` or `SelectMany` call

## Example

```csharp
query.SelectExpr<Entity, EntityDto>(e => new
{
    // Nullability removed: List<string>? → List<string>
    ChildNames = e.Child?.Select(c => c.Name).ToList(),

    // Nullability removed: IEnumerable<ChildDto>? → IEnumerable<ChildDto>
    ChildDtos = e.Child?.Select(c => new { c.Name, c.Description }),

    // Nullability preserved (explicit ternary): List<string>?
    ExplicitNullableNames = e.Child != null
        ? e.Child.Select(c => c.Name).ToList()
        : null,
});
```

## Generated Code

```csharp
public partial class EntityDto
{
    // Non-nullable array property
    public required List<string> ChildNames { get; set; }

    // Non-nullable collection property
    public required IEnumerable<ChildDto> ChildDtos { get; set; }

    // Nullable (because of explicit ternary)
    public required List<string>? ExplicitNullableNames { get; set; }
}

// Conversion with default empty collections
var converted = matchedQuery.Select(d => new EntityDto
{
    ChildNames = d.Child != null
        ? d.Child.Select(c => c.Name).ToList()
        : new List<int>(),

    ChildDtos = d.Child != null
        ? d.Child.Select(c => new ChildDto { Name = c.Name, Description = c.Description })
        : Enumerable.Empty<ChildDto>(),

    ExplicitNullableNames = d.Child != null
        ? d.Child.Select(c => c.Name).ToList()
        : null,
});
```

## Disabling This Behavior

If you prefer to keep nullability on array types, set the global property to `false`:

```xml
<PropertyGroup>
  <LinqraftArrayNullabilityRemoval>false</LinqraftArrayNullabilityRemoval>
</PropertyGroup>
```

This change helps avoid unnecessary null checks like `dto.ChildNames ?? []`, keeping the code simple.
