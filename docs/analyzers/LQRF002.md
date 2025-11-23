# LQRF002 - ApiControllerProducesResponseTypeAnalyzer

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
The analyzer suggests adding `[ProducesResponseType(typeof(TDto))]` (or the appropriate status-code overload) to the action to improve OpenAPI metadata. This is currently reported as an informational suggestion.

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
    [ProducesResponseType(typeof(ProductDto))]
    public IActionResult Get()
    {
        var dto = _db.Products.SelectExpr<Product, ProductDto>(p => new { p.Id, p.Name });
        return Ok(dto);
    }
}
```

## Notes and edge cases
- The analyzer infers `TDto` from the `SelectExpr<,>` generic arguments when present.
- For untyped `SelectExpr` usages the analyzer cannot infer the DTO type and will not report.

## Suppression
Use Roslyn suppression mechanisms to suppress the suggestion if OpenAPI annotations are managed separately.

## Implementation notes
- Analyzer id: `LQRF002`
- Implementation: `Linqraft.Analyzer.ApiControllerProducesResponseTypeAnalyzer`

