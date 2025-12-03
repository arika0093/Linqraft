# Linqraft Analyzers

## Overview
| Diagnostic ID | Analyzer | Description |
|--------------|----------|-------------|
| [LQRF001](./LQRF001.md) | AnonymousTypeToDtoAnalyzer | Suggest converting anonymous types to DTO classes |
| [LQRF002](./LQRF002.md) | ApiControllerProducesResponseTypeAnalyzer | Suggest adding `ProducesResponseType` for untyped API actions using `SelectExpr` |
| [LQRS001](./LQRS001.md) | SelectExprToTypedAnalyzer | Suggest converting untyped `SelectExpr` to generic form `SelectExpr<TSource, TDto>` |
| [LQRS002](./LQRS002.md) | SelectToSelectExprAnonymousAnalyzer | `IQueryable.Select` with anonymous projection → `SelectExpr` suggestion |
| [LQRS003](./LQRS003.md) | SelectToSelectExprNamedAnalyzer | `IQueryable.Select` with named-object projection → `SelectExpr` suggestion |
| [LQRS004](./LQRS004.md) | TernaryNullCheckToConditionalAnalyzer | Suggest simplifying ternary null/object patterns |
| [LQRS005](./LQRS005.md) | UnnecessaryCaptureAnalyzer | Capture variables that are not referenced in SelectExpr should be removed |
| [LQRE001](./LQRE001.md) | LocalVariableCaptureAnalyzer | Reports missing `capture:` entries for `SelectExpr` selectors |
| [LQRE002](./LQRE002.md) | GroupByAnonymousKeyAnalyzer | Reports anonymous type keys in `GroupBy` followed by `SelectExpr` |

Basically, all analyzers come with code fixes that can be automatically applied to implement the suggested changes.
This allows you to fix the above recommendations with a single click.

## Prefixes
- `LQRF` - Linqraft Refactoring suggestions (utility improvements, without SelectExpr)
- `LQRS` - Linqraft SelectExpr suggestions
- `LQRE` - Linqraft Errors