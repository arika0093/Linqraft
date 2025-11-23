````markdown
# LQRS004 - TernaryNullCheckToConditionalAnalyzer

**Severity:** Info  
**Category:** Design  
**Default:** Enabled

## Description
Detects conditional (ternary) expressions where one branch returns `null` (or a nullable null-cast) and the other branch returns an object creation (anonymous or named), and the condition contains a null-check expression. Such patterns are often simplifyable using null-conditional (`?.`) chains or other null-safe idioms.

## When It Triggers
- The node is a conditional expression (`condition ? whenTrue : whenFalse`).
- The condition contains a null check (e.g. `x != null`, `x == null`) or a logical-and chain that includes a null check.
- One branch is `null` (or `(Type?)null`) and the other branch constructs an object (`new ...` or anonymous `new { ... }`).

## Suggested Transformation
The analyzer reports an informational diagnostic and suggests a null-propagation style replacement when the pattern is a simple null check that guards an object creation. A common safe transformation is to move the nullable operator into the member access so that inner member accesses use the null-conditional operator.

## Replacement example
Before:
```csharp
x.A != null ? new { B = x.A.B } : null
```

