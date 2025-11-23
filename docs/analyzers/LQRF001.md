# LQRF001 - AnonymousTypeToDtoAnalyzer

**Severity:** Hidden
**Category:** Design
**Default:** Enabled

## Description
Detects anonymous object creations (`new { ... }`) that appear in contexts where a strongly-typed DTO class would be more appropriate. The analyzer ignores empty anonymous types and anonymous objects that are already part of `SelectExpr` calls.

## When It Triggers
- When an anonymous object creation appears in a convertible context such as:
  - Variable initializers (e.g. `var x = new { ... }`)
  - Return or `yield return` statements
  - Assignments (`x = new { ... }`)
  - Method arguments (`Method(new { ... })`)
  - Array/collection initializers (`new[] { new { ... } }`)
  - Conditional expressions (`cond ? new { ... } : other`)
  - Lambda bodies (`x => new { ... }`)

## When It Doesn't Trigger
- Empty anonymous types.
- Anonymous types that are produced inside `SelectExpr<T, TDto>(...)` invocations (the library already provides DTO tooling there).

## Code Fixes
`AnonymousTypeToDtoCodeFixProvider` provides two fixes:
1. Convert to a DTO class and add the generated class into the current file.
2. Convert to a DTO class and create a new file for the DTO in the same project.

The code fix analyzes the anonymous shape, generates DTO classes (including nested DTOs for nested anonymous objects), replaces the anonymous object with a `new DTO { ... }` initializer, and adds the generated DTO class(es) to the file or a new document.

## Example
Before:
```csharp
public object GetUserInfo(User user)
{
    return new  // LQRF001
    {
        user.Id,
        user.Name,
        user.Email
    };
}
```

After (one possible result):
```csharp
public object GetUserInfo(User user)
{
    return new UserInfoDto
    {
        Id = user.Id,
        Name = user.Name,
        Email = user.Email
    };
}

public class UserInfoDto
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Email { get; set; }
}
```

## Notes and edge cases
- Nested anonymous objects are converted into nested DTOs when the fix generates classes.
- The fixer tries to pick meaningful DTO names using `DtoNamingHelper` and falls back to generated names when necessary.

## Suppression
Use Roslyn suppression mechanisms to avoid suggestions where anonymous types are preferred.

## Implementation notes
- Analyzer id: `LQRF001`
- Implementation: `Linqraft.Analyzer.AnonymousTypeToDtoAnalyzer`
- Code fix: `Linqraft.Analyzer.AnonymousTypeToDtoCodeFixProvider`

