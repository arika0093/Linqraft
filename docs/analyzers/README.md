# Linqraft Analyzers

| Diagnostic ID | Analyzer | Description |
|--------------|----------|-------------|
| [LQRF001](analyzers/LQRF001.md) | AnonymousTypeToDtoAnalyzer | Anonymous type → DTO conversion suggestion |
| [LQRF002](analyzers/LQRF002.md) | ApiControllerProducesResponseTypeAnalyzer | Add ProducesResponseType for API SelectExpr usage |
| [LQRS001](analyzers/LQRS001.md) | SelectExprToTypedAnalyzer | Untyped SelectExpr → typed form |
| [LQRS002](analyzers/LQRS002.md) | SelectToSelectExprAnonymousAnalyzer | IQueryable.Select anonymous → SelectExpr |
| [LQRS003](analyzers/LQRS003.md) | SelectToSelectExprNamedAnalyzer | IQueryable.Select named DTO → SelectExpr |
| [LQRS004](analyzers/LQRS004.md) | TernaryNullCheckToConditionalAnalyzer | Simplify ternary null/object pattern |
| [LQRE001](analyzers/LQRE001.md) | LocalVariableCaptureAnalyzer | Missing capture parameter entries |
