# 08. Implementation Notes for the Rewrite

## 1. Purpose and source material

This document captures non-negotiable implementation rules for the large Linqraft refactor.
It combines:

- Roslyn's incremental generator guidance
- Roslyn's incremental generator cookbook conventions
- the current Linqraft codebase shape
- additional repository-specific constraints requested for this rewrite

Note:
the older `source-generators.cookbook.md` page is still useful for high-level framing, but the concrete non-empty conventions currently live in `incremental-generators.cookbook.md`.
This document therefore treats the incremental cookbook as the operative source for the "Conventions" section while still honoring the user-requested exception for raw `SelectExpr` invocation discovery.

Primary external references:

- `https://raw.githubusercontent.com/dotnet/roslyn/main/docs/features/incremental-generators.md`
- `https://raw.githubusercontent.com/dotnet/roslyn/main/docs/features/incremental-generators.cookbook.md`
- `https://raw.githubusercontent.com/arika0093/IDeepCloneable/main/src/IDeepCloneable.Generator.Source/Utility/EquatableArray.cs`
- `https://raw.githubusercontent.com/arika0093/IDeepCloneable/main/src/IDeepCloneable.Generator.Source/Utility/IndentedStringBuilder.cs`
- `https://raw.githubusercontent.com/arika0093/IDeepCloneable/main/src/IDeepCloneable.Generator.Source/Utility/CodeTemplateContents.cs`

Repository-local hotspots that this guidance applies to first:

- `src\Linqraft.SourceGenerator\SelectExprGenerator.cs`
- `src\Linqraft.Core\PipelineModels.cs`
- `src\Linqraft.Core\GenerateSourceCodeSnippets.cs`
- `src\Linqraft.Core\GenerateDtoClassInfo.cs`
- `src\Linqraft.SourceGenerator\SelectExprGroups.cs`
- `src\Linqraft.Core\Formatting\CodeFormatter.cs`

## 2. Baseline Roslyn rules that the rewrite MUST preserve

## 2.1 Additive-only generation

Source generators are additive.
The rewrite MUST continue to:

- add generated code
- emit diagnostics when generation cannot safely proceed
- avoid rewriting or mutating user-authored source text directly

Analyzer/code-fix layers may assist users in changing source, but the generator itself must remain additive.

## 2.2 Incremental execution and reuse

Roslyn's incremental model is built around deferred execution and cache reuse between pipeline transformations.
The rewrite MUST treat every provider stage as cache-sensitive:

- transform early
- convert unstable compiler objects into stable value models as soon as possible
- avoid carrying more information than the next stage actually needs

The key practical rule is: **if two runs produce the same model values, Roslyn should be able to short-circuit downstream work**.

## 3. Roslyn cookbook conventions adapted for Linqraft

## 3.1 Pipeline models MUST be value-equatable

Roslyn's cookbook explicitly recommends value-equatable pipeline models.
For Linqraft, that becomes:

- use `record` for stable pipeline models
- use only value-equatable fields/properties inside those models
- do not keep `ISymbol`, `SyntaxNode`, `SemanticModel`, or `Location` inside stable pipeline models
- reduce compiler objects to strings, enums, booleans, integers, hashes, and other immutable value data as early as possible

Implications for this repository:

- `src\Linqraft.Core\PipelineModels.cs` is the correct direction and should become the normal shape of incremental handoff data
- late-stage code generation should consume equatable records, not rich Roslyn objects
- if diagnostics still need a source span, prefer storing value data such as file path and span offsets instead of raw `Location`

## 3.2 Use `ForAttributeWithMetadataName` wherever declarations are attribute-driven

Roslyn recommends `SyntaxProvider.ForAttributeWithMetadataName` because it is significantly more efficient than a general `CreateSyntaxProvider` scan for attribute-driven discovery.

For Linqraft, the required rule is:

