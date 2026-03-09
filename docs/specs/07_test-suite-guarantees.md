# 07. Test Suite Guarantees

## 1. Purpose

This document records what the **current test suite actually enforces**.
It is intentionally narrower than the user-facing documentation set:

- if a behavior appears here, there is current automated coverage for it
- if a behavior is documented elsewhere but absent here, it is a desired product contract but **not** a strongly test-locked guarantee yet

This distinction matters because the test suite itself is a candidate for slimming and deduplication during the refactor.
The goal of this document is to preserve the meaningful guarantees while making it easier to simplify overlapping tests later.

## 2. Test corpus covered by this document

| Project | Role | Representative files |
| --- | --- | --- |
| `tests\Linqraft.Tests` | generator/runtime/integration coverage | `SimpleCaseTest.cs`, `ExplicitDtoComprehensiveTest.cs`, `NestedCaseTest.cs`, `LocalVariableCaptureTest.cs`, `LinqraftMappingGenerateTest.cs` |
| `tests\Linqraft.Tests.Analyzer` | analyzer and code-fix coverage | `AnonymousTypeToDtoAnalyzerTests.cs`, `SelectToSelectExprNamedCodeFixProviderTests.cs`, `LocalVariableCaptureAnalyzerTests.cs`, `GeneratedHashedNamespaceUsageAnalyzerTests.cs` |
| `tests\Linqraft.Tests.Configuration` | configuration-oriented smoke/integration coverage | `Program.cs` |

## 3. Generator/runtime guarantees currently enforced

## 3.1 Core `SelectExpr` and DTO generation

The current tests guarantee the following baseline behaviors:

| Guaranteed behavior | Primary test coverage |
| --- | --- |
| `SelectExpr` can generate DTOs from anonymous-shape projections on normal query flows | `tests\Linqraft.Tests\SimpleCaseTest.cs`, `tests\Linqraft.Tests\AnonymousCaseTest.cs` |
| the same source type can generate multiple distinct DTOs from different projection shapes without name collision | `tests\Linqraft.Tests\SameClassManyPatternsTest.cs`, `tests\Linqraft.Tests\SimpleCaseTest.cs` |
| `SelectExpr<TSource, TResult>` produces the caller-selected root DTO type | `tests\Linqraft.Tests\SimpleCaseTest.cs`, `tests\Linqraft.Tests\ExplicitDtoComprehensiveTest.cs` |
| predefined DTO object creation continues to work as a supported pattern | `tests\Linqraft.Tests\PropertyAccessibilityTest.cs`, `tests\Linqraft.Tests\Issue172_PredefinedNestedNamedTypesTest.cs` |
| `SelectExpr` works on both `IQueryable<T>` and `IEnumerable<T>` receivers | `tests\Linqraft.Tests\IEnumerableCaseTest.cs`, `tests\Linqraft.Tests\LocalVariableCaptureTest.cs` |
| long LINQ chains before `SelectExpr` remain supported | `tests\Linqraft.Tests\SimpleCaseTest.cs`, `tests\Linqraft.Tests\AnonymousCaseTest.cs`, `tests\Linqraft.Tests\IEnumerableCaseTest.cs` |

Representative locked scenarios:

- `SimpleCaseTest.Case2ManyLinqMethods()` verifies `Where(...)` and downstream aggregate usage before projection.
- `IEnumerableCaseTest` verifies the same projection style is usable on in-memory sequences.
- `SameClassManyPatternsTest` guards against accidental DTO-name reuse when the same source type participates in multiple projection shapes.

## 3.2 Null-conditional handling and nullability

The null-handling area is one of the most heavily exercised parts of the suite.
Current tests lock the following behaviors:

| Guaranteed behavior | Primary test coverage |
| --- | --- |
| multi-hop null-conditional chains are supported in generated projections | `tests\Linqraft.Tests\NestedCaseTest.cs`, `tests\Linqraft.Tests\Issue_NullConditionalWithChainsTest.cs`, `tests\Linqraft.Tests\TutorialCaseTest.cs` |
| null-conditional handling works in predefined DTO initializers as well as anonymous-shape projections | `tests\Linqraft.Tests\NestedCaseTest.cs`, `tests\Linqraft.Tests\Issue193_NullConditionalInInitializerTest.cs` |
| collection properties derived from `?.Select(...)` or `?.SelectMany(...)` can become non-nullable with empty fallback behavior | `tests\Linqraft.Tests\NullableCollectionWithEmptyFallbackTest.cs`, `tests\Linqraft.Tests\ListNullabilityCaseTest.cs` |
| lambda-context nullability is preserved in minimal-API-like usage | `tests\Linqraft.Tests\Issue132_LambdaNullabilityTest.cs` |
| null-conditional access inside object initializers affects the member assignment, not the whole object creation | `tests\Linqraft.Tests\Issue193_NullConditionalInInitializerTest.cs` |
| empty-array/empty-enumerable fallbacks are emitted using explicit LINQ-friendly forms | `tests\Linqraft.Tests\Issue193_NullConditionalInInitializerTest.cs` |
| byte-array and binary-data cases do not regress nullability handling | `tests\Linqraft.Tests\ByteArrayCaseTest.cs` |

