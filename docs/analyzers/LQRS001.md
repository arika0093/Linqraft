````markdown
# LQRS001 - SelectExprToTypedAnalyzer

**Severity:** Hidden  
**Category:** Design  
# LQRS001 - SelectExprToTypedAnalyzer

**Severity:** Hidden
**Category:** Design
**Default:** Enabled

## Description
Detects `SelectExpr` invocations that are written without explicit type arguments while the selector produces an anonymous type. The analyzer reports a hidden diagnostic indicating the call can be converted to `SelectExpr<TSource, TDto>` to improve type safety and enable strongly-typed projections.

## When It Triggers
- The `SelectExpr` call is invoked without generic type arguments.
- The selector (lambda) returns an anonymous object (`new { ... }`).

## Code Fix
`SelectExprToTypedCodeFixProvider` can replace the untyped invocation with a generic form like `SelectExpr<SourceType, GeneratedDto>` and will add any necessary `using` directives for the source type's namespace. The fixer generates a DTO name based on context and inserts the generated DTO type when appropriate.

## Example
Before:
```csharp
var result = query.SelectExpr(x => new  // LQRS001
{
    x.Id,
    x.Name,
    x.Price
});
```

After (one possible result):
```csharp
var result = query.SelectExpr<Product, ResultDto_XXXXXXXX>(x => new
{
    x.Id,
    x.Name,
    x.Price
});
```

## Notes and edge cases
- The DTO name is inferred by `DtoNamingHelper` from available context; when context is insufficient a generated fallback name is used.
- The analyzer reports a hidden diagnostic so conversions can be surfaced by code fixes or IDE lightbulb actions without showing a warning in code.

## Suppression
Use standard Roslyn suppression mechanisms (pragma, suppression attributes, or .editorconfig) to suppress the diagnostic in special cases.

## Implementation notes
- Analyzer id: `LQRS001`
- Implementation: `Linqraft.Analyzer.SelectExprToTypedAnalyzer`
- Code fix: `Linqraft.Analyzer.SelectExprToTypedCodeFixProvider`
