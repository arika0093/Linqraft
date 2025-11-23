````markdown
# LQRS005 - UnnecessaryCaptureAnalyzer

**Severity:** Warning  
**Category:** Usage  
**Default:** Enabled

## Description
Detects capture variables passed to `SelectExpr` that are not referenced inside the selector lambda and suggests removing them. Unnecessary captured variables add visual noise and may be misleading about which outer values are actually used by the projection.

This analyzer looks for `SelectExpr` invocations that include a `capture:` argument (or a positional capture argument) expressed as an anonymous object (for example `capture: new { foo, bar }`) and reports each captured member that does not appear (as an identifier or relevant member access) inside the selector lambda.

## When It Triggers
- The invocation is a `SelectExpr(...)` call.
- The call provides captures via a `capture:` named argument or a second positional argument that is an anonymous object (e.g. `new { a, b }`).
- One or more properties listed in that anonymous capture object are never referenced from within the selector lambda body.

## Code Fix
`UnnecessaryCaptureCodeFixProvider` offers a fix that removes the unused capture variable(s). If all captured variables are unused, the entire capture argument is removed. If only some are unused, the code fix rewrites the anonymous capture object to contain only the variables that are actually referenced by the selector.

The code fix preserves formatting and normalizes line endings.

## Examples

Before (unused capture `now`):
```csharp
var now = DateTime.UtcNow;

var result = query.SelectExpr(
    x => new {
        x.Id,
        x.Timestamp
    },
    capture: new { now } // LQRS005 reported for 'now'
);
```

After (capture removed):
```csharp
var now = DateTime.UtcNow;

var result = query.SelectExpr(
    x => new {
        x.Id,
        x.Timestamp
    }
);
```

Before (mixed used/unused):
```csharp
string userName = GetUserName();
int unusedCounter = 0;

var items = db.Items.SelectExpr(
    i => new { i.Id, Owner = userName },
    capture: new { userName, unusedCounter }
);
// LQRS005 will report 'unusedCounter' as unnecessary
```

After (keep only `userName`):
```csharp
string userName = GetUserName();
int unusedCounter = 0;

var items = db.Items.SelectExpr(
    i => new { i.Id, Owner = userName },
    capture: new { userName }
);
```

## Notes and edge cases
- The analyzer attempts to distinguish lambda parameters from captured variables and ignores identifiers that are part of member names (e.g., property names in anonymous object initializers) or right-hand side member names.
- Member accesses like `this.SomeField` or `obj.Member` are considered: if the selector references a member (field/property) and that member is part of the capture object, the analyzer treats it as used.
- Static members accessed via type names are not considered captured and will not trigger the diagnostic.
- The analyzer reports each unused captured property separately so you can fix a single reported item or apply a batch fix.

## Suppression
You can suppress the diagnostic using normal Roslyn suppressions (pragma, suppression attributes, or a .editorconfig rule) if you intentionally keep a capture for clarity or future use.

## Implementation notes
- Analyzer id: `LQRS005`  
- Implementation: `Linqraft.Analyzer.UnnecessaryCaptureAnalyzer`  
- Code fix: `Linqraft.Analyzer.UnnecessaryCaptureCodeFixProvider`  
- The implementation finds the lambda, enumerates captures via `CaptureHelper.GetCapturedVariables`, computes used identifiers in the lambda (excluding lambda parameters and identifiers that are part of member/property names), and reports unused captures. The code fix removes or updates the `capture` anonymous object accordingly.

For more details, see implementation in `src/Linqraft.Analyzer/UnnecessaryCaptureAnalyzer.cs` and `UnnecessaryCaptureCodeFixProvider.cs`.

````
