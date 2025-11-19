# ApplyDiff API Design Document

## Overview

When using EF Core with short-lived DbContext instances (e.g., Singleton IDbContextFactory), the ChangeTracker cannot be used effectively, making it cumbersome to reflect DTO changes back to the database. To solve this problem, we leverage Linqraft's auto-generation process to provide an API that reflects only the changed properties from DTOs to the database.

## Background & Problem

### Current Pain Points

1. When DbContext is short-lived, ChangeTracker-based change tracking is unavailable
2. Manual mapping from DTOs to entities is tedious
3. Implementing partial updates (only changed properties) is cumbersome
4. Nested objects (child collections) with add/update/delete operations are particularly complex

### Solution Approach

Adopt the Immutable + Diff pattern:
- Generate DTOs as records, express changes using with-expressions
- Detect and apply differences between original and modified
- No issues with serialization/deserialization when passing to other services via API

## API Design

### Basic Usage Flow

```csharp
// 1. Fetch (generate DTO for update)
var original = await query
    .Where(x => x.Id == id)
    .SelectExprForUpdate<OrderDto>(x => new {
        x.Id,
        x.Name,
        CustomerName = x.Customer.Details.Name,
        Items = x.Items.Select(i => new {
            i.Id,
            i.ProductName,
            i.Quantity
        }).ToList()
    })
    .FirstAsync();

// 2. Modify (using record with-expression)
var modified = original with {
    Name = "New Name",
    CustomerName = "New Customer",
    Items = original.Items
        .Where(i => i.Id != 3)  // Delete
        .Select(i => i.Id == 1 ? i with { Quantity = 10 } : i)  // Update
        .Append(new OrderItemDto { ProductName = "New", Quantity = 1 })  // Add
        .ToList()
};

// 3. Apply
await using var ctx = await dbContextFactory.CreateDbContextAsync();
await ctx.ApplyDiffAsync(modified, original);
```

### Method List

| Method | Description |
|--------|-------------|
| `SelectExprForUpdate<TDto>` | Fetch DTO for update. Also auto-generates Include information |
| `ApplyDiffAsync(modified, original)` | Apply only the differences |
| `ApplyAllAsync(modified)` | Overwrite all properties |

### Argument Order

Adopt `(modified, original)` order:
- `modified` is the main subject (what to apply)
- `original` is optional reference information
- Consistency with `ApplyAllAsync(modified)`

### Automatic Predicate Inference

Since the primary key is known, the Where clause predicate is auto-generated:

```csharp
// Internally generates x => x.Id == original.Id
await ctx.ApplyDiffAsync(modified, original);
```

## Technical Details

### Primary Key Detection

Auto-detection (priority order):
1. `[Key]` attribute
2. `[PrimaryKey]` attribute
3. Convention (`Id` or `{TypeName}Id`)

Manual specification (e.g., when using ModelBuilder):

```csharp
// Specify with attribute
[LinqraftKey(nameof(Id))]
public partial record OrderDto { ... }

// Or specify at SelectExprForUpdate
query.SelectExprForUpdate<OrderDto>(
    x => new { ... },
    options => options.WithKey(x => x.Id)
);
```

### New Item Identification

Items with default primary key value (`Id == default(TKey)`) are treated as new items:
- `int` → `0`
- `Guid` → `Guid.Empty`
- `long` → `0L`

### Automatic Include Generation

Auto-generate necessary Include/ThenInclude from paths referenced in `SelectExprForUpdate`:

```csharp
// Auto-generated from SelectExprForUpdate content
query
    .Include(x => x.Customer)
        .ThenInclude(x => x.Details)
    .Include(x => x.Items)
        .ThenInclude(x => x.Product)
            .ThenInclude(x => x.Supplier);
```

### Behavior When No Changes

Do nothing (no database round-trip). Return 0.

### Persistence Method

Adopt SaveChanges-based approach:
- Compatibility with Interceptors
- Automatic foreign key setting when adding child entities
- Support for optimistic concurrency control

```csharp
// Internal implementation outline
var entity = await dbContext.Set<Order>()
    .Include(...)  // Auto-generated Include
    .FirstAsync(x => x.Id == original.Id);

// Update properties
if (original.Name != modified.Name)
    entity.Name = modified.Name;

// Diff processing for child collections
// ...

await dbContext.SaveChangesAsync();
```

## Generated Code

### DTO (record)

```csharp
public partial record OrderDto
{
    public int Id { get; init; }
    public string Name { get; init; }
    public string CustomerName { get; init; }
    public List<OrderItemDto> Items { get; init; }
}

public partial record OrderItemDto
{
    public int Id { get; init; }
    public string ProductName { get; init; }
    public int Quantity { get; init; }
}
```

### Metadata & Extension Methods