- all attribute-driven discovery surfaces MUST use `ForAttributeWithMetadataName`
- this applies immediately to mapping-method and mapping-declare discovery
- any future generator entry point that can be driven by a marker attribute MUST also use `ForAttributeWithMetadataName`

### Explicit exception: raw `SelectExpr` invocation discovery

`SelectExpr` call sites are expressions, not attributed declarations.
Because the user cannot attach a marker attribute to an arbitrary invocation expression, this one discovery path MAY continue to use `CreateSyntaxProvider`.

Even in that exception case, the rewrite MUST still:

- keep the predicate extremely cheap
- project to a minimal value model quickly
- avoid carrying syntax/semantic objects beyond the earliest stage that truly needs them

## 3.3 Use an indented text writer, not `SyntaxNode` generation + formatting

Roslyn's cookbook explicitly recommends text-based code generation with an indented writer rather than building `SyntaxNode`s and calling `NormalizeWhitespace`.

For Linqraft, this becomes a rewrite rule:

- generated source MUST be assembled with `IndentedStringBuilder`-style text emission
- code generation MUST NOT depend on `NormalizeWhitespace`
- ad-hoc newline joining and manual indentation arithmetic SHOULD be reduced in favor of the builder

Practical consequence:

- `src\Linqraft.Core\Formatting\CodeFormatter.cs` and string-interpolation-heavy emitters should be phased into a shared `IndentedStringBuilder`-based emission style
- generator code should build readable source directly, with indentation managed by the builder instead of post-processing

## 3.4 Generated marker types MUST use `Microsoft.CodeAnalysis.EmbeddedAttribute`

The cookbook recommends embedding marker types so consumers do not hit duplicate internal marker-type warnings across projects.

For Linqraft, all generated marker/helper types such as:

- `LinqraftAutoGeneratedDtoAttribute`
- `LinqraftMappingGenerateAttribute`
- any future generated internal marker attributes

MUST:

- be emitted during post-initialization
- carry `Microsoft.CodeAnalysis.EmbeddedAttribute`
- remain hidden from normal user-facing authoring where appropriate

## 3.5 Do not scan indirect interface/base-type/derived-attribute graphs

Roslyn's cookbook is explicit that these scans are expensive and not incrementally friendly.
The rewrite MUST NOT rely on:

- walking all types to discover indirect interface implementations
- walking base-type chains across the entire compilation as the primary marker mechanism
- supporting attribute inheritance as a customization mechanism for generator discovery

For Linqraft, that means:

- prefer explicit marker attributes or explicit syntax forms
- use analyzers to guide invalid authoring rather than broad semantic graph scans
- keep discovery local, intention-revealing, and incrementally scannable

## 4. Standard shape for incremental pipeline data

## 4.1 Use verbose records with required init properties

This rewrite adopts a stricter house rule than the Roslyn examples:

- use `record` types
- **do not** use positional/primary-constructor record declarations such as `record A(string Name, int Count)`
- instead use explicit properties with `required` + `init`

Required style:

```csharp
public sealed record SelectExprInvocationModel
{
    public required string LambdaBodyHash { get; init; }
    public required string CallerNamespace { get; init; }
    public required string FilePath { get; init; }
    public required int SpanStart { get; init; }
    public required int SpanLength { get; init; }
}
```

Rationale:

- easier partial migration from existing mutable models
- better readability during large refactors
- more patch-friendly when properties are added or removed
- more consistent with the current `PipelineModels.cs` direction

## 4.2 Use `EquatableArray<T>` for collection members inside pipeline models

Built-in collection types such as arrays, `List<T>`, and `ImmutableArray<T>` are not value-equatable by default.
That makes them poor fit for stable incremental models.

Required rule:

- any stable record that must carry an ordered collection of equatable items MUST use a wrapper equivalent to `EquatableArray<T>`
- the wrapper must implement value-based equality and stable hashing
- `T` itself should be equatable

