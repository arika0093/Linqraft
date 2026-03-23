# Array Nullability Removal

For convenience, Linqraft automatically removes nullability from collection-type properties in generated DTOs when the query shape guarantees that an empty collection is a better fallback than `null`.

## Why?

With collection types, there's rarely a need to distinguish between `null` and `[]` (empty collection). Removing nullability simplifies your code by eliminating unnecessary null checks like `dto.Items ?? []`.

## Rules

Nullability is removed when **all** of the following conditions are met:

1. The expression does **not** contain a ternary operator (`? :`)
2. The type is `IEnumerable<T>` or derived (List, Array, etc.)
3. The expression uses null-conditional access (`?.`)
4. The expression contains a `Select` or `SelectMany` call

## Example

```csharp
query.UseLinqraft().Select<EntityDto>(e => new
{
    // Nullability removed: List<string>? -> List<string>
    ChildNames = e.Child?.Select(c => c.Name).ToList(),

    // Nullability removed: IEnumerable<ChildDto>? -> IEnumerable<ChildDto>
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
    public required List<string> ChildNames { get; set; }
    public required IEnumerable<ChildDto> ChildDtos { get; set; }
    public required List<string>? ExplicitNullableNames { get; set; }
}

var converted = matchedQuery.Select(d => new EntityDto
{
    ChildNames = d.Child != null
        ? d.Child.Select(c => c.Name).ToList()
        : new List<string>(),

    ChildDtos = d.Child != null
        ? d.Child.Select(c => new ChildDto { Name = c.Name, Description = c.Description })
        : Enumerable.Empty<ChildDto>(),

    ExplicitNullableNames = d.Child != null
        ? d.Child.Select(c => c.Name).ToList()
        : null,
});
```

## Disabling This Behavior

If you prefer to keep nullability on collection types, set the global property to `false`:

```xml
<PropertyGroup>
  <LinqraftArrayNullabilityRemoval>false</LinqraftArrayNullabilityRemoval>
</PropertyGroup>
```

This change helps avoid unnecessary null checks like `dto.ChildNames ?? []`, keeping the code simple.
