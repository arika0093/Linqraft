# Linqraft Specifications

This folder translates the current public README and documentation set into implementation-oriented specifications for the planned refactor.
The target outcome is a spec set detailed enough that a compatible Linqraft clone can be implemented from these documents without treating the current codebase as the primary source of truth.

## Scope

These specifications cover the library's documented:

- core purpose and architectural model
- public authoring surface
- code-generation behavior
- configuration options
- analyzer and code-fix behavior
- constraints, compatibility rules, and non-functional expectations

They intentionally focus on the externally visible contract and the behaviors promised by the docs.

## Source corpus

The spec set is derived primarily from:

- `README.md`
- `docs/README.nuget.md`
- `docs/library/*.md`
- `docs/analyzers/*.md`

Where the docs rely on generated-code examples or embedded support types, the current public support surface in `src/Linqraft.Core/GenerateSourceCodeSnippets.cs` is used only to disambiguate the documented API shape.

## Normative language

The keywords **MUST**, **MUST NOT**, **SHOULD**, **SHOULD NOT**, and **MAY** are used in the RFC sense:

- **MUST / MUST NOT**: required for a compatible clone
- **SHOULD / SHOULD NOT**: strongly recommended unless there is a deliberate, documented compatibility break
- **MAY**: optional behavior allowed by the current documentation

## Source precedence

If the existing documentation is inconsistent, use this precedence order while implementing the clone:

1. behavior stated in the usage, configuration, FAQ, and analyzer guides
2. generated-code examples in `README.md` and the library guides
3. the public support surface emitted by the generator (`SelectExpr` stubs, mapping attributes, helper base class)
4. marketing and comparison language

## Document map

| File | Purpose |
| --- | --- |
| `01-system-overview.md` | Product definition, component model, lifecycle, and invariants |
| `02-public-api-and-usage.md` | Author-facing API, supported call forms, and usage contracts |
| `03-code-generation-spec.md` | DTO generation, interceptor generation, transformation rules, and generated artifacts |
| `04-configuration-and-behavior.md` | MSBuild properties, naming, comments, nullability rules, and extensibility contracts |
| `05-analyzers-and-tooling.md` | Diagnostic catalog, triggers, code fixes, and migration/tooling expectations |
| `06-constraints-and-nonfunctional.md` | Environment constraints, compatibility matrix, stability rules, performance expectations, and acceptance checklist |

## Core compatibility invariants

A compatible clone MUST preserve the following high-level promises from the current documentation:

1. **Query-first projection authoring**  
   Users define the projection shape directly inside the LINQ query rather than in an external mapping configuration.

2. **On-demand DTO generation**  
   The explicit DTO workflow MUST generate DTO types from anonymous-shape selectors, including nested DTOs for nested anonymous objects and collections.

3. **Null-conditional support in expression-tree scenarios**  
   Users MAY author `?.` in `SelectExpr`, and the generated query for `IQueryable` MUST be translated to a form compatible with expression trees and LINQ providers.

4. **Zero runtime dependency for consumers**  
   Linqraft MUST remain a compile-time tool. Generated code must be ordinary C# code that executes through standard LINQ primitives without requiring a runtime mapping engine.

5. **Analyzer-assisted adoption**  
   The analyzer package MUST continue to provide migration suggestions, guardrails, and code fixes around `Select`, `SelectExpr`, captured values, and unsafe use of generated types.

6. **Extensible generated DTOs**  
   Generated DTOs MUST remain ordinary partial types so that consumers can extend them with members, interfaces, attributes, and predeclared properties.

## Implementation posture for the refactor

These specs are intended to preserve the current feature set during a large internal rewrite.
A future implementation MAY reorganize internals, but it SHOULD NOT alter any documented authoring pattern, generated-shape contract, or diagnostic behavior unless the spec set is updated first.
