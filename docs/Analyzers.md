# Linqraft Analyzers

This document describes the Roslyn analyzers included in the Linqraft.Analyzer package.

## Overview

Linqraft provides two analyzers with corresponding code fix providers to improve code quality and type safety when working with LINQ queries and anonymous types.

| Diagnostic ID | Analyzer | Description |
|--------------|----------|-------------|
| [LQRF001](#lqrf001-anonymoustypetodtoanalyzer) | AnonymousTypeToDtoAnalyzer | Detects anonymous types that can be converted to DTO classes |
| [LQRS001](#lqrs001-selectexprtotypedanalyzer) | SelectExprToTypedAnalyzer | Detects SelectExpr calls without type arguments |
| [LQRE001](#lqre001-localvariablecaptureanalyzer) | LocalVariableCaptureAnalyzer | Detects local variables used in SelectExpr without capture parameter |

---

## LQRF001: AnonymousTypeToDtoAnalyzer

**Severity:** Hidden
**Category:** Design
**Default:** Enabled

### Description

This analyzer detects anonymous type usages that can be converted to strongly-typed DTO classes for better type safety and reusability.

### When it triggers

The analyzer reports a diagnostic when it finds an anonymous type (`new { ... }`) in convertible contexts:

- Variable declarations: `var result = new { ... }`
- Return statements: `return new { ... }`
- Yield return statements: `yield return new { ... }`
- Assignments: `x = new { ... }`
- Method arguments: `Method(new { ... })`
- Array initializers: `new[] { new { ... } }`
- Conditional expressions: `condition ? new { ... } : other`
- Lambda bodies: `x => new { ... }`

### When it doesn't trigger

- Empty anonymous types (no initializers)
- Anonymous types inside `SelectExpr<T, TDto>()` calls (already handled by Linqraft)

### Code Fixes

The `AnonymousTypeToDtoCodeFixProvider` offers two fix options:

1. **Convert to Class (add to current file)** - Generates the DTO class at the end of the current file
2. **Convert to Class (new file)** - Creates a new file for the DTO class

### Example

**Before:**
```csharp
public class UserService
{
    public object GetUserInfo(User user)
    {
        return new  // LQRF001: Anonymous type can be converted to DTO
        {
            user.Id,
            user.Name,
            user.Email
        };
    }
}
```

**After applying fix:**
```csharp
public class UserService
{
    public object GetUserInfo(User user)
    {
        return new UserInfoDto
        {
            Id = user.Id,
            Name = user.Name,
            Email = user.Email
        };
    }
}

public class UserInfoDto
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Email { get; set; }
}
```

### DTO Naming Convention

The generated DTO class name is inferred from context:

- Variable name: `var userInfo = new { ... }` → `UserInfoDto`
- Assignment target: `userInfo = new { ... }` → `UserInfoDto`
- Method name: `GetUserInfo()` → `UserInfoDto` (strips "Get" prefix)
- Default fallback: `GeneratedDto`

---

## LQRS001: SelectExprToTypedAnalyzer

**Severity:** Info
**Category:** Design
**Default:** Enabled

### Description

This analyzer detects `SelectExpr` calls without explicit type arguments that can be converted to typed versions (`SelectExpr<T, TDto>`) for better type safety.

### When it triggers

The analyzer reports a diagnostic when:

1. A `SelectExpr()` method is called without type arguments
2. The lambda body contains an anonymous type

### Code Fixes

The `SelectExprToTypedCodeFixProvider` provides:

- **Convert to typed SelectExpr** - Adds type arguments to the SelectExpr call

### Example

**Before:**
```csharp
public class ProductRepository
{
    public void GetProducts(IQueryable<Product> query)
    {
        var result = query.SelectExpr(x => new  // LQRS001: SelectExpr can be converted to SelectExpr<Product, ResultDto_XXXXXXXX>
        {
            x.Id,
            x.Name,
            x.Price
        });
    }
}
```

**After applying fix:**
```csharp
public class ProductRepository
{
    public void GetProducts(IQueryable<Product> query)
    {
        var result = query.SelectExpr<Product, ResultDto_XXXXXXXX>(x => new
        {
            x.Id,
            x.Name,
            x.Price
        });
    }
}
```

### DTO Naming Convention

The generated DTO name follows these rules:

1. Variable name: `var products = query.SelectExpr(...)` → `ProductsDto_HASH`
2. Assignment target: `products = query.SelectExpr(...)` → `ProductsDto_HASH`
3. Method name: `GetProducts()` → `ProductsDto_HASH` (strips "Get" prefix)
4. Default fallback: `ResultDto_HASH`

The hash suffix (8 characters, A-Z and 0-9) is generated from the property names using the FNV-1a algorithm to ensure uniqueness.

---

## LQRE001: LocalVariableCaptureAnalyzer

**Severity:** Error
**Category:** Usage
**Default:** Enabled

### Description

This analyzer detects when local variables, method parameters, or instance fields/properties are referenced inside `SelectExpr` lambda expressions without being passed via the `capture` parameter. This is required for Linqraft to properly generate expression trees with captured variables.

### When it triggers

The analyzer reports a diagnostic when it finds references to:

- Local variables from outer scope
- Method parameters
- Instance fields (non-static)
- Instance properties (non-static)

that are used inside a `SelectExpr` lambda but are not included in the `capture` parameter.

### When it doesn't trigger

- Constants (compile-time values like `const int X = 10`)
- Lambda parameters themselves
- Static fields and properties
- Variables already included in the `capture` parameter

### Code Fixes

The `LocalVariableCaptureCodeFixProvider` provides:

- **Add capture parameter** - Automatically adds or updates the `capture` parameter with all required variables

### Examples

#### Example 1: Local variables

**Before:**
```csharp
public void GetProducts(IQueryable<Product> query)
{
    var multiplier = 10;
    var result = query.SelectExpr(x => new  // LQRE001: Local variable 'multiplier' is used...
    {
        x.Id,
        AdjustedPrice = x.Price * multiplier
    });
}
```

**After applying fix:**
```csharp
public void GetProducts(IQueryable<Product> query)
{
    var multiplier = 10;
    var result = query.SelectExpr(x => new
    {
        x.Id,
        AdjustedPrice = x.Price * multiplier
    },
    capture: new { multiplier });
}
```

#### Example 2: Instance fields and properties

**Before:**
```csharp
public class ProductService
{
    private int DefaultDiscount { get; set; } = 10;
    private string Currency = "USD";
    public const double TaxRate = 0.1;

    public void GetProducts(IQueryable<Product> query)
    {
        var result = query.SelectExpr(x => new  // LQRE001 on DefaultDiscount and Currency
        {
            x.Id,
            x.Price,
            Discount = DefaultDiscount,
            CurrencyCode = Currency,
            Tax = TaxRate  // No error - const field
        });
    }
}
```

**After applying fix:**
```csharp
public class ProductService
{
    private int DefaultDiscount { get; set; } = 10;
    private string Currency = "USD";
    public const double TaxRate = 0.1;

    public void GetProducts(IQueryable<Product> query)
    {
        var result = query.SelectExpr(x => new
        {
            x.Id,
            x.Price,
            Discount = DefaultDiscount,
            CurrencyCode = Currency,
            Tax = TaxRate
        },
        capture: new { Currency, DefaultDiscount });
    }
}
```

#### Example 3: Incomplete capture parameter

**Before:**
```csharp
public void GetData(IQueryable<Entity> query)
{
    var local1 = 10;
    var local2 = "World";
    var result = query.SelectExpr(x => new  // LQRE001: Local variable 'local2' is used...
    {
        Value1 = x.Value + local1,
        Value2 = x.Name + local2
    },
    capture: new { local1 });  // Missing local2
}
```

**After applying fix:**
```csharp
public void GetData(IQueryable<Entity> query)
{
    var local1 = 10;
    var local2 = "World";
    var result = query.SelectExpr(x => new
    {
        Value1 = x.Value + local1,
        Value2 = x.Name + local2
    },
    capture: new { local1, local2 });  // Both variables now captured
}
```

### Important Notes

- The `capture` parameter is required for Linqraft to properly generate expression trees that can be used with Entity Framework Core and other LINQ providers
- Instance members (fields/properties) must be captured even though they're accessible via `this` because expression trees need explicit closure
- Constants don't need to be captured as they are inlined at compile time
- The code fix automatically merges any existing capture variables with newly detected ones
