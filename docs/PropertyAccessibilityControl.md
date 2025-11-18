# Property Accessibility Control for Explicit DTOs

## Overview

This feature allows you to control the accessibility of individual properties in auto-generated DTOs when using the **Explicit Pattern** (`SelectExpr<TIn, TResult>`).

## How It Works

When you define a partial class for your DTO with properties having specific accessibility modifiers (e.g., `internal`, `protected internal`), the source generator will:

1. Detect the existing properties from your partial class definition
2. Extract their accessibility modifiers
3. Skip generating those properties (since they're already defined)
4. Only generate properties that are NOT predefined
5. Respect the `required` keyword visibility constraints

## Example

### Basic Usage

Define a partial class with the properties you want to control:

```csharp
// Your partial class with controlled accessibility
public partial class SampleDto
{
    internal string InternalProperty { get; set; } = "";
}

// Use the DTO in a query
var result = data.AsQueryable()
    .SelectExpr<Sample, SampleDto>(s => new
    {
        InternalProperty = s.InternalProperty,
        PublicProperty = s.PublicProperty,
    })
    .ToList();
```

The generator will create:

```csharp
// Generated partial class
public partial class SampleDto
{
    // InternalProperty is NOT generated (already defined by you)
    public required string PublicProperty { get; set; }
}
```

### Extending with Methods

You can add methods to your partial class that use the internal properties:

```csharp
public partial class SampleDto
{
    internal string InternalProperty { get; set; } = "";
    
    // Add methods that access internal properties
    public bool HasInternalValue()
    {
        return !string.IsNullOrEmpty(InternalProperty);
    }
}
```

### Multiple Accessibility Levels

You can use different accessibility modifiers for different properties:

```csharp
public partial class ComplexDto
{
    internal string InternalField { get; set; } = "";
    protected internal string ProtectedInternalField { get; set; } = "";
}

var result = data.AsQueryable()
    .SelectExpr<Entity, ComplexDto>(x => new
    {
        x.PublicField,           // Will be generated as public
        x.InternalField,         // Already defined as internal
        x.ProtectedInternalField, // Already defined as protected internal
    })
    .ToList();
```

Generated:

```csharp
public partial class ComplexDto
{
    public required string PublicField { get; set; }
    // InternalField and ProtectedInternalField are not generated
}
```

### With Nested Selects

The feature works seamlessly with nested selects:

```csharp
public partial class ParentDto
{
    internal int InternalId { get; set; }
}

var result = data.AsQueryable()
    .SelectExpr<Parent, ParentDto>(p => new
    {
        InternalId = p.Id,
        Children = p.Children.Select(c => new { c.Name, c.Value }).ToList(),
    })
    .ToList();
```

## Use Cases

This feature is particularly useful when:

1. **Encapsulation**: You want to keep some properties internal to your assembly while exposing others publicly
2. **Partial Class Extensions**: You need to extend the DTO with methods that access internal state
3. **Library Development**: You're building a library and want to control which properties are exposed in the public API
4. **Data Privacy**: You want to mark sensitive properties as internal for additional safety

## Compatibility

This is a **backward-compatible** feature:
- Existing code continues to work as before
- The feature is **opt-in** - you only get property-level accessibility control when you predefine properties in a partial class
- If you don't predefine any properties, all generated properties will be `public` as before

## Limitations

1. **Explicit Pattern Only**: This feature only works with the Explicit Pattern (`SelectExpr<TIn, TResult>`), not with Anonymous or Named patterns
2. **Required Keyword**: The `required` keyword is only used when the property's accessibility is at least as visible as the containing class (to prevent CS9032 errors)
3. **Partial Classes**: You must use partial classes to enable this feature

## Technical Details

### How Properties Are Detected

The generator examines the `TResult` type parameter to find existing properties:

```csharp
.SelectExpr<TIn, TResult>(...)
//              ^^^^^^^ This type is analyzed for existing properties
```

### Accessibility Priority

When a property is predefined:
1. The generator uses the predefined accessibility
2. The property is excluded from generation
3. The Select expression still assigns to the property (which exists in the partial class)

### Required Keyword Logic

The generator only applies the `required` keyword when:
- It's enabled in configuration (`HasRequired = true`)
- The property's accessibility is at least as visible as the class

Visibility levels (from most to least visible):
1. `public`
2. `protected internal`
3. `protected`
4. `internal`
5. `private protected`
6. `private`

## See Also

- [Explicit Pattern Documentation](./ExplicitPattern.md)
- [Configuration Options](./Configuration.md)
- [Test Examples](../tests/Linqraft.Tests/PropertyAccessibilityTest.cs)
