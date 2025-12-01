# LQRS004 - TernaryNullCheckToConditionalAnalyzer

**Severity:** Info  
**Category:** Design  
**Default:** Enabled

## Description
Detects conditional (ternary) expressions where one branch returns `null` (or a nullable null-cast) and the other branch returns an object creation (anonymous or named), and the condition contains a null-check expression. Such patterns are often simplifiable using null-conditional (`?.`) chains or other null-safe idioms.

## When It Triggers
- The node is a conditional expression (`condition ? whenTrue : whenFalse`).
- The condition contains a null check (e.g. `x != null`, `x == null`) or a logical-and chain that includes a null check.
- One branch is `null` (or `(Type?)null`) and the other branch constructs an object (`new ...` or anonymous `new { ... }`).
- The conditional is inside a `SelectExpr` call.

## When It Doesn't Trigger
- Neither branch is an object creation.
- The condition doesn't include a null-check.
- The conditional is outside a `SelectExpr` call.

## Suggested Transformation
The analyzer reports an informational diagnostic and suggests a null-propagation style replacement when the pattern is a simple null check that guards an object creation. A common safe transformation is to move the nullable operator into the member access so that inner member accesses use the null-conditional operator.

### Relationship with LQRS002/LQRS003

Some `Select` → `SelectExpr` conversion options automatically apply this transformation:
- **LQRS002** (anonymous type): Always applies ternary simplification
- **LQRS003 "Explicit"**: Applies ternary simplification

Others preserve the original ternary patterns:
- **LQRS003 "Explicit (strict)"**: Does NOT apply ternary simplification
- **LQRS003 "Predefined"**: Does NOT apply ternary simplification

For the conversions that preserve ternary patterns, you can use LQRS004 to manually apply the transformation afterward if desired.

### Replacement example
Given a conditional like:
```csharp
x.A != null ? new { B = x.A.B } : null
```
the analyzer suggests replacing the object-creating ternary with a projection that uses null-conditional access inside the object initializer:
```csharp
new { B = x.A?.B }
```

This effectively collapses the ternary and moves the `?.` into the member access, producing an object whose properties are null when the source is null instead of returning `null` for the whole object. Because the resulting nullability shape differs from the original expression, this transformation is offered as an informational suggestion.
