# Generating Select Methods Within a Class

This guide explains how to generate reusable Select extension methods within your own classes using Linqraft's `[LinqraftMappingGenerate]` attribute.

## Overview

Linqraft traditionally uses interceptors to generate efficient Select expressions inline. However, in some scenarios you may want to define reusable Select methods within your own classes:

- Creating reusable projection methods that can be called from multiple places
- Using with EF Core's compiled queries (`EF.CompileAsyncQuery`)
- Supporting EF Core's precompiled queries (EF9+) for NativeAOT compatibility
- Organizing complex projections in dedicated query classes

The `[LinqraftMappingGenerate]` attribute generates extension methods in your class instead of using interceptors, enabling these scenarios.

## Basic Usage

### Step 1: Create a Static Partial Class

Define a static partial class with a template method marked with `[LinqraftMappingGenerate]`:

```csharp
public static partial class OrderQueries
{
    // Template method - never called, only analyzed by Linqraft
    [LinqraftMappingGenerate("ProjectToDto")]
    internal static IQueryable<OrderDto> Template(this IQueryable<Order> source) => source
        .SelectExpr<Order, OrderDto>(o => new
        {
            o.Id,
            CustomerName = o.Customer?.Name,  // ?.operator is converted
            Items = o.Items.Select(i => new
            {
                i.ProductName,
                i.Quantity,
            }),
        });
}
```

### Step 2: Use the Generated Method

Linqraft generates the `ProjectToDto` extension method in your class:

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

### Step 3: Call the Generated Method

```csharp
// Call the generated method from anywhere
var orders = await dbContext.Orders
    .ProjectToDto()
    .ToListAsync();
```

## Why Use Mapping Methods?

### Benefits

1. **Reusability**: Define projections once, use them everywhere
2. **Organization**: Group related queries in dedicated classes
3. **Testability**: Easy to test projection logic independently
4. **Compatibility**: Works with scenarios where interceptors cannot be used

### Use Cases

- **Reusable Projections**: Call the same projection from multiple endpoints
- **Query Classes**: Organize complex queries in dedicated static classes
- **EF Core Compiled Queries**: Improve performance with `EF.CompileAsyncQuery`
- **EF Core Precompiled Queries**: Enable NativeAOT support (EF9+)

## Key Features

### 1. Full SelectExpr Syntax Support

Template methods support all Linqraft features:

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

## Using with EF Core

### Compiled Queries (EF Core 2.0+)

Improve performance by compiling queries once and reusing them:

```csharp
public static partial class OrderQueries
{
    [LinqraftMappingGenerate("ProjectToSummary")]
    internal static IQueryable<OrderSummaryDto> Template(
        this IQueryable<Order> source) => source
        .SelectExpr<Order, OrderSummaryDto>(o => new
        {
            o.Id,
            o.OrderNumber,
            TotalAmount = o.Items.Sum(i => i.Quantity * i.UnitPrice),
        });
}

public class OrderService
{
    // Compile the query once
    private static readonly Func<MyDbContext, Task<List<OrderSummaryDto>>> 
        GetOrderSummaries = EF.CompileAsyncQuery(
            (MyDbContext db) => db.Orders
                .ProjectToSummary()
                .ToListAsync()
        );

    public async Task<List<OrderSummaryDto>> GetSummariesAsync(MyDbContext db)
    {
        // Reuse the compiled query
        return await GetOrderSummaries(db);
    }
}
```

**Benefits:**
- ✅ Queries are compiled once and cached
- ✅ Improved performance for frequently-called queries
- ✅ Expression tree compatibility with proper null checks

### Precompiled Queries (EF9+)

Enable NativeAOT support by precompiling queries at build time:

```csharp
public static partial class ProductQueries
{
    [LinqraftMappingGenerate("ProjectToCard")]
    internal static IQueryable<ProductCardDto> Template(
        this IQueryable<Product> source) => source
        .SelectExpr<Product, ProductCardDto>(p => new
        {
            p.Id,
            p.Name,
            p.Price,
            CategoryName = p.Category?.Name,
        });
}

// EF9 will precompile this at build time for NativeAOT
var products = await dbContext.Products
    .Where(p => p.IsActive)
    .ProjectToCard()
    .ToListAsync();
```

**Benefits:**
- ✅ NativeAOT compatibility
- ✅ SQL generated at build time
- ✅ Zero runtime expression compilation overhead
- ✅ Smaller binary sizes

## Requirements

1. **Static Partial Class**: The containing class must be `static` and `partial`
2. **Top-Level Class**: Extension methods must be in a non-nested class
3. **SelectExpr Inside**: The template method must contain at least one `SelectExpr` call

