# LQRF004: Generate Synchronous API Response Methods

## Overview

The LQRF004 analyzer detects void methods that contain unassigned Select operations with anonymous types on **IQueryable** sources. These methods can be automatically converted to synchronous API response methods that return `List<TDto>`.

This analyzer is the synchronous version of LQRF003, designed for non-EntityFramework scenarios or when async/await is not needed.

## Diagnostic Information

- **Diagnostic ID**: LQRF004
- **Title**: Method can be converted to synchronous API response method
- **Message**: Method '{0}' can be converted to a synchronous API response method
- **Category**: Design
- **Severity**: Info
- **Enabled by Default**: Yes

## Description

When writing API methods, developers often start by writing "mockup" code with void methods that contain a query but don't return anything. This analyzer identifies such patterns and offers a code fix to convert them into proper synchronous API response methods.

### Triggering Conditions

The analyzer will report a diagnostic when ALL of the following conditions are met:

1. The method has a `void` return type (not Task)
2. The method contains a `.Select()` call with an anonymous type
3. The Select is called on an `IQueryable<T>` source (not IEnumerable<T>)
4. The Select result is **not** assigned to a variable (it's a standalone expression statement)

### Non-Triggering Conditions

The analyzer will NOT report a diagnostic when:

- The method returns any type other than void
- The Select uses a named type instead of an anonymous type
- The Select is called on `IEnumerable<T>` instead of `IQueryable<T>`
- The Select result is assigned to a variable

## Code Fix

The code fix performs the following transformations:

1. Changes the return type from `void` to `List<TDto>`
2. Adds `return` keyword before the query
3. Replaces `.Select(...)` with `.SelectExpr<TSource, TDto>(...)`
4. Adds `.ToList()` at the end of the query
5. Adds necessary using directives

**Note:** Unlike LQRF003, this analyzer does NOT:
- Add `async` keyword
- Add `await` keyword
- Use `ToListAsync()` 
- Add `Microsoft.EntityFrameworkCore` using directive

## Examples

### Example 1: Void Method with IQueryable

**Before:**

```csharp
using System.Linq;
using System.Collections.Generic;

class Item
{
    public int Id { get; set; }
    public string Name { get; set; }
    public bool IsActive { get; set; }
}

class MyClass
{
    void GetItems()  // LQRF004: Method 'GetItems' can be converted to a synchronous API response method
    {
        var list = new List<Item>();
        list.AsQueryable()
            .Where(i => i.IsActive)
            .Select(i => new { i.Id, i.Name });
    }
}
```

**After applying code fix:**

```csharp
using System.Linq;
using System.Collections.Generic;

class Item
{
    public int Id { get; set; }
    public string Name { get; set; }
    public bool IsActive { get; set; }
}

class MyClass
{
    List<ItemsDto_XXXXXXX> GetItems()
    {
        var list = new List<Item>();
        return list.AsQueryable()
            .Where(i => i.IsActive)
            .SelectExpr<Item, ItemsDto_XXXXXXX>(i => new { i.Id, i.Name }).ToList();
    }
}
```

### Example 2: Void Method with Chained Operations

**Before:**

```csharp
using System.Linq;
using System.Collections.Generic;

class MyClass
{
    void GetActiveItems()  // LQRF004: Method 'GetActiveItems' can be converted to a synchronous API response method
    {
        var items = new List<Item>();
        items.AsQueryable()
            .Where(i => i.Id > 0)
            .OrderBy(i => i.Name)
            .Select(i => new { i.Id, i.Name });
    }
}
```

**After applying code fix:**

```csharp
using System.Linq;
using System.Collections.Generic;

class MyClass
{
    List<ItemDto_XXXXXXX> GetActiveItems()
    {
        var items = new List<Item>();
        return items.AsQueryable()
            .Where(i => i.Id > 0)
            .OrderBy(i => i.Name)
            .SelectExpr<Item, ItemDto_XXXXXXX>(i => new { i.Id, i.Name }).ToList();
    }
}
```

## Notes

- This analyzer works with **any IQueryable** source, not just EntityFramework DbSets
- The generated DTO name follows the same naming conventions as other Linqraft analyzers
- The code fix automatically adds required using directives:
  - `System.Linq`
  - `System.Collections.Generic`
- The code fix preserves formatting and indentation from the original code
- **No async/await** is used, making this suitable for synchronous scenarios

## Comparison with LQRF003

| Feature | LQRF003 (Async) | LQRF004 (Sync) |
|---------|-----------------|----------------|
| Return Type | `Task<List<TDto>>` | `List<TDto>` |
| Async Keyword | ✅ Yes | ❌ No |
| Await Keyword | ✅ Yes | ❌ No |
| ToList Method | `ToListAsync()` | `ToList()` |
| EF Core Required | ✅ Yes | ❌ No |
| DbSet Required | ✅ Yes | ❌ No |
| IQueryable Support | DbSet only | Any IQueryable |
| Suitable For | EF Core async operations | Synchronous operations |

## Related Analyzers

- **LQRF003**: Convert method to async API response method (EntityFramework, async version)
- **LQRS002**: IQueryable.Select can be converted to SelectExpr (anonymous)
- **LQRS003**: IQueryable.Select can be converted to SelectExpr (named DTO)
- **LQRF002**: Add ProducesResponseType to clarify API return type

## See Also

- [SelectExpr Usage Patterns](../library/usage-patterns.md)
- [Auto-Generated DTOs](../library/auto-generated-comments.md)
