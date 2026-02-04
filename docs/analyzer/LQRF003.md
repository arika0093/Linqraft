# LQRF003: Generate API Response Methods

## Overview

The LQRF003 analyzer detects void or Task (non-generic) methods that contain unassigned Select operations with anonymous types on IQueryable sources. These methods can be automatically converted to async API response methods that return `Task<List<TDto>>`.

## Diagnostic Information

- **Diagnostic ID**: LQRF003
- **Title**: Method can be converted to API response method
- **Message**: Method '{0}' can be converted to an async API response method
- **Category**: Design
- **Severity**: Info
- **Enabled by Default**: Yes

## Description

When writing API methods, developers often start by writing "mockup" code with void or Task methods that contain a query but don't return anything. This analyzer identifies such patterns and offers a code fix to convert them into proper async API response methods.

### Triggering Conditions

The analyzer will report a diagnostic when ALL of the following conditions are met:

1. The method has a `void` or `Task` (non-generic) return type
2. The method contains a `.Select()` call with an anonymous type
3. The Select is called on an `IQueryable<T>` source (not IEnumerable<T>)
4. The Select result is **not** assigned to a variable (it's a standalone expression statement)

### Non-Triggering Conditions

The analyzer will NOT report a diagnostic when:

- The method returns `Task<T>` (already has a return type)
- The Select uses a named type instead of an anonymous type
- The Select is called on `IEnumerable<T>` instead of `IQueryable<T>`
- The Select result is assigned to a variable

## Code Fix

The code fix performs the following transformations:

1. Adds the `async` keyword to the method (if not already present)
2. Changes the return type from `void`/`Task` to `Task<List<TDto>>`
3. Appends "Async" suffix to the method name (if not already present)
4. Adds `return` keyword before the query
5. Adds `await` keyword before the query
6. Replaces `.Select(...)` with `.SelectExpr<TSource, TDto>(...)`
7. Adds `.ToListAsync()` at the end of the query
8. Adds necessary using directives

## Examples

### Example 1: Void Method

**Before:**

```csharp
using System.Linq;

class MyClass
{
    private readonly IDbContextFactory<MyAppDbContext> _dbContextFactory;

    public MyClass(IDbContextFactory<MyAppDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    void GetItems()  // LQRF003: Method 'GetItems' can be converted to an async API response method
    {
        using var dbContext = _dbContextFactory.CreateDbContext();
        dbContext.Items
            .Where(i => i.IsActive)
            .Select(i => new { i.Id, i.Name });
    }
}
```

**After applying code fix:**

```csharp
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

class MyClass
{
    private readonly IDbContextFactory<MyAppDbContext> _dbContextFactory;

    public MyClass(IDbContextFactory<MyAppDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    async Task<List<ItemDto_XXXXXXX>> GetItemsAsync()
    {
        using var dbContext = _dbContextFactory.CreateDbContext();
        return await dbContext.Items
            .Where(i => i.IsActive)
            .SelectExpr<Item, ItemDto_XXXXXXX>(i => new { i.Id, i.Name })
            .ToListAsync();
    }
}
```

### Example 2: Task Method

**Before:**

```csharp
async Task GetItems()  // LQRF003: Method 'GetItems' can be converted to an async API response method
{
    var list = new List<Item>();
    await list.AsQueryable()
        .Where(i => i.Id > 0)
        .Select(i => new { i.Id, i.Name });
}
```

**After applying code fix:**

```csharp
async Task<List<ItemDto_XXXXXXX>> GetItemsAsync()
{
    var list = new List<Item>();
    return await list.AsQueryable()
        .Where(i => i.Id > 0)
        .SelectExpr<Item, ItemDto_XXXXXXX>(i => new { i.Id, i.Name })
        .ToListAsync();
}
```

## Notes

- The generated DTO name follows the same naming conventions as other Linqraft analyzers
- The code fix automatically adds required using directives:
  - `System.Threading.Tasks`
  - `System.Collections.Generic`
  - `System.Linq`
  - `Microsoft.EntityFrameworkCore` (for ToListAsync)
- If the method name already ends with "Async", the suffix is not duplicated
- The code fix preserves formatting and indentation from the original code

## Related Analyzers

- **LQRS002**: IQueryable.Select can be converted to SelectExpr (anonymous)
- **LQRS003**: IQueryable.Select can be converted to SelectExpr (named DTO)
- **LQRF002**: Add ProducesResponseType to clarify API return type

## See Also

- [SelectExpr Usage Patterns](../library/usage-patterns.md)
- [Auto-Generated DTOs](../library/auto-generated-comments.md)
