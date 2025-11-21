# Linqraft Analyzers

This document describes the Roslyn analyzers included in the Linqraft.Analyzer package.

## Overview

Linqraft provides several analyzers with corresponding code fix providers to improve code quality and type safety when working with LINQ queries and anonymous types.

| Diagnostic ID | Analyzer | Description |
|--------------|----------|-------------|
| [LQRF001](#lqrf001-anonymoustypetodtoanalyzer) | AnonymousTypeToDtoAnalyzer | Detects anonymous types that can be converted to DTO classes |
| [LQRS001](#lqrs001-selectexprtotypedanalyzer) | SelectExprToTypedAnalyzer | Detects SelectExpr calls without type arguments |
| [LQRS002](#lqrs002-selecttoselectexpranonymousanalyzer) | SelectToSelectExprAnonymousAnalyzer | Detects IQueryable.Select with anonymous types that can be converted to SelectExpr |
| [LQRS003](#lqrs003-selecttoselectexprnamedanalyzer) | SelectToSelectExprNamedAnalyzer | Detects IQueryable.Select with named types that can be converted to SelectExpr |
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

**Severity:** Hidden
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

## LQRS002: SelectToSelectExprAnonymousAnalyzer

**Severity:** Info
**Category:** Design
**Default:** Enabled

### Description

This analyzer detects `IQueryable.Select()` calls with anonymous type projections that can be converted to `SelectExpr` for better performance and type safety. It only triggers for `IQueryable` (not `IEnumerable`).

### When it triggers

The analyzer reports a diagnostic when:

1. A `Select()` method is called on an `IQueryable<T>` source
2. The lambda body contains an anonymous type creation (`new { ... }`)

### Code Fixes

The `SelectToSelectExprAnonymousCodeFixProvider` provides two fix options:

1. **Convert to SelectExpr (anonymous pattern)** - Converts `Select` to `SelectExpr` keeping the anonymous type
2. **Convert to SelectExpr<T, TDto> (explicit DTO pattern)** - Converts to typed `SelectExpr` with generated DTO name

### Examples

#### Example 1: Anonymous pattern

**Before:**
```csharp
public class ProductRepository
{
    public void GetProducts(IQueryable<Product> query)
    {
        var result = query.Select(x => new  // LQRS002: IQueryable.Select with anonymous type can be converted to SelectExpr
        {
            x.Id,
            x.Name,
            x.Price
        });
    }
}
```

**After applying "Convert to SelectExpr (anonymous pattern)":**
```csharp
public class ProductRepository
{
    public void GetProducts(IQueryable<Product> query)
    {
        var result = query.SelectExpr(x => new
        {
            x.Id,
            x.Name,
            x.Price
        });
    }
}
```

**After applying "Convert to SelectExpr<T, TDto> (explicit DTO pattern)":**
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

### When it doesn't trigger

- `IEnumerable.Select()` calls (only `IQueryable` is supported)
- `Select()` calls without anonymous types
- `Select()` calls with named type projections (handled by LQRS003)

---

## LQRS003: SelectToSelectExprNamedAnalyzer

**Severity:** Info
**Category:** Design
**Default:** Enabled

### Description

This analyzer detects `IQueryable.Select()` calls with named/predefined type projections that can be converted to `SelectExpr` for better performance and type safety. It only triggers for `IQueryable` (not `IEnumerable`).

### When it triggers

The analyzer reports a diagnostic when:

1. A `Select()` method is called on an `IQueryable<T>` source
2. The lambda body contains an object creation expression with a named type (`new SomeDto { ... }`)

### Code Fixes

The `SelectToSelectExprNamedCodeFixProvider` provides two fix options:

1. **Convert to SelectExpr<T, TDto> (explicit DTO pattern)** - Converts to typed `SelectExpr` with generated DTO name
2. **Convert to SelectExpr (predefined DTO pattern)** - Converts `Select` to `SelectExpr` keeping the named type

### Examples

#### Example 1: Predefined DTO pattern

**Before:**
```csharp
public class ProductDto
{
    public int Id { get; set; }
    public string Name { get; set; }
}

public class ProductRepository
{
    public void GetProducts(IQueryable<Product> query)
    {
        var result = query.Select(x => new ProductDto  // LQRS003: IQueryable.Select with named type can be converted to SelectExpr
        {
            Id = x.Id,
            Name = x.Name
        });
    }
}
```

**After applying "Convert to SelectExpr (predefined DTO pattern)":**
```csharp
public class ProductDto
{
    public int Id { get; set; }
    public string Name { get; set; }
}

public class ProductRepository
{
    public void GetProducts(IQueryable<Product> query)
    {
        var result = query.SelectExpr(x => new ProductDto
        {
            Id = x.Id,
            Name = x.Name
        });
    }
}
```

**After applying "Convert to SelectExpr<T, TDto> (explicit DTO pattern)":**
```csharp
public class ProductDto
{
    public int Id { get; set; }
    public string Name { get; set; }
}

public class ProductRepository
{
    public void GetProducts(IQueryable<Product> query)
    {
        var result = query.SelectExpr<Product, ResultDto_XXXXXXXX>(x => new ProductDto
        {
            Id = x.Id,
            Name = x.Name
        });
    }
}
```

### When it doesn't trigger

- `IEnumerable.Select()` calls (only `IQueryable` is supported)
- `Select()` calls without object creation
- `Select()` calls with anonymous types (handled by LQRS002)

---

## LQRE001: LocalVariableCaptureAnalyzer

**Severity:** Error
**Category:** Usage
**Default:** Enabled

### Description

This analyzer detects when local variables, method parameters, instance/static fields/properties, or const fields are referenced inside `SelectExpr` lambda expressions without being passed via the `capture` parameter. This ensures complete isolation of the selector expression, which is required for Linqraft to properly generate expression trees.

### When it triggers

The analyzer reports a diagnostic when it finds references to:

- Local variables from outer scope (except const local variables)
- Method parameters from outer scope
- Instance fields and properties
- Static fields and properties
- Const fields (class-level constants)
- `this.Property` member access
- `ClassName.StaticMember` access

that are used inside a `SelectExpr` lambda but are not included in the `capture` parameter.

### When it doesn't trigger

- Const local variables (e.g., `const int X = 10` declared in a method)
- Lambda parameters themselves
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

#### Example 2: Instance fields, static fields, and const fields

**Before:**
```csharp
public class ProductService
{
    private int DefaultDiscount { get; set; } = 10;
    private string Currency = "USD";
    public const double TaxRate = 0.1;

    public void GetProducts(IQueryable<Product> query)
    {
        var result = query.SelectExpr(x => new  // LQRE001 on all three
        {
            x.Id,
            x.Price,
            Discount = DefaultDiscount,
            CurrencyCode = Currency,
            Tax = TaxRate
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
        capture: new { Currency, DefaultDiscount, TaxRate });
    }
}
```

#### Example 3: this.Property and static member access

**Before:**
```csharp
public class OrderService
{
    private string Status { get; set; } = "Active";

    public void GetOrders(IQueryable<Order> query)
    {
        var result = query.SelectExpr(x => new  // LQRE001 on both
        {
            x.Id,
            OrderStatus = this.Status,
            DefaultValue = Settings.DefaultValue
        });
    }
}
```

**After applying fix:**
```csharp
public class OrderService
{
    private string Status { get; set; } = "Active";

    public void GetOrders(IQueryable<Order> query)
    {
        var result = query.SelectExpr(x => new
        {
            x.Id,
            OrderStatus = this.Status,
            DefaultValue = Settings.DefaultValue
        },
        capture: new { DefaultValue, Status });
    }
}
```

#### Example 4: Incomplete capture parameter

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
- **All external references must be captured** for complete isolation of the selector expression, including:
  - Instance members (even when accessed via `this`)
  - Static members (even when accessed via `ClassName.Member`)
  - Const fields (class-level constants, though const local variables don't need capture)
- The code fix automatically merges any existing capture variables with newly detected ones
- Variables are alphabetically sorted in the generated capture parameter for consistency
