# 06. Constraints, Compatibility, and Non-Functional Expectations

## 1. Environment constraints

## 1.1 Language requirement

A compatible clone MUST require C# 12 or later for the interceptor-driven workflow.

## 1.2 SDK/tooling requirement

The current docs require one of:

- .NET SDK 8.0.400 or later
- Visual Studio 2022 17.11 or later

The docs explicitly frame this as an SDK/tooling requirement rather than a target-framework requirement.

## 1.3 Older runtime targets

The docs allow targeting older runtimes as long as the build environment can use the required language/tooling features.
For .NET 7 or below, the documented setup is:

- set `LangVersion` to `12.0` or later
- use PolySharp to backfill newer language/runtime support where needed

## 2. Provider compatibility

The clone MUST continue to support the documented provider matrix:

- Entity Framework Core
- Entity Framework 6
- LINQ to SQL
- LINQ to Objects / in-memory collections
- custom providers that expose compatible `IQueryable<T>` or `IEnumerable<T>` behavior

The generator SHOULD remain provider-agnostic beyond the need to emit standard LINQ constructs.

## 3. Stability model

## 3.1 Stable user-facing contracts

The following are stable contracts and MUST be preserved:

- `SelectExpr` authoring patterns
- caller-named explicit root DTOs
- mapping generation attributes and helper base class
- analyzer IDs and their general intent
- documented MSBuild property names and meanings

## 3.2 Unstable implementation details

The following are intentionally unstable and users are warned not to depend on them:

- hash values in generated nested DTO names
- hash-based generated namespaces
- exact generated helper-class names for mapping methods

The clone MAY change the exact hash algorithm as long as the documented "generated names are unstable implementation details" contract is preserved.

## 4. Performance expectations

The docs do not guarantee exact benchmark numbers, but they do establish clear non-functional goals.
A compatible clone SHOULD preserve these properties:

1. runtime performance is close to hand-written projections
2. there is no runtime mapping engine overhead
3. generated code uses native LINQ projection constructs
4. optional prebuilt-expression mode can reduce repeated expression construction for eligible `IQueryable` flows

The published benchmark corpus covers:

- `IEnumerable`
- `IQueryable`
- EF Core with SQLite
- .NET JIT scenarios
- NativeAOT scenarios

Those published numbers are informative rather than normative, but they SHOULD remain a regression reference during the refactor.

## 5. Optional and beta behaviors

## 5.1 Nested `SelectExpr`

This feature is documented as beta.
A compatible clone SHOULD preserve it, including the "empty partial declarations required" rule, but it MAY continue to be labeled beta until the rewrite intentionally graduates it.

## 5.2 Mapping methods

The mapping-method feature is also documented as an alternative path for compiled-query and AOT scenarios.
It SHOULD be preserved because it covers cases where interceptor-based direct call-site replacement is not the best fit, including EF Core compiled queries, EF Core precompiled queries, and NativeAOT-oriented execution paths.

## 6. Documentation-derived edge constraints

The clone MUST preserve the following documented constraints:

- explicit capture is required for non-constant outer values
- anonymous `GroupBy` keys feeding `SelectExpr` are invalid and must be converted to named types
- prebuilt expression caching does not apply to anonymous projections
- prebuilt expression caching does not apply when capture variables are present
- array nullability removal is disabled when the user authors an explicit ternary returning `null`

## 7. Generated-code transparency

The docs treat generated code as inspectable and debuggable.
A compatible clone SHOULD continue to make generated output understandable through:

- IDE navigation
- emitted generated files
- familiar C# syntax rather than opaque binary artifacts

## 8. Acceptance checklist for a compatible clone

Before a refactored implementation is considered compatible, it SHOULD satisfy the following scenario checklist.

### 8.1 Core projection scenarios

- `IQueryable` anonymous projection using `SelectExpr(x => new { ... })`
- explicit DTO projection using `SelectExpr<TSource, TDto>(x => new { ... })`
- predefined DTO projection using `SelectExpr(x => new ExistingDto { ... })`
- nested anonymous object projection
- nested collection projection
- calculated field projection using aggregate operators such as `Sum` and `Max`

### 8.2 Nullability and capture scenarios

- `?.` on a single navigation
- chained `?.` across multiple navigation levels
- capture of one or more locals via `capture: new { ... }`
- analyzer error for missing capture
- analyzer warning and fix for unnecessary capture
- collection nullability removal for `?.Select(...)`
- nullability preservation when the user authors an explicit ternary

### 8.3 Extensibility scenarios

- extending a generated DTO via a partial class
- predeclaring a DTO property so the generator suppresses its own version
- generating records instead of classes
- switching property accessor styles
- toggling `required`

### 8.4 Nested and reusable DTO scenarios

- nested anonymous DTO generation using the default hash namespace strategy
- nested anonymous DTO generation using hash-suffixed class names
- nested explicit DTO generation via nested `SelectExpr`
- warnings when code explicitly depends on generated nested DTO types or namespaces

### 8.5 Mapping-method scenarios

- helper-class mapping generation with default method naming
- helper-class mapping generation with custom method naming
- static-partial-class mapping generation
- using generated mapping methods in compiled-query style code

### 8.6 Analyzer scenarios

- `Select` to `SelectExpr` conversion for anonymous projections
- `Select` to `SelectExpr` conversion for named projections with all three fix variants
- anonymous-shape to DTO refactoring outside `SelectExpr`
- API action metadata suggestion for untyped controller actions
- async and sync API response method generation suggestions
- anonymous `GroupBy` key rejection when followed by `SelectExpr`

## 9. Documentation discrepancy note

Some warning docs refer to a `SelectExpr<TResult>` convenience form, while the main usage guides consistently emphasize:

- inferred `SelectExpr(selector)`
- explicit `SelectExpr<TSource, TResult>(selector)`

For clone compatibility, the latter two forms are the required baseline contract.
If the refactor chooses to preserve or reintroduce additional convenience overloads, they SHOULD be documented explicitly in the public guides.
