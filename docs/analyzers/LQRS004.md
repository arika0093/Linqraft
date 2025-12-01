# LQRS004 - TernaryNullCheckToConditionalAnalyzer

**Severity:** Info  
**Category:** Design  
**Default:** Disabled (integrated into LQRS002/LQRS003)

## Description
Detects conditional (ternary) expressions where one branch returns `null` (or a nullable null-cast) and the other branch returns an object creation (anonymous or named), and the condition contains a null-check expression. Such patterns are simplifiable using null-conditional (`?.`) chains.

> **Note:** As of version 0.5.0, this analyzer is **disabled by default**. The transformation it suggests is now **automatically applied** when using the LQRS002 or LQRS003 code fixes (converting `Select` to `SelectExpr`). Users who prefer the more concise null-conditional notation no longer need to apply this transformation separately.

## Integration with LQRS002/LQRS003

Previously, when converting `.Select(...)` to `.SelectExpr(...)`, ternary null checks with object creations were preserved as-is, leaving users to manually apply the LQRS004 transformation if desired. 

Now, when LQRS002 or LQRS003 code fixes are applied, any ternary null check patterns of the form:

```csharp
prop = x.A != null ? new { x.A.B } : null
```

are automatically simplified to:

```csharp
prop = new { B = x.A?.B }
```

This provides a more concise result by default. While this changes the nullability shape slightly (individual properties become nullable instead of the whole object being null), built-in analyzer warnings make these changes easy to notice and address.

## When It Would Trigger (if enabled)
- The node is a conditional expression (`condition ? whenTrue : whenFalse`).
- The condition contains a null check (e.g. `x != null`, `x == null`) or a logical-and chain that includes a null check.
- One branch is `null` (or `(Type?)null`) and the other branch constructs an object (`new ...` or anonymous `new { ... }`).
- The conditional is inside a `SelectExpr` call.

## When It Doesn't Trigger
- Neither branch is an object creation.
- The condition doesn't include a null-check.
- The conditional is outside a `SelectExpr` call.

## Example Transformation

Before (in a `.Select(...)` call):
```csharp
var result = query.Select(x => new
{
    Child = x.Child != null ? new { x.Child.Name, x.Child.Value } : null
});
```

After (automatically applied via LQRS002 code fix):
```csharp
var result = query.SelectExpr(x => new
{
    Child = new { Name = x.Child?.Name, Value = x.Child?.Value }
});
```

## Nullability Considerations

This transformation changes the position of nullability:
- **Before:** The `Child` property is nullable; `Child.Name` and `Child.Value` are non-nullable when `Child` is not null.
- **After:** The `Child` property is non-nullable; `Name` and `Value` are nullable (via `?.`).

Built-in C# nullable reference type warnings (e.g., CS8602) will highlight these differences, making it easy to adjust code if needed.
