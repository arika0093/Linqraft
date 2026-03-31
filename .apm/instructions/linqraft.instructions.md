---
applyTo: "**/*.cs"
---

# Linqraft

This project uses **Linqraft** — a Roslyn Source Generator that auto-generates DTO classes from LINQ projection selectors at compile time.

- Use `.UseLinqraft().Select<TDto>(x => new { ... })` for internal DTOs and nullable navigation (`?.`)
- Do **not** add `UseLinqraft()` for plain anonymous projections that need neither a named type nor `?.`

Refer to the **`linqraft` skill** for full API reference, patterns, and examples.
