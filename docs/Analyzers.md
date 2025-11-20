# Linqraft Analyzers

This document describes the Roslyn analyzers included in the Linqraft.Analyzer package.

## Overview

Linqraft provides two analyzers with corresponding code fix providers to improve code quality and type safety when working with LINQ queries and anonymous types.

| Diagnostic ID | Analyzer | Description |
|--------------|----------|-------------|
| [LQRF001](#lqrf001-anonymoustypetodtoanalyzer) | AnonymousTypeToDtoAnalyzer | Detects anonymous types that can be converted to DTO classes |
| [LQRS001](#lqrs001-selectexprtotypedanalyzer) | SelectExprToTypedAnalyzer | Detects SelectExpr calls without type arguments |

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