Use the referenced implementation from IDeepCloneable as the baseline:

- `internal readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IEnumerable<T> where T : IEquatable<T>`

Suggested uses inside Linqraft:

- grouped projection descriptors
- ordered DTO property models
- ordered capture-variable models
- ordered mapping-method parameter or nested-class models

## 4.3 Remove unstable compiler objects as early as possible

The rewrite should distinguish between:

1. **early extraction stages**, where syntax or semantic objects may briefly exist
2. **stable model stages**, where only equatable value models remain

Required rule:

- `ISymbol`, `SemanticModel`, `SyntaxNode`, and raw `Location` objects MUST NOT cross stable pipeline boundaries
- if a later stage still needs semantic information, recalculate it locally from a nearer unstable source rather than storing the compiler object in the stable model

## 4.4 Prefer precomputed fully qualified strings in models

Because Linqraft must emit fully qualified code everywhere, pipeline models SHOULD precompute and store concrete fully qualified type strings once the semantic information is known.

This reduces:

- repeated symbol formatting work
- downstream ambiguity about qualification rules
- accidental mixing of simple names and fully qualified names

## 5. Code-emission standards for the rewrite

## 5.1 Use `IndentedStringBuilder` as the default code writer

The rewrite SHOULD adopt the referenced `IndentedStringBuilder` implementation, or a directly equivalent local copy, as the standard emission primitive.

Required usage pattern:

- open a block
- call `IncreaseIndent()`
- emit indented lines
- call `DecreaseIndent()`
- close the block

This should replace the majority of:

- manual `new string(' ', ...)`
- `CodeFormatter.Indent(...)`
- string concatenation that relies on downstream formatting cleanup

## 5.2 Centralize repetitive boilerplate in utility template classes

Repeated boilerplate should not be rebuilt ad hoc in multiple generators or emitters.

Required rule:

- move stable repeated fragments into utility/template containers
- generated code should reference those utilities rather than duplicating the same string literals in many places

Examples of content that belongs in template utilities:

- `[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]`
- auto-generated file headers
- common pragma blocks
- common `using` blocks when still required
- repeated attribute and marker-type declarations

The referenced `CodeTemplateContents.EditorBrowsableAttribute` pattern from IDeepCloneable is the intended model.

## 5.3 Every emitted type reference MUST be fully qualified with `global::`

This is a hard rewrite rule.
All emitted **type references** must use a `global::`-prefixed fully qualified spelling.

This includes:

- attribute type names
- base types and implemented interfaces
- property types
- field types
- method return types
- parameter types
- local variable type names
- cast targets
- object-creation type names
- generic type arguments where the argument is a concrete type
- concrete types introduced from user-authored selectors and nested projections
- helper return types in generated extension methods

Examples:

```csharp
[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]
public static global::System.Linq.IQueryable<global::MyApp.OrderDto> ProjectToOrder(
    this global::System.Linq.IQueryable<global::MyApp.Order> source)
```

```csharp
Items = source.OrderItems.Select(oi => new global::MyApp.LinqraftGenerated_ABCD1234.ItemsDto
{
    ProductName = oi.Product != null ? (global::System.String?)oi.Product.Name : null,
})
```

## 5.4 Use `$$"""..."""` for multi-line strings

When emitting multi-line string literals, the rewrite SHOULD use C# 11's raw string literal feature with interpolation (`$$"""..."""`) to preserve readability and reduce the need for manual newline and indentation management.

```csharp
// DO NOT
var sb = new IndentedStringBuilder();
sb.AppendLine("line A");
sb.AppendLine($"line {someValue}");
sb.AppendLine("line C { /* some action */ }");

// DO
var sb = new IndentedStringBuilder();
sb.AppendLine($$"""
    line A
    line {{someValue}}
    line C { /* some action */ }
    """);
```

## 5.5 Do not use Reflection

