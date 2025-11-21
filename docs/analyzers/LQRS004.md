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

## When It Doesn't Trigger
- Neither branch is an object creation.
- The condition doesn't include a null-check.

## Suggested Transformation
The analyzer reports an informational diagnostic; possible simplifications include using null-conditional/propagation patterns (e.g. `source?.Property`) or restructuring expressions to avoid redundant null-check ternaries. The project currently provides the diagnostic as a suggestion rather than an automated rewrite in all cases because semantic-preserving transformations can be context dependent.

## Example
Before:
```csharp
var dto = input != null ? new ProductDto { Id = input.Id } : null;
```

After (conceptual simplification using null-propagation):
```csharp
var dto = input is null ? null : new ProductDto { Id = input.Id };
// or when safe to use property projection: var dto = input?.Let(i => new ProductDto { Id = i.Id });
```