Representative locked scenarios:

- `NullableCollectionWithEmptyFallbackTest.NullableParent_WithSelectSimple_ShouldGenerateNonNullableCollection()` locks the "empty collection instead of nullable collection" behavior.
- `Issue193_NullConditionalInInitializerTest` locks the distinction between "nullable member value" and "nullable whole object".
- `Issue132_LambdaNullabilityTest` locks that nullable reference information is not accidentally over-normalized when `SelectExpr` appears inside a lambda-driven API pattern.

## 3.3 Nested DTO generation, naming, and deduplication

Current tests guarantee:

| Guaranteed behavior | Primary test coverage |
| --- | --- |
| nested anonymous projections create nested DTOs | `tests\Linqraft.Tests\Issue59_DirectNestedAnonymousTypeTest.cs`, `tests\Linqraft.Tests\NestedCaseTest.cs` |
| repeated identical nested shapes can deduplicate to a shared generated DTO definition | `tests\Linqraft.Tests\Issue239_MinimalReproTest.cs`, `tests\Linqraft.Tests\Issue239_DuplicateChildDtoTest.cs` |
| nested named DTO types inside projections are emitted with fully qualified type references | `tests\Linqraft.Tests\Issue172_PredefinedNestedNamedTypesTest.cs` |
| nested DTOs still work when partial types are nested inside other types | `tests\Linqraft.Tests\PartialNestedDtoTest.cs`, `tests\Linqraft.Tests\Issue_ClassInClassGeneratedTest.cs` |
| nested `SelectExpr` remains supported through issue-regression coverage | `tests\Linqraft.Tests\Issue207_NestedSelectExprTest.cs`, `tests\Linqraft.Tests\Issue217_NestedSelectExprTest.cs` |

The full-qualification guarantee is especially important for the refactor because it overlaps with the future rewrite rule that generated type spellings must consistently use `global::`-qualified names.

## 3.4 Namespace placement, visibility, and user-owned DTO shape

Current tests guarantee:

| Guaranteed behavior | Primary test coverage |
| --- | --- |
| generated DTOs are placed in the caller namespace rather than blindly following the source entity namespace | `tests\Linqraft.Tests\CrossNamespaceTest.cs` |
| global/root namespace scenarios remain supported | `tests\Linqraft.Tests\RootNamespaceTest.cs`, `tests\Linqraft.Tests\GlobalNamespaceNestedTest.cs` |
| internal partial DTOs remain supported | `tests\Linqraft.Tests\InternalPartialClassTest.cs` |
| user-predeclared property accessibility is respected instead of being overwritten by generation | `tests\Linqraft.Tests\PropertyAccessibilityTest.cs` |
| generated files land in expected output locations when generated-file inspection is enabled | `tests\Linqraft.Tests\ExplicitDtoLocationTest.cs` |

These tests collectively lock the rule that DTO generation is not only about member shape, but also about **where** the generated types live and how they coexist with user-authored partial declarations.

## 3.5 Captures, composed expressions, and advanced LINQ operators

Current tests guarantee:

| Guaranteed behavior | Primary test coverage |
| --- | --- |
| capture syntax works for anonymous, explicit DTO, and predefined DTO patterns | `tests\Linqraft.Tests\LocalVariableCaptureTest.cs` |
| captured values can include primitives, `DateTime`, request objects, destructured members, and nested member-access values | `tests\Linqraft.Tests\LocalVariableCaptureTest.cs` |
| `SelectMany` is supported for flattening scenarios | `tests\Linqraft.Tests\SelectManyCaseTest.cs` |
| `FirstOrDefault()`-style patterns remain supported inside projections | `tests\Linqraft.Tests\Issue_SelectFirstOrDefaultTest.cs` |
| `OfType<T>()` inside projection logic remains supported | `tests\Linqraft.Tests\Issue_OfTypeInSelectExprTest.cs` |
| ordinary aggregate expressions such as `Count`, `Sum`, and `Max` remain usable as calculated fields | `tests\Linqraft.Tests\SimpleCaseTest.cs`, `tests\Linqraft.Tests\TutorialCaseTest.cs` |
| ternary/null-conditional combinations do not regress object-shape generation | `tests\Linqraft.Tests\Issue_TernaryAndNullConditionalWithAnonymousTypeTest.cs`, `tests\Linqraft.Tests\TernaryNestedDtoTest.cs`, `tests\Linqraft.Tests\TernaryWithSelectIssueTest.cs` |

