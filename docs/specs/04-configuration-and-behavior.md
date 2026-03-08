# 04. Configuration and Behavioral Controls

## 1. Overview

Linqraft uses MSBuild properties to customize generation globally.
A compatible clone MUST continue to support the documented property set, defaults, and behavior.

## 2. Required property set

| Property | Default | Allowed values | Required behavior |
| --- | --- | --- | --- |
| `LinqraftGlobalNamespace` | empty | any namespace string | Controls the namespace used when the source entity is in the global namespace |
| `LinqraftRecordGenerate` | `false` | `true` / `false` | Generates records instead of classes |
| `LinqraftPropertyAccessor` | `Default` | `Default`, `GetAndSet`, `GetAndInit`, `GetAndInternalSet` | Controls the generated property accessor style |
| `LinqraftHasRequired` | `true` | `true` / `false` | Adds or omits the `required` keyword on generated properties |
| `LinqraftCommentOutput` | `All` | `All`, `SummaryOnly`, `None` | Controls XML documentation generation for DTOs and DTO properties |
| `LinqraftArrayNullabilityRemoval` | `true` | `true` / `false` | Enables or disables nullability removal for eligible collection properties |
| `LinqraftNestedDtoUseHashNamespace` | `true` | `true` / `false` | Chooses between hash-based nested namespaces and hash-suffixed class names |
| `LinqraftUsePrebuildExpression` | `false` | `true` / `false` | Enables compile-time-built cached expressions for eligible `IQueryable` projections |

## 3. Namespace behavior

## 3.1 Root DTO namespace

The default behavior is:

- if the source type belongs to a namespace, the generated root DTO belongs to that namespace
- if the source type belongs to the global namespace, `LinqraftGlobalNamespace` determines whether the generated DTO also lives in the global namespace or in a configured fallback namespace

## 3.2 Nested DTO namespace strategies

### Hash namespace mode (`LinqraftNestedDtoUseHashNamespace=true`)

Required behavior:

- nested DTOs are emitted into a namespace segment such as `ParentNamespace.LinqraftGenerated_HASH`
- parent DTO properties reference those nested DTOs by fully qualified type

This is the documented default.

### Hash class-name mode (`LinqraftNestedDtoUseHashNamespace=false`)

Required behavior:

- nested DTOs stay in the parent namespace
- the hash moves into the class name, for example `ItemsDto_HASH`

## 4. Type-shape behavior

## 4.1 Class vs record

When `LinqraftRecordGenerate=true`, the clone MUST emit `record` instead of `class`.
The accessor default MUST follow the documented behavior:

- `Default` means `get; set;` for classes
- `Default` means `get; init;` for records

## 4.2 Property accessors

The clone MUST implement these accessor modes:

- `Default`
- `GetAndSet`
- `GetAndInit`
- `GetAndInternalSet`

The behavior applies to all generated DTO properties unless the user has predeclared the property.

## 4.3 `required` keyword

When `LinqraftHasRequired=true`, generated properties MUST include `required`.
When it is `false`, they MUST NOT.

## 5. Documentation generation

## 5.1 Supported source comment kinds

The clone MUST be able to extract DTO documentation from:

- XML summary comments
- EF Core `Comment` attributes
- `Display(Name = "...")`
- single-line comments

## 5.2 Output modes

### `All`

The clone SHOULD emit both:

- a summary describing the member
- provenance or reference remarks similar to the documented examples

### `SummaryOnly`

The clone MUST emit the summary text but omit extended source-reference remarks.

### `None`

The clone MUST suppress generated XML documentation.

## 6. Array nullability behavior

When `LinqraftArrayNullabilityRemoval=true`, the clone MUST apply the collection nullability normalization rules described in `03-code-generation-spec.md`.
When set to `false`, the clone MUST preserve the nullable collection type implied by the authored expression instead of normalizing it to a non-nullable collection plus empty default.

## 7. Prebuilt expression behavior

`LinqraftUsePrebuildExpression` is documented as a performance optimization with constraints.
A compatible clone MUST preserve those documented constraints:

- it applies only to `IQueryable`, not `IEnumerable`
- it does not apply to anonymous-result projections
- it does not apply when capture variables are present

The exact internal implementation MAY vary, but the effect SHOULD remain: eligible expressions are constructed once and reused rather than re-created for every invocation.

## 8. Partial-class extensibility

The clone MUST preserve the documented partial-class customization model.

Consumers MAY:

- add helper methods
- implement interfaces
- add attributes
- predeclare properties to control accessibility or implementation details

The generator MUST respect those predeclared properties by not emitting duplicates.

## 9. Generated DTO naming rules

## 9.1 Root DTOs

The root DTO name is caller-controlled in the explicit DTO pattern.
That name MUST be used exactly as the generated root type name.

## 9.2 Nested DTOs

The nested DTO base name is derived from the parent property name, with a DTO-style suffix.
A uniqueness component MUST be added to avoid collisions.

Documented examples include:

- `CustomerInfoDto`
- `ItemsDto`
- `ItemsDto_HASH`
- `Namespace.LinqraftGenerated_HASH.ItemsDto`

## 10. Generated-code inspection

The docs explicitly support inspecting generated output by:

1. using IDE "Go to Definition"
2. enabling:

```xml
<EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
<CompilerGeneratedFilesOutputPath>Generated</CompilerGeneratedFilesOutputPath>
```

This is not a Linqraft-specific property contract, but a compatible clone SHOULD continue to emit inspectable code that behaves well with those standard SDK features.

## 11. Behavioral notes that matter to clone compatibility

### 11.1 Generated DTOs are normal C# types

Generated DTOs MUST be usable as:

- API response contracts
- method return types
- serialization targets
- unit-test objects

### 11.2 Auto-generated nested DTOs are not intended as stable authored dependencies

Although generated nested DTOs are real types, users SHOULD be guided away from explicit long-term dependence on their unstable names.
That guidance is enforced through the analyzer set.
