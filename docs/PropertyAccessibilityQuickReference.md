# Property Accessibility Control - Quick Reference

## Two Approaches to Control Property Accessibility

### Approach A: Predefined Properties (Recommended for Most Cases)

**When to use:**
- You want simple, type-safe C# code
- You prefer using standard accessibility modifiers
- You're defining properties in a separate partial class file

**Example:**
```csharp
// File: MyDtos.cs
public partial class UserDto
{
    internal int UserId { get; set; }
    internal string Email { get; set; } = "";
    
    // Helper methods using internal properties
    public bool IsValid() => UserId > 0 && !string.IsNullOrEmpty(Email);
}

// File: UserService.cs
var users = dbContext.Users
    .SelectExpr<User, UserDto>(u => new
    {
        UserId = u.Id,
        u.Email,
        u.Name,
        u.Role,
    })
    .ToList();

// Generated code (in UserDto):
// public required string Name { get; set; }
// public required string Role { get; set; }
```

**Pros:**
- ✅ Simple and intuitive
- ✅ Compile-time type safety
- ✅ Standard C# syntax
- ✅ IDE fully understands accessibility

**Cons:**
- ❌ Requires separate property definitions
- ❌ Properties hidden from generated code view

---

### Approach B: Attribute-Based (Recommended for Explicit Marking)

**When to use:**
- You want to keep all properties visible in one place
- You need to override public properties to be internal
- You prefer explicit marking over implicit definitions

**Example:**
```csharp
using Linqraft;

// File: MyDtos.cs
public partial class UserDto
{
    [LinqraftAccessibility("internal")]
    public int UserId { get; set; }
    
    [LinqraftAccessibility("internal")]
    public string Email { get; set; } = "";
    
    // No attribute = uses actual accessibility (public)
    public string Name { get; set; } = "";
    public string Role { get; set; } = "";
    
    // Helper methods using internal properties
    public bool IsValid() => UserId > 0 && !string.IsNullOrEmpty(Email);
}

// File: UserService.cs
var users = dbContext.Users
    .SelectExpr<User, UserDto>(u => new
    {
        UserId = u.Id,
        u.Email,
        u.Name,
        u.Role,
    })
    .ToList();

// Generated code (in UserDto):
// public required string Name { get; set; }
// public required string Role { get; set; }
```

**Pros:**
- ✅ All properties visible in definition
- ✅ Can mark public properties as internal
- ✅ Explicit and self-documenting
- ✅ Easier to see all properties at once

**Cons:**
- ❌ Requires attribute (slight verbosity)
- ❌ String-based (not compile-time checked)
- ❌ IDE may show public property even if marked internal

---

## Combined Usage

You can mix both approaches in the same DTO:

```csharp
using Linqraft;

public partial class ProductDto
{
    // Approach B: Attribute-based
    [LinqraftAccessibility("internal")]
    public int ProductId { get; set; }
    
    // Approach A: Actual accessibility
    internal decimal Cost { get; set; }
    
    // Default: public (no attribute, public declaration)
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
}
```

## Which Approach Should I Use?

| Scenario | Recommended Approach | Reason |
|----------|---------------------|--------|
| Simple internal properties | **A: Predefined** | Simplest, most idiomatic C# |
| Need to see all properties together | **B: Attribute** | Better visibility |
| Multiple files (DTOs in separate file) | **A: Predefined** | Natural separation |
| Override public to internal | **B: Attribute** | Can't do with A alone |
| Library development | **A: Predefined** | More explicit control |
| Quick prototyping | **B: Attribute** | Less file switching |
| Team preference for attributes | **B: Attribute** | Explicit marking |
| Team preference for C# syntax | **A: Predefined** | Standard modifiers |

## Valid Accessibility Values (for Attribute)

- `"public"` - Accessible everywhere
- `"internal"` - Accessible only within the assembly
- `"protected"` - Accessible within class and derived classes
- `"protected internal"` - Accessible within assembly or derived classes
- `"private protected"` - Accessible within class or derived classes in same assembly
- `"private"` - Accessible only within the class

## Real-World Example

```csharp
using Linqraft;

// Scenario: Building an API with internal audit fields
public partial class OrderDto
{
    // Public API fields (will be generated as public)
    public int OrderId { get; set; }
    public string CustomerName { get; set; } = "";
    public decimal TotalAmount { get; set; }
    
    // Internal audit fields (marked via attribute)
    [LinqraftAccessibility("internal")]
    public DateTime CreatedAt { get; set; }
    
    [LinqraftAccessibility("internal")]
    public string CreatedBy { get; set; } = "";
    
    [LinqraftAccessibility("internal")]
    public DateTime? ModifiedAt { get; set; }
    
    // Business logic using internal fields
    public bool IsRecentlyModified()
    {
        return ModifiedAt.HasValue 
            && (DateTime.UtcNow - ModifiedAt.Value).TotalHours < 24;
    }
}

// Query
var orders = dbContext.Orders
    .SelectExpr<Order, OrderDto>(o => new
    {
        o.OrderId,
        o.CustomerName,
        o.TotalAmount,
        o.CreatedAt,
        o.CreatedBy,
        o.ModifiedAt,
    })
    .ToList();

// Generated code only includes public properties:
// public required int OrderId { get; set; }
// public required string CustomerName { get; set; }
// public required decimal TotalAmount { get; set; }
```

## Getting Started

1. **Choose your approach** based on your needs
2. **Define your DTO** with accessibility control
3. **Use SelectExpr** as normal - the generator handles the rest
4. **Extend with methods** that use internal properties as needed

Both approaches are production-ready and fully tested!