## When to Use

| Scenario | Use Interceptors | Use MappingGenerate |
|----------|------------------|---------------------|
| Quick inline projections | ✅ Recommended | - |
| Reusable projections | - | ✅ Recommended |
| EF compiled queries | ❌ Not compatible | ✅ Required |
| EF precompiled queries | ❌ Not compatible | ✅ Required |
| NativeAOT with EFCore | ❌ Not compatible | ✅ Required |
| Organized query classes | - | ✅ Recommended |

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

### Example 1: Basic Reusable Projection

```csharp
public static partial class ProductQueries
{
    [LinqraftMappingGenerate("ProjectToCard")]
    internal static IQueryable<ProductCardDto> Template(
        this IQueryable<Product> source) => source
        .SelectExpr<Product, ProductCardDto>(p => new
        {
            p.Id,
            p.Name,
            p.Price,
            p.ImageUrl,
        });
}

// Use from anywhere in your application
public class ProductController
{
    public async Task<IActionResult> GetProducts()
    {
        var products = await _context.Products
            .Where(p => p.IsActive)
            .ProjectToCard()
            .ToListAsync();
        
        return Ok(products);
    }
    
    public async Task<IActionResult> GetFeaturedProducts()
    {
        // Reuse the same projection
        var featured = await _context.Products
            .Where(p => p.IsFeatured)
            .ProjectToCard()
            .ToListAsync();
        
        return Ok(featured);
    }
}
```

### Example 2: Organized Query Class

```csharp
// Organize all customer-related projections in one class
public static partial class CustomerQueries
{
    [LinqraftMappingGenerate("ProjectToSummary")]
    internal static IQueryable<CustomerSummaryDto> TemplateSummary(
        this IQueryable<Customer> source) => source
        .SelectExpr<Customer, CustomerSummaryDto>(c => new
        {
            c.Id,
            c.Name,
            c.Email,
            OrderCount = c.Orders.Count(),
            TotalSpent = c.Orders.Sum(o => o.TotalAmount),
        });
    
    [LinqraftMappingGenerate("ProjectToDetailed")]
    internal static IQueryable<CustomerDetailedDto> TemplateDetailed(
        this IQueryable<Customer> source) => source
        .SelectExpr<Customer, CustomerDetailedDto>(c => new
        {
            c.Id,
            c.Name,
            c.Email,
            c.Phone,
            BillingAddress = c.BillingAddress?.Street,
            RecentOrders = c.Orders
                .OrderByDescending(o => o.OrderDate)
                .Take(5)
                .SelectExpr<Order, RecentOrderDto>(o => new
                {
                    o.Id,
                    o.OrderNumber,
                    o.OrderDate,
                    o.TotalAmount,
                }),
        });
}
```

### Example 3: With EF Compiled Query (Optional)

```csharp
public static partial class OrderQueries
{
    [LinqraftMappingGenerate("ProjectToInvoice")]
    internal static IQueryable<InvoiceDto> Template(
        this IQueryable<Order> source) => source
        .SelectExpr<Order, InvoiceDto>(o => new
        {
            o.OrderNumber,
            CustomerName = o.Customer?.Name,
            Items = o.Items.SelectExpr<OrderItem, OrderItemDto>(i => new
            {
                i.ProductName,
                i.Quantity,
                i.UnitPrice,
                Total = i.Quantity * i.UnitPrice,
            }),
            TotalAmount = o.Items.Sum(i => i.Quantity * i.UnitPrice),
        });
}

// Use with EF compiled query for performance
public class InvoiceService
{
    private static readonly Func<MyDbContext, DateTime, Task<List<InvoiceDto>>> 
        GetRecentInvoices = EF.CompileAsyncQuery(
            (MyDbContext db, DateTime since) => db.Orders
                .Where(o => o.Date > since)
                .ProjectToInvoice()
                .ToListAsync()
        );

    public async Task<List<InvoiceDto>> GetInvoicesAsync(MyDbContext db)
    {
        return await GetRecentInvoices(db, DateTime.Today.AddDays(-30));
    }
}
```

## Further Reading

- [Linqraft Usage Patterns](./usage-patterns.md) - Learn about the three main Linqraft patterns
- [Linqraft Performance](./performance.md) - Performance optimization tips
- [EF Core Compiled Queries](https://learn.microsoft.com/en-us/ef/core/performance/advanced-performance-topics#compiled-queries) - Using with EF Core
- [EF Core Precompiled Queries](https://github.com/dotnet/efcore/issues/25009) - NativeAOT support in EF9
