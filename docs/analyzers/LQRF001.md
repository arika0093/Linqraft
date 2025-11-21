# LQRF001 - AnonymousTypeToDtoAnalyzer

**Severity:** Hidden  
**Category:** Design  
**Default:** Enabled

## Description
Detects anonymous type usages (`new { ... }`) that can be converted to strongly-typed DTO classes for better type safety and reusability.

## When It Triggers
Anonymous object creation appears in a convertible context:
- Variable declarations: `var result = new { ... }`
- Return statements / yield return statements
- Assignments: `x = new { ... }`
- Method arguments: `Method(new { ... })`
- Array initializers: `new[] { new { ... } }`
- Conditional expressions: `condition ? new { ... } : other`
- Lambda bodies: `x => new { ... }`

## When It Doesn't Trigger
- Empty anonymous types
- Anonymous types inside `SelectExpr<T, TDto>()` calls (already handled by Linqraft)

## Code Fixes
`AnonymousTypeToDtoCodeFixProvider` offers:
1. Convert to Class (add to current file)
2. Convert to Class (new file)

## Example
**Before:**
```csharp
public class UserService
{
    public object GetUserInfo(User user)
    {
        return new  // LQRF001: Anonymous type can be converted to DTO
        {
            user.Id,
            user.Name,
            user.Email
        };
    }
}
```
**After:**
```csharp
public class UserService
{
    public object GetUserInfo(User user)
    {
        return new UserInfoDto
        {
            Id = user.Id,
            Name = user.Name,
            Email = user.Email
        };
    }
}

public class UserInfoDto
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Email { get; set; }
}
```

## DTO Naming Convention
- Variable name: `var userInfo = new { ... }` → `UserInfoDto`
- Assignment target: `userInfo = new { ... }` → `UserInfoDto`
- Method name: `GetUserInfo()` → `UserInfoDto` (strips "Get")
- Fallback: `GeneratedDto`