```csharp
public static class OrderDtoExtensions
{
    // Include information
    public static IQueryable<Order> ApplyIncludes(IQueryable<Order> query)
    {
        return query
            .Include(x => x.Customer)
                .ThenInclude(x => x.Details)
            .Include(x => x.Items);
    }

    // ApplyDiffAsync
    public static async Task<int> ApplyDiffAsync(
        this DbContext dbContext,
        OrderDto modified,
        OrderDto original)
    {
        var entity = await ApplyIncludes(dbContext.Set<Order>())
            .FirstAsync(x => x.Id == original.Id);

        // Update parent properties
        if (original.Name != modified.Name)
            entity.Name = modified.Name;

        if (original.CustomerName != modified.CustomerName)
        {
            if (entity.Customer?.Details == null)
                throw new InvalidOperationException(
                    "Cannot update Customer.Details.Name: path is null");
            entity.Customer.Details.Name = modified.CustomerName;
        }

        // Diff child collection
        var originalDict = original.Items.ToDictionary(x => x.Id);
        var modifiedDict = modified.Items.ToDictionary(x => x.Id);

        // Delete
        var toRemove = entity.Items
            .Where(e => !modifiedDict.ContainsKey(e.Id))
            .ToList();
        foreach (var item in toRemove)
            entity.Items.Remove(item);

        // Update
        foreach (var entityItem in entity.Items)
        {
            if (modifiedDict.TryGetValue(entityItem.Id, out var mod) &&
                originalDict.TryGetValue(entityItem.Id, out var orig))
            {
                if (orig.ProductName != mod.ProductName)
                    entityItem.ProductName = mod.ProductName;
                if (orig.Quantity != mod.Quantity)
                    entityItem.Quantity = mod.Quantity;
            }
        }

        // Add (EF auto-sets foreign key)
        foreach (var mod in modified.Items.Where(x => x.Id == default))
        {
            entity.Items.Add(new OrderItem {
                ProductName = mod.ProductName,
                Quantity = mod.Quantity
            });
        }

        return await dbContext.SaveChangesAsync();
    }

    // ApplyAllAsync
    public static async Task<int> ApplyAllAsync(
        this DbContext dbContext,
        OrderDto modified)
    {
        var entity = await ApplyIncludes(dbContext.Set<Order>())
            .FirstAsync(x => x.Id == modified.Id);

        entity.Name = modified.Name;
        // ... overwrite all properties

        return await dbContext.SaveChangesAsync();
    }
}
```

## Constraints & Validation

### Deep Nesting in Child Collections

Deep nesting (2+ levels) within child collections (e.g., `i.Product.Supplier.Name`) is prohibited because it causes issues when adding new items:

```csharp
// ❌ Error
Items = x.Items.Select(i => new {
    i.Id,
    SupplierName = i.Product.Supplier.Name  // Prohibited
})
```

### Circular References

Paths containing circular references are prohibited:

```csharp
// ❌ Error
ParentName = x.Parent.Child.Parent.Name  // Circular
```

### Validation Timing

The ultimate goal is to detect these at compile time with Analyzer errors.

| Code | Description | Level |
|------|-------------|-------|
| LQ001 | Deep nesting in child collection | Error |
| LQ002 | Circular reference | Error |
| LQ003 | Non-updatable path | Error |

## Implementation Priority

### Phase 1: MVP

1. Basic `SelectExprForUpdate` implementation
   - record DTO generation
   - Auto-detect primary key (`[Key]`, `[PrimaryKey]`, convention)
   - Auto-generate Include information
2. `ApplyDiffAsync(modified, original)` - flat properties only
3. `ApplyAllAsync(modified)` - full overwrite

### Phase 2: Nested Object Support

1. Deep nested parent updates (`x.Customer.Details.Name`)
2. Child collection add/update/delete
3. Null check + runtime exception

### Phase 3: Extensions

1. Array support
   ```csharp
   await ctx.ApplyDiffAsync(modifiedList, originalList);
   ```
2. Manual primary key specification option
3. Optimistic concurrency (ConcurrencyToken)

### Phase 4: Quality Improvements (Lowest Priority)

1. Analyzer errors (LQ001, LQ002, LQ003)
2. Circular reference detection
3. More detailed error messages

## Array Support (Future)

```csharp
await ctx.ApplyDiffAsync(modifiedList, originalList);

// Internal implementation
var ids = modifiedList.Select(x => x.Id).ToList();
var entities = await dbContext.Set<Order>()
    .Include(...)
    .Where(x => ids.Contains(x.Id))
    .ToListAsync();

var originalDict = originalList.ToDictionary(x => x.Id);

foreach (var modified in modifiedList)
{
    var original = originalDict[modified.Id];
    var entity = entities.First(x => x.Id == modified.Id);
    // Apply diff...
}

await dbContext.SaveChangesAsync();  // Bulk save at the end
```

## References

- Existing `SelectExpr` implementation: `src/Linqraft.SourceGenerator/SelectExprInfo.cs`
- Interceptor mechanism: `src/Linqraft.SourceGenerator/SelectExprGenerator.cs`

## Future Considerations

1. **INotifyPropertyChanged auto-implementation**: Option to add change notification to DTOs (as a separate feature)
2. **Optimistic concurrency**: Handling of `[ConcurrencyCheck]` and `[Timestamp]`
3. **Batch update optimization**: Efficient processing of large data volumes