## 3.6 Mapping-method and reusable projection guarantees

Current tests guarantee:

| Guaranteed behavior | Primary test coverage |
| --- | --- |
| `[LinqraftMappingGenerate]` can generate reusable extension methods from a declared template | `tests\Linqraft.Tests\LinqraftMappingGenerateTest.cs` |
| `LinqraftMappingDeclare<T>` plus `DefineMapping()` can generate reusable extension methods | `tests\Linqraft.Tests\LinqraftMappingDeclareTest.cs` |
| custom generated method names are honored | `tests\Linqraft.Tests\LinqraftMappingGenerateTest.cs`, `tests\Linqraft.Tests\LinqraftMappingDeclareTest.cs` |
| nested collection projections remain supported in the mapping-method workflows | `tests\Linqraft.Tests\LinqraftMappingGenerateTest.cs`, `tests\Linqraft.Tests\LinqraftMappingDeclareTest.cs` |

These tests lock the fact that mapping methods are not a documentation-only feature; they are exercised runtime behavior.

## 3.7 Issue-regression coverage

Issue-titled tests currently act as an important compatibility layer.
They preserve past bug fixes and edge-case semantics including:

| Issue-focused guarantee | Primary file |
| --- | --- |
| comments inside authored `SelectExpr` should not break generation | `tests\Linqraft.Tests\Issue109_CommentsInSelectExprTest.cs` |
| static values in the query flow remain usable in supported patterns | `tests\Linqraft.Tests\Issue157_UseStaticValueInQuery.cs` |
| nested object creation continues to work | `tests\Linqraft.Tests\Issue159_NestedObjectCreationTest.cs` |
| generic/predefined DTO interaction does not regress | `tests\Linqraft.Tests\Issue80_GenericAndPredefinedTest.cs` |
| sealed and interface-heavy DTO scenarios remain valid | `tests\Linqraft.Tests\Issue33_SealedPatternTest.cs` |
| nested property documentation can survive generation | `tests\Linqraft.Tests\Issue_NestedPropertyDocumentationTest.cs` |

## 4. Analyzer and code-fix guarantees currently enforced

The analyzer test project locks not just diagnostic existence, but also the intended migration path.

| Diagnostic or feature | Current test-enforced guarantee | Primary test coverage |
| --- | --- | --- |
| `LQRF002` ApiControllerProducesResponseType | API controllers using typed `SelectExpr` can receive response-metadata suggestions and fixes | `tests\Linqraft.Tests.Analyzer\ApiControllerProducesResponseTypeAnalyzerTests.cs`, `tests\Linqraft.Tests.Analyzer\ApiControllerProducesResponseTypeCodeFixProviderTests.cs` |
| `LQRS001` SelectExprToTyped | anonymous `SelectExpr` calls can be promoted to `SelectExpr<TSource, TDto>` with inferred DTO naming | `tests\Linqraft.Tests.Analyzer\SelectExprToTypedAnalyzerTests.cs` |
| `LQRS002` anonymous `Select` to `SelectExpr` | only `IQueryable` anonymous projections are targeted, and the hidden suggestion still keeps both Linqraft conversion fixes available | `tests\Linqraft.Tests.Analyzer\SelectToSelectExprAnonymousAnalyzerTests.cs`, `tests\Linqraft.Tests.Analyzer\SelectToSelectExprAnonymousCodeFixProviderTests.cs` |
| `LQRS003` named `Select` to `SelectExpr` | named-object projections can be converted through the supported fix variants | `tests\Linqraft.Tests.Analyzer\SelectToSelectExprNamedAnalyzerTests.cs`, `tests\Linqraft.Tests.Analyzer\SelectToSelectExprNamedCodeFixProviderTests.cs` |
| `LQRS004` ternary simplification | null-guard ternaries with object creation inside `SelectExpr` are flagged and simplified via code fix | `tests\Linqraft.Tests.Analyzer\TernaryNullCheckToConditionalAnalyzerTests.cs`, `tests\Linqraft.Tests.Analyzer\TernaryNullCheckToConditionalCodeFixProviderTests.cs` |
| `LQRS005` unnecessary capture | unused capture entries are diagnosed and can be removed automatically | `tests\Linqraft.Tests.Analyzer\UnnecessaryCaptureAnalyzerTests.cs`, `tests\Linqraft.Tests.Analyzer\UnnecessaryCaptureCodeFixProviderTests.cs` |
| `LQRE001` local-variable capture | uncaptured outer values are errors, and fixes add or amend `capture:` objects | `tests\Linqraft.Tests.Analyzer\LocalVariableCaptureAnalyzerTests.cs`, `tests\Linqraft.Tests.Analyzer\LocalVariableCaptureCodeFixProviderTests.cs` |
| `LQRE002` anonymous `GroupBy` keys | anonymous grouping keys feeding `SelectExpr` are rejected and can be converted to named types | `tests\Linqraft.Tests.Analyzer\GroupByAnonymousKeyAnalyzerTests.cs`, `tests\Linqraft.Tests.Analyzer\GroupByAnonymousKeyCodeFixProviderTests.cs` |
| `LQRW001` hashed namespace usage | direct dependence on hash-based generated namespaces is warned against | `tests\Linqraft.Tests.Analyzer\GeneratedHashedNamespaceUsageAnalyzerTests.cs` |
| `LQRW002` auto-generated DTO usage | explicit dependence on auto-generated nested DTO types is warned against | `tests\Linqraft.Tests.Analyzer\AutoGeneratedDtoUsageAnalyzerTests.cs` |

