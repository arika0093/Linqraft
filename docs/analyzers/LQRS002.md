# LQRS002 - SelectToSelectExprAnonymousAnalyzer

**Severity:** Info
**Category:** Design
**Default:** Enabled

## Description
Detects `System.Linq` `Select` calls performed on `IQueryable<T>` whose selector returns an anonymous object. These calls can be converted to Linqraft's `SelectExpr` to gain the library's projection behavior and improve type handling for query translation.

## When It Triggers
- The invocation is a `Select(...)` method from `System.Linq` (symbol name `Select`).
- The receiver is an `IQueryable<T>` (or a type implementing `IQueryable<T>`).
- The selector lambda returns an anonymous object (`new { ... }`).

## When It Doesn't Trigger
- Calls on `IEnumerable<T>`/in-memory sequences (the analyzer checks the invocation target's type).
- Selectors that do not create an anonymous type.
- Named object creations are handled by `LQRS003`.

## Code Fix
`SelectToSelectExprAnonymousCodeFixProvider` can offer conversions such as:
- Convert `Select(...)` → `SelectExpr(...)` preserving the anonymous projection.
- Convert `Select(...)` → `SelectExpr<TSource, TDto>(...)` with a generated DTO type when appropriate.

## Example
Before:
```csharp
var result = query.Select(x => new  // LQRS002
{
    x.Id,
    x.Name,
    x.Price
});
```

After (anonymous pattern):
```csharp
var result = query.SelectExpr(x => new
{
    x.Id,
    x.Name,
    x.Price
});
```

After (explicit DTO pattern):
```csharp
var result = query.SelectExpr<Product, ResultDto_XXXXXXXX>(x => new
{
    x.Id,
    x.Name,
    x.Price
});
```

## Notes and edge cases
- The fixer may generate DTO type names and insert generic type arguments where needed.
- The analyzer takes care to avoid flagging in-memory `IEnumerable<T>` `Select` calls.

## Suppression
Use Roslyn suppression mechanisms to suppress this diagnostic when conversion is not desired.

## Implementation notes
- Analyzer id: `LQRS002`
- Implementation: `Linqraft.Analyzer.SelectToSelectExprAnonymousAnalyzer`
- Code fix: `Linqraft.Analyzer.SelectToSelectExprAnonymousCodeFixProvider`

