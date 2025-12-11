# EFCore Query (Pre)Compilation Support

This guide explains how to use Linqraft with Entity Framework Core's query (pre)compilation features for improved performance and NativeAOT compatibility.

## Overview

EF Core provides two types of query optimization:

1. **Compiled Queries** - Queries pre-built within the program using `EF.CompileAsyncQuery`
2. **Precompiled Queries** (EF9+) - Queries pre-built at build time for NativeAOT support

Linqraft traditionally uses interceptors to generate efficient Select expressions, but interceptors are not compatible with these compilation features. The `[LinqraftMappingGenerate]` attribute provides an alternative approach that generates extension methods instead of interceptors, making it compatible with both compilation strategies.

## The Problem

Traditional Linqraft `SelectExpr` usage relies on interceptors:

```csharp
// This works normally but fails with query (pre)compilation
var query = dbContext.Orders
    .SelectExpr<Order, OrderDto>(o => new
    {
        o.Id,
        CustomerName = o.Customer?.Name,
    });
```

**Issues:**
- Interceptors conflict with EF's query compilation
- Cannot use `?.` operator in `EF.CompileAsyncQuery` (requires Expression trees)
- Precompiled queries fail to parse interceptor-based code

## The Solution: LinqraftMappingGenerate

Use the `[LinqraftMappingGenerate]` attribute to generate extension methods instead of interceptors:

### Step 1: Create a Static Partial Class

```csharp
public static partial class OrderQueries
{
    // This method is never called - it's just a template for code generation
    [LinqraftMappingGenerate("ProjectToDto")]
    internal static IQueryable<OrderDto> DummyQuery(this IQueryable<Order> source) => source
        .SelectExpr<Order, OrderDto>(o => new
        {
            o.Id,
            CustomerName = o.Customer?.Name,  // ?.operator works!
            Items = o.Items.Select(i => new
            {
                i.ProductName,
                i.Quantity,
            }),
        });
}
```

### Step 2: Use the Generated Extension Method

Linqraft generates a `ProjectToDto` extension method in the same class:

```csharp
// Generated code (simplified):
public static partial class OrderQueries
{
    internal static IQueryable<OrderDto> ProjectToDto(
        this IQueryable<Order> source)
    {
        return source.Select(o => new OrderDto
        {
            Id = o.Id,
            CustomerName = o.Customer != null ? o.Customer.Name : null,
            Items = o.Items.Select(i => new OrderItemDto
            {
                ProductName = i.ProductName,
                Quantity = i.Quantity,
            }),
        });
    }
}
```

### Step 3: Use in Your Code

```csharp
// Regular usage
var orders = await dbContext.Orders
    .ProjectToDto()
    .ToListAsync();

// With compiled queries (EF Core 2.0+)
private static readonly Func<MyDbContext, Task<List<OrderDto>>> GetOrdersCompiled =
    EF.CompileAsyncQuery((MyDbContext db) => db.Orders.ProjectToDto().ToList());

var orders = await GetOrdersCompiled(dbContext);

// With precompiled queries (EF9+)
// Just use normally - EF9 will precompile at build time
var orders = await dbContext.Orders
    .ProjectToDto()
    .ToListAsync();
```

## Key Features

### 1. Full SelectExpr Syntax Support

The dummy method supports all Linqraft features:

```csharp
[LinqraftMappingGenerate("ProjectToFullDto")]
internal static IQueryable<CustomerDto> DummyFullProjection(
    this IQueryable<Customer> source) => source
    .SelectExpr<Customer, CustomerDto>(c => new
    {
        c.Id,
        c.Name,
        c.Email,
        // Null-conditional access
        BillingCity = c.BillingAddress?.City,
        // Nested SelectExpr
        Orders = c.Orders.SelectExpr<Order, OrderSummaryDto>(o => new
        {
            o.Id,
            o.TotalAmount,
        }),
        // Complex expressions
        HasPremiumOrders = c.Orders.Any(o => o.TotalAmount > 1000),
    });
```

### 2. Automatic DTO Generation