Additional analyzer regression files such as `Issue97TestsV2.cs`, `Issue98Tests.cs`, and `Issue102Tests.cs` lock previously fixed analyzer-specific corner cases.

## 5. What the current tests **do not** strongly lock yet

The following behaviors appear lightly tested or absent from current automated coverage and should therefore be treated carefully during the refactor:

- deep stress cases for very large nested projection graphs
- multi-file or cross-project analyzer scenarios
- suppression/pragma behavior for analyzers
- provider-specific translation behavior beyond the current functional smoke tests
- thread-safety or concurrent generator-driver execution cases
- heavy generic-constraint matrices
- broad performance regression coverage

This does **not** mean the behaviors are unsupported; it means the current suite is not the right place to infer a hard regression contract for them.

## 6. Redundancy and deduplication candidates for a slimmer future suite

The current suite contains meaningful overlap.
The following clusters are strong candidates for consolidation while preserving the underlying guarantees:

### 6.1 Null-conditional cluster

Potentially overlapping files:

- `NestedCaseTest.cs`
- `ListNullabilityCaseTest.cs`
- `ByteArrayCaseTest.cs`
- `Issue132_LambdaNullabilityTest.cs`
- `Issue193_NullConditionalInInitializerTest.cs`
- `ExplicitDtoComprehensiveTest.cs`

Suggested future shape:

- one feature-matrix-style nullability suite
- separate narrow regression tests only for historically tricky issue fixes

### 6.2 Basic DTO-generation cluster

Potentially overlapping files:

- `SimpleCaseTest.cs`
- `AnonymousCaseTest.cs`
- `SameClassManyPatternsTest.cs`

Suggested future shape:

- shared fixture data
- fewer one-off tests, more parameterized coverage around projection shape variants

### 6.3 Capture cluster

Potentially overlapping files:

- `LocalVariableCaptureTest.cs`
- `LocalVariableCaptureAnalyzerTests.cs`
- `LocalVariableCaptureCodeFixProviderTests.cs`

Suggested future shape:

- keep one runtime behavior matrix
- keep one analyzer/code-fix matrix
- avoid duplicating the same scenarios across all three layers unless the behavior differs

### 6.4 Nested projection cluster

Potentially overlapping files:

- `Issue172_PredefinedNestedNamedTypesTest.cs`
- `NestedCaseTest.cs`
- `Issue207_NestedSelectExprTest.cs`
- `Issue217_NestedSelectExprTest.cs`
- `SelectManyCaseTest.cs`

Suggested future shape:

- a reusable nested-projection scaffold
- distinct submatrices for anonymous nested DTOs, predefined nested DTOs, and nested `SelectExpr`

### 6.5 Mapping-method cluster

Potentially overlapping files:

- `LinqraftMappingGenerateTest.cs`
- `LinqraftMappingDeclareTest.cs`

Suggested future shape:

- parameterize the shared expectations
- keep only one or two path-specific tests where the workflows truly differ

### 6.6 Analyzer/code-fix boilerplate cluster

Many analyzer test classes share the same structure:

- reports diagnostic
- does not report diagnostic
- code fix rewrites the code

Suggested future shape:

- a shared analyzer/code-fix fixture base
- parameterized assertion helpers for severity, fix title, and transformed output

## 7. Refactor usage guidance

During the implementation rewrite:

1. treat the behaviors in Sections 3 and 4 as the **minimum regression bar**
2. feel free to simplify the physical test layout as long as those guarantees remain covered
3. add new tests before relying on currently untested behavior as a compatibility promise
4. prefer merged feature-matrix tests over many micro-tests when the setup and assertion logic are identical

In other words, the future test refactor should optimize for **coverage of guarantees**, not preservation of the current file-by-file layout.
