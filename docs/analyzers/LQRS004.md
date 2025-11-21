# LQRS004 - TernaryNullCheckToConditionalAnalyzer

**Severity:** Info  
**Category:** Design  
**Default:** Enabled

## Description
Detects ternary expressions performing null checks that return an object creation (anonymous or named) in one branch and `null` (or `(Type?)null`) in the other. These can be simplified using null-conditional operators (`?.`) for readability and to avoid CS8602 warnings.

## When It Triggers
A conditional expression of the form:
```csharp
condition ? new Something { ... } : null
// or
condition ? null : new Something { ... }
```
Where `condition` (possibly combined with `&&`) includes at least one `x == null` or `x != null` style null check.

## When It Doesn't Trigger
- Neither branch is an object creation.
- No explicit null check in the condition.
- Both branches produce non-null object creations.

## Code Fix (Conceptual)
Suggests rewriting to use null-conditional chains, e.g.:
```csharp
var result = source?.Child != null
    ? new Something { Value = source.Child.Prop }
    : null; // potential simplification
```
Or direct property projection with `?.` where feasible.

## Example
**Before:**
```csharp
var dto = input != null ? new ProductDto { Id = input.Id } : null;
```
**After (Simplified):**
```csharp
var dto = input?.Id is int id ? new ProductDto { Id = id } : null; // or alternative null-conditional pattern
```
