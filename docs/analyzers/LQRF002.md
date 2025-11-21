# LQRF002 - ApiControllerProducesResponseTypeAnalyzer

**Severity:** Info  
**Category:** Design  
**Default:** Enabled

## Description
When `SelectExpr<T, TDto>` is used inside ASP.NET Core `[ApiController]` actions returning `IActionResult`, adding `[ProducesResponseType]` improves OpenAPI documentation clarity.

## When It Triggers
- Method is inside a class marked with `[ApiController]`.
- Return type is (or reduces to) `IActionResult` / compatible.
- Method body contains `SelectExpr<,>` producing a DTO.
- No `[ProducesResponseType]` attribute already present.

## Code Fix (Planned)
Suggests adding `[ProducesResponseType(typeof(TDto))]` to the action.

## Example (Conceptual)
**Before:**
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
**After:**
```csharp
[ApiController]
[Route("api/[controller]")]
public class ProductsController : ControllerBase
{
    [ProducesResponseType(typeof(ProductDto))]
    public IActionResult Get()
    {
        var dto = _db.Products.SelectExpr<Product, ProductDto>(p => new { p.Id, p.Name });
        return Ok(dto);
    }
}
```
