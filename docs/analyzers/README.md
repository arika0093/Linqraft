# Linqraft Analyzers

| Diagnostic ID | Analyzer | Description |
|--------------|----------|-------------|
| [LQRF001](./LQRF001.md) | AnonymousTypeToDtoAnalyzer | Anonymous type → DTO conversion suggestion |
| [LQRF002](./LQRF002.md) | ApiControllerProducesResponseTypeAnalyzer | Add ProducesResponseType for API SelectExpr usage |
| [LQRS001](./LQRS001.md) | SelectExprToTypedAnalyzer | Untyped SelectExpr → typed form |
| [LQRS002](./LQRS002.md) | SelectToSelectExprAnonymousAnalyzer | IQueryable.Select anonymous → SelectExpr |
| [LQRS003](./LQRS003.md) | SelectToSelectExprNamedAnalyzer | IQueryable.Select named DTO → SelectExpr |
| [LQRS004](./LQRS004.md) | TernaryNullCheckToConditionalAnalyzer | Simplify ternary null/object pattern |
| [LQRE001](./LQRE001.md) | LocalVariableCaptureAnalyzer | Missing capture parameter entries |