DTOs are generated automatically, just like regular SelectExpr:

```csharp
// Linqraft generates:
public partial class OrderDto
{
    public int Id { get; set; }
    public string? CustomerName { get; set; }
    public List<OrderItemDto> Items { get; set; }
}

public partial class OrderItemDto
{
    public string ProductName { get; set; }
    public int Quantity { get; set; }
}
```

### 3. Nested Collections and Types

Nested `SelectExpr` calls are converted to `Select`:

```csharp
[LinqraftMappingGenerate("ProjectWithNested")]
internal static IQueryable<CustomerDto> DummyWithNested(
    this IQueryable<Customer> source) => source
    .SelectExpr<Customer, CustomerDto>(c => new
    {
        c.Id,
        Orders = c.Orders.SelectExpr<Order, OrderDto>(o => new
        {
            o.Id,
            Items = o.Items.Select(i => i.ProductName),
        }),
    });
```

## Benefits

### For Compiled Queries

✅ **Expression Tree Compatibility**: Generated code uses proper null checks instead of `?.`

✅ **Performance**: Queries are compiled once and reused

✅ **Type Safety**: Full compile-time type checking

### For Precompiled Queries (EF9+)

✅ **NativeAOT Support**: Works with ahead-of-time compilation

✅ **Build-Time Optimization**: SQL is generated at build time

✅ **Zero Runtime Overhead**: No expression tree compilation at runtime

✅ **Smaller Binaries**: No need for runtime compilation infrastructure

## Requirements

1. **Static Partial Class**: The containing class must be `static` and `partial`
2. **Top-Level Class**: Extension methods must be in a non-nested class
3. **SelectExpr Inside**: The dummy method must contain at least one `SelectExpr` call

## Comparison: Interceptors vs LinqraftMappingGenerate

| Feature | Interceptors | LinqraftMappingGenerate |
|---------|-------------|------------------------|
| Regular queries | ✅ | ✅ |
| Compiled queries | ❌ | ✅ |
| Precompiled queries | ❌ | ✅ |
| NativeAOT | ❌ | ✅ |
| Syntax | Direct inline | Requires dummy method |
| DTO generation | Automatic | Automatic |
| Nested SelectExpr | ✅ | ✅ |

## Best Practices

### 1. Naming Conventions

Use descriptive names for both the dummy method and generated method:

```csharp
[LinqraftMappingGenerate("ProjectToOrderSummary")]
internal static IQueryable<OrderSummaryDto> DummyOrderSummary(...)

[LinqraftMappingGenerate("ProjectToDetailedCustomer")]
internal static IQueryable<DetailedCustomerDto> DummyDetailedCustomer(...)
```

### 2. Organize by Entity

Group related projections in the same class:

```csharp
public static partial class CustomerQueries
{
    [LinqraftMappingGenerate("ProjectToSummary")]
    internal static IQueryable<CustomerSummaryDto> DummySummary(...)
    
    [LinqraftMappingGenerate("ProjectToDetailed")]
    internal static IQueryable<CustomerDetailedDto> DummyDetailed(...)
}
```

### 3. Document Purpose

Add XML comments to explain the projection:

```csharp
/// <summary>
/// Projects Customer entities to summary DTOs including order count.
/// Optimized for list displays.
/// </summary>
[LinqraftMappingGenerate("ProjectToSummary")]
internal static IQueryable<CustomerSummaryDto> DummySummary(...)
```

### 4. Test Generated Code

Verify the generated extension methods work as expected:

```csharp
[Fact]
public void ProjectToDto_GeneratesCorrectQuery()
{
    var query = dbContext.Orders.ProjectToDto();
    var sql = query.ToQueryString();
    
    Assert.Contains("Customer.Name", sql);
    Assert.DoesNotContain("?."), sql); // Null conditionals converted
}
```

## Limitations

1. **No Local Variable Capture**: The current implementation doesn't support captured variables
   ```csharp
   // Not supported yet
   var threshold = 100;
   [LinqraftMappingGenerate("ProjectFiltered")]
   internal static IQueryable<OrderDto> Dummy(...) => source
       .SelectExpr(o => new { o.Id, IsLarge = o.Total > threshold });
   ```

