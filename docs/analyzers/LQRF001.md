# LQRF001 - ApiControllerProducesResponseTypeAnalyzer

**Severity:** Info  
**Category:** Design  
**Default:** Enabled

## Description
Detects `SelectExpr<T, TDto>` usage inside ASP.NET Core `[ApiController]` action methods that return an untyped `IActionResult` (or `ActionResult` without a type argument) and do not declare a `[ProducesResponseType]` attribute. Adding `[ProducesResponseType]` clarifies the response type for OpenAPI generators.

## When It Triggers
- The containing class has an `[ApiController]` attribute.
- The method return type is `IActionResult` or `ActionResult` (untyped, i.e. not `ActionResult<T>`).
- The method body contains a `SelectExpr` invocation with type arguments (the analyzer looks for `SelectExpr<,>` usage to infer the DTO type).
- The method does not already have a `ProducesResponseType` attribute.

## Suggested Fix
The analyzer suggests adding `[ProducesResponseType(...)]` metadata that matches the action result shape. For example, `Ok(query.ToList())` should use `[ProducesResponseType(200, Type = typeof(List<TDto>))]`, while `Ok(query)` should use `[ProducesResponseType(200, Type = typeof(IEnumerable<TDto>))]`.

## Example
Before:
```csharp
[ApiController]
[Route("api/[controller]")]
public class ProductsController : ControllerBase
{
    public IActionResult Get()
    {
        var dto = _db.Products.SelectExpr<Product, ProductDto>(p => new { p.Id, p.Name });
        return Ok(dto);
    }
}
```

After (suggested):
```csharp
[ApiController]
[Route("api/[controller]")]
public class ProductsController : ControllerBase
{
    [ProducesResponseType(200, Type = typeof(IEnumerable<ProductDto>))]
    public IActionResult Get()
    {
        var dto = _db.Products.SelectExpr<Product, ProductDto>(p => new { p.Id, p.Name });
        return Ok(dto);
    }
}
```