Reflection APIs such as `GetConstructor` or `GetProperty` MUST NOT be used anywhere in the generated code. All type information should be fully resolved at compile time and directly embedded in the generated code.
This means that any necessary type information must be collected during the SourceGenerator phase and directly included in the emitted code, rather than relying on runtime reflection to access type metadata.

### Practical clarification

This rule applies to **type references**, not to syntax positions where C# does not allow `global::`:

- the declared identifier of the type currently being defined (`public partial class OrderDto`) remains just `OrderDto`
- declared generic type parameter names (`TSource`, `TResult`) remain plain identifiers
- namespace declarations remain ordinary namespace syntax

Everywhere else, use fully qualified type spellings.

## 5.4 Prefer symbol-display helpers over ad hoc name assembly

To make Section 5.3 reliable, the rewrite SHOULD standardize around a small number of symbol-formatting helpers that always emit the approved fully qualified shape.

Required outcome:

- no emitter should independently decide whether to prefix `global::`
- user-authored type names and generic arguments should be normalized immediately when entering the stable pipeline

## 6. Linqraft-specific architecture guidance

## 6.1 Keep the current split between expression discovery and declarative mapping discovery

The current code already reflects the key architectural distinction:

- `SelectExpr` invocation discovery uses syntax scanning
- mapping declarations use `ForAttributeWithMetadataName`

The rewrite SHOULD keep that split, because it matches the Roslyn performance guidance and the nature of the two authoring styles.

## 6.2 Expand `PipelineModels.cs` instead of inventing ad hoc transport objects

`src\Linqraft.Core\PipelineModels.cs` is already aligned with the correct direction:

- explicit equatable records
- string/int/bool-based payloads
- comments documenting incremental-caching intent

The rewrite should treat this file, or its successor, as the canonical home for stable cross-stage models.

## 6.3 Replace formatter-style helpers with writer-style helpers

`src\Linqraft.Core\Formatting\CodeFormatter.cs` is useful as a transitional helper, but it is not the desired final abstraction.
The rewrite SHOULD migrate most generation sites toward `IndentedStringBuilder`, leaving only small utility helpers where text post-processing is still genuinely needed.

## 6.4 Consolidate generated support text

`src\Linqraft.Core\GenerateSourceCodeSnippets.cs` currently carries multiple long literal templates.
That is the right conceptual home for embedded support types, but the rewrite SHOULD further separate:

- reusable one-line boilerplate constants
- small shared snippets
- large generated support type declarations
- projection-specific emitted bodies

The goal is to reduce repeated literal fragments and make it obvious which parts are policy versus per-feature generation.

## 7. Rewrite checklist

Before considering the refactored generator architecture complete, confirm all of the following:

1. all stable pipeline handoff models are explicit-property `record`s
2. no stable model stores `ISymbol`, `SyntaxNode`, `SemanticModel`, or raw `Location`
3. all ordered collections inside stable models use `EquatableArray<T>` or an equivalent value-equatable wrapper
4. all attribute-driven discovery uses `ForAttributeWithMetadataName`
5. direct `SelectExpr` invocation discovery remains the only justified `CreateSyntaxProvider` exception
6. generated marker types are emitted in post-initialization and marked with `EmbeddedAttribute`
7. emission code uses `IndentedStringBuilder`, not `SyntaxNode` generation plus whitespace normalization
8. repetitive text boilerplate has been centralized into template utilities
9. every emitted type reference is `global::`-qualified
10. tests continue to cover the fully qualified nested-type and mapping-method scenarios that are especially sensitive to these rules

## 8. Priority recommendation

The safest order of implementation during the refactor is:

1. stabilize the incremental pipeline model layer
2. standardize emitted type qualification
3. switch emitters to `IndentedStringBuilder`
4. centralize boilerplate/template fragments
5. only then simplify or repartition the generator pipeline

That order minimizes the chance of mixing architectural cleanup with behavioral regressions in code emission.