2. **Must Be Static**: The containing class must be static
   ```csharp
   // ❌ Wrong
   public partial class CustomerQueries { ... }
   
   // ✅ Correct
   public static partial class CustomerQueries { ... }
   ```

3. **Dummy Method Never Called**: The dummy method exists only for code generation
   ```csharp
   // Don't call this directly
   var result = source.DummyQuery(); // ❌
   
   // Call the generated method instead
   var result = source.ProjectToDto(); // ✅
   ```

## Troubleshooting

### Error: "Extension methods must be defined in a top level static class"

**Cause**: The class is nested inside another class.

**Solution**: Move the class to the top level (not nested).

### Error: "No defining declaration found for partial method"

**Cause**: The class is not marked as `partial`.

**Solution**: Add the `partial` keyword to the class declaration.

### Generated Method Not Found

**Cause**: The source generator didn't run or failed.

**Solutions**:
1. Clean and rebuild: `dotnet clean && dotnet build`
2. Check that the attribute parameter is a valid method name
3. Verify the dummy method contains a `SelectExpr` call
4. Check the build output for generator errors

## Examples

### Example 1: Simple Projection

```csharp
public static partial class ProductQueries
{
    [LinqraftMappingGenerate("ProjectToCard")]
    internal static IQueryable<ProductCardDto> DummyCard(
        this IQueryable<Product> source) => source
        .SelectExpr<Product, ProductCardDto>(p => new
        {
            p.Id,
            p.Name,
            p.Price,
            p.ImageUrl,
        });
}

// Usage
var products = await dbContext.Products
    .Where(p => p.IsActive)
    .ProjectToCard()
    .ToListAsync();
```

### Example 2: With Compiled Query

```csharp
public static partial class CustomerQueries
{
    [LinqraftMappingGenerate("ProjectToSummary")]
    internal static IQueryable<CustomerSummaryDto> DummySummary(
        this IQueryable<Customer> source) => source
        .SelectExpr<Customer, CustomerSummaryDto>(c => new
        {
            c.Id,
            c.Name,
            OrderCount = c.Orders.Count(),
        });
}

public class CustomerService
{
    private static readonly Func<MyDbContext, Task<List<CustomerSummaryDto>>> 
        GetCustomerSummaries = EF.CompileAsyncQuery(
            (MyDbContext db) => db.Customers
                .ProjectToSummary()
                .ToList()
        );

    public async Task<List<CustomerSummaryDto>> GetSummariesAsync(MyDbContext db)
    {
        return await GetCustomerSummaries(db);
    }
}
```

### Example 3: For NativeAOT (EF9+)

```csharp
public static partial class OrderQueries
{
    [LinqraftMappingGenerate("ProjectToInvoice")]
    internal static IQueryable<InvoiceDto> DummyInvoice(
        this IQueryable<Order> source) => source
        .SelectExpr<Order, InvoiceDto>(o => new
        {
            o.OrderNumber,
            CustomerName = o.Customer?.Name,
            Items = o.Items.Select(i => new
            {
                i.ProductName,
                i.Quantity,
                i.UnitPrice,
                Total = i.Quantity * i.UnitPrice,
            }),
            TotalAmount = o.Items.Sum(i => i.Quantity * i.UnitPrice),
        });
}

// EF9 will precompile this at build time for NativeAOT
var invoices = await dbContext.Orders
    .Where(o => o.Date > DateTime.Today.AddDays(-30))
    .ProjectToInvoice()
    .ToListAsync();
```

## Further Reading

- [EF Core Compiled Queries](https://learn.microsoft.com/en-us/ef/core/performance/advanced-performance-topics#compiled-queries)
- [EF Core Precompiled Queries (EF9)](https://github.com/dotnet/efcore/issues/25009)
- [Linqraft Usage Patterns](./usage-patterns.md)
- [Linqraft Performance](./performance.md)
