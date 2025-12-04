# Feasibility Study: Single Type Parameter SelectExpr<TOut> and Nested SelectExpr Support

## Executive Summary

This document presents a feasibility study for two proposed enhancements to the Linqraft source generator:

1. **Single Type Parameter**: Allow `SelectExpr<TOut>` instead of requiring `SelectExpr<TIn, TOut>`
2. **Nested SelectExpr Support**: Enable nested `SelectExpr` calls within the projection lambda

Both features are **feasible** with moderate to significant changes. This document outlines the required modifications, challenges, and implementation strategy.

---

## Table of Contents

1. [Current Architecture Overview](#current-architecture-overview)
2. [Feature 1: Single Type Parameter SelectExpr<TOut>](#feature-1-single-type-parameter-selectexprtout)
3. [Feature 2: Nested SelectExpr Support](#feature-2-nested-selectexpr-support)
4. [Implementation Plan](#implementation-plan)
5. [Risk Assessment](#risk-assessment)
6. [Conclusion](#conclusion)

---

## Current Architecture Overview

### How SelectExpr Works Today

The current Linqraft implementation supports three patterns:

#### Pattern 1: Anonymous Type (auto-inferred)
```csharp
query.SelectExpr(x => new { x.Id, x.Name });
// Returns: anonymous type (auto-generated)
```

#### Pattern 2: Explicit DTO with Two Type Parameters
```csharp
query.SelectExpr<BaseClass, SampleDto>(x => new { x.Id, x.Name });
// Returns: IQueryable<SampleDto> (DTO class is auto-generated)
```

#### Pattern 3: Predefined DTO (user-defined class)
```csharp
query.SelectExpr(x => new PredefinedDto { x.Name, x.Value });
// Returns: IQueryable<PredefinedDto>
```

### Key Components

| File | Purpose |
|------|---------|
| `src/Linqraft.Core/GenerateSourceCodeSnippets.cs` | Defines the SelectExpr extension method signatures |
| `src/Linqraft.SourceGenerator/SelectExprGenerator.cs` | Main source generator that detects and processes SelectExpr calls |
| `src/Linqraft.Core/SelectExprInfo.cs` | Base class for SelectExpr information |
| `src/Linqraft.Core/SelectExprInfoExplicitDto.cs` | Handles explicit DTO pattern (Pattern 2) |
| `src/Linqraft.Core/SelectExprInfoAnonymous.cs` | Handles anonymous type pattern (Pattern 1) |
| `src/Linqraft.Core/SelectExprInfoNamed.cs` | Handles predefined DTO pattern (Pattern 3) |
| `src/Linqraft.Core/DtoStructure.cs` | Analyzes anonymous type structures |
| `src/Linqraft.Core/DtoProperty.cs` | Analyzes property expressions within projections |

### Current Extension Method Signatures

From `GenerateSourceCodeSnippets.cs` (SelectExprExtensions constant):
```csharp
// Current explicit DTO signatures (require TIn and TResult)
public static IQueryable<TResult> SelectExpr<TIn, TResult>(
    this IQueryable<TIn> query, Func<TIn, TResult> selector) where TIn : class;

public static IQueryable<TResult> SelectExpr<TIn, TResult>(
    this IQueryable<TIn> query, Func<TIn, object> selector) where TIn : class;
```

> **Note:** Code references in this document point to methods or sections rather than exact line numbers, as line numbers may change as the codebase evolves.

---

## Feature 1: Single Type Parameter SelectExpr<TOut>

### Goal

Allow users to write:
```csharp
query.SelectExpr<IntermediateType>(d => new {
    Prop1 = d.Field1,  // d should be correctly inferred from IQueryable<T>
    Prop2 = d.Field2,
});
```

Instead of:
```csharp
query.SelectExpr<BaseClass, IntermediateType>(d => new { ... });
```

### Technical Analysis

#### Challenge 1: C# Generics Type Inference

C# does not support partial generic type argument inference. When you specify `SelectExpr<IntermediateType>(...)`, the compiler cannot infer `TIn` from the `IQueryable<TIn>` source.

**Current Behavior:**
```csharp
// This works - all type arguments specified
query.SelectExpr<Entity, Dto>(x => new { x.Id });

// This doesn't work - partial inference not supported
query.SelectExpr<Dto>(x => new { x.Id }); // Compiler error: cannot infer TIn
```

#### Solution: Add New Overload with Single Type Parameter

Add a new extension method signature that only requires the output type:

```csharp
// NEW: Single type parameter version
public static IQueryable<TResult> SelectExpr<TResult>(
    this IQueryable<object> query, Func<object, object> selector);

// OR use a generic constraint approach:
public static IQueryable<TResult> SelectExpr<TResult>(
    this IQueryable query, Func<object, object> selector);
```

However, this approach has a significant problem: **the lambda parameter `d` would have type `object`**, losing IntelliSense and type safety at design time.

#### Recommended Solution: Source Generator Workaround

Since Linqraft is a source generator that intercepts method calls at compile time, we can leverage this:

1. **Define a marker method** with single type parameter that takes `Func<object, object>`
2. **At generation time**, the source generator already knows the actual `TIn` type from the `IQueryable<TIn>` expression
3. **Generate the interceptor** with the correctly typed lambda

**Implementation Strategy:**

```csharp
// In GenerateSourceCodeSnippets.cs - Add new marker method:
public static IQueryable<TResult> SelectExpr<TResult>(
    this IQueryable query, Func<object, object> selector)
    where TResult : class 
    => throw InvalidException;
```

**Key Insight:** The source generator's `GetSelectExprInfo` method already extracts the source type from `IQueryable<TIn>`:

```csharp
// From SelectExprGenerator.cs, GetAnonymousSelectExprInfo method:
var typeInfo = semanticModel.GetTypeInfo(memberAccess.Expression);
if (typeInfo.Type is not INamedTypeSymbol namedType)
    return null;

// Get T from IQueryable<T>
var sourceType = namedType.TypeArguments.FirstOrDefault();
```

### Required Modifications for Feature 1

#### 1. `src/Linqraft.Core/GenerateSourceCodeSnippets.cs`

**Add new extension method signatures:**

```csharp
// Lines ~150-190: Add after existing SelectExpr signatures

/// <summary>
/// Create select expression method with single type parameter.
/// The input type is inferred from the IQueryable source at compile time.
/// </summary>
public static IQueryable<TResult> SelectExpr<TResult>(
    this IQueryable query, Func<object, object> selector)
    where TResult : class => throw InvalidException;

/// <summary>
/// Create select expression method with single type parameter (IEnumerable version).
/// </summary>
public static IEnumerable<TResult> SelectExpr<TResult>(
    this IEnumerable query, Func<object, object> selector)
    where TResult : class => throw InvalidException;

/// <summary>
/// Create select expression method with single type parameter and capture.
/// </summary>
public static IQueryable<TResult> SelectExpr<TResult>(
    this IQueryable query, Func<object, object> selector, object capture)
    where TResult : class => throw InvalidException;

/// <summary>
/// Create select expression method with single type parameter and capture (IEnumerable version).
/// </summary>
public static IEnumerable<TResult> SelectExpr<TResult>(
    this IEnumerable query, Func<object, object> selector, object capture)
    where TResult : class => throw InvalidException;
```

#### 2. `src/Linqraft.SourceGenerator/SelectExprGenerator.cs`

**Modify `GetSelectExprInfo` method to handle single type parameter:**

```csharp
// In the GetSelectExprInfo method, add handling for single type parameter
// after the check for lambda expression (around the area that checks for generic invocations)

// 2. Check for SelectExpr<TResult> (single type parameter)
if (
    invocation.Expression is MemberAccessExpressionSyntax memberAccess
    && memberAccess.Name is GenericNameSyntax genericName
    && genericName.TypeArgumentList.Arguments.Count == 1  // <-- NEW: Single type parameter
    && body is AnonymousObjectCreationExpressionSyntax anonSyntax
)
{
    return GetSingleTypeParamSelectExprInfo(
        context,
        anonSyntax,
        genericName,
        lambdaParamName,
        captureArgExpr,
        captureType
    );
}
```

**Add new method `GetSingleTypeParamSelectExprInfo`:**

```csharp
private static SelectExprInfoExplicitDto? GetSingleTypeParamSelectExprInfo(
    GeneratorSyntaxContext context,
    AnonymousObjectCreationExpressionSyntax anonymousObj,
    GenericNameSyntax genericName,
    string lambdaParameterName,
    ExpressionSyntax? captureArgumentExpression,
    ITypeSymbol? captureArgumentType
)
{
    var invocation = (InvocationExpressionSyntax)context.Node;
    var semanticModel = context.SemanticModel;

    // Get target type from MemberAccessExpression
    if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
        return null;

    // Get type information from the IQueryable source
    var typeInfo = semanticModel.GetTypeInfo(memberAccess.Expression);
    if (typeInfo.Type is not INamedTypeSymbol namedType)
        return null;

    // Get TIn from IQueryable<TIn> (inferred automatically)
    var sourceType = namedType.TypeArguments.FirstOrDefault();
    if (sourceType is null)
        return null;

    // Get TResult (single type parameter) - this is the explicit DTO name
    var typeArguments = genericName.TypeArgumentList.Arguments;
    if (typeArguments.Count != 1)
        return null;

    var tResultType = semanticModel.GetTypeInfo(typeArguments[0]).Type;
    if (tResultType is null)
        return null;

    // Rest of the implementation follows existing GetExplicitDtoSelectExprInfo pattern...
    // (see existing code at lines 284-355)
}
```

#### 3. `src/Linqraft.Core/SelectExprInfoExplicitDto.cs`

**Modify `GenerateSelectExprMethod` to handle single type parameter:**

The generated interceptor method signature needs to handle the new pattern:

```csharp
// Modify GenerateSelectExprMethod around lines 300-414
// Add conditional logic for single vs dual type parameter generation

// For single type parameter, generate:
sb.AppendLine($"public static {returnTypePrefix}<TResult> SelectExpr_{id}<TResult>(");
sb.AppendLine($"    this {returnTypePrefix} query, Func<object, object> selector)");

// vs current two type parameter:
sb.AppendLine($"public static {returnTypePrefix}<TResult> SelectExpr_{id}<TIn, TResult>(");
sb.AppendLine($"    this {returnTypePrefix}<TIn> query, Func<TIn, object> selector)");
```

#### 4. User Experience Consideration: IntelliSense

**Challenge:** With `Func<object, object>`, the lambda parameter `d` has type `object`, losing IntelliSense for the source type's properties.

**Possible Mitigations:**

1. **Analyzer hint**: Create an analyzer that provides completion suggestions based on the IQueryable source type
2. **Documentation**: Clearly document this trade-off
3. **IDE extension**: Create a Roslyn-based extension for better completion (out of scope for initial implementation)

**Alternative Approach - Preserve Type Safety:**

Instead of using non-generic `IQueryable`, we could use an unconstrained generic parameter:

```csharp
// Better approach - maintains type safety
public static IQueryable<TResult> SelectExpr<TSource, TResult>(
    this IQueryable<TSource> query, Func<TSource, object> selector)
    where TSource : class
    where TResult : class => throw InvalidException;
```

However, this still requires specifying both type parameters. The trade-off between type safety and single-parameter syntax is fundamental to C# generics.

**Recommendation:** Accept this trade-off for the initial implementation. Users who need full IntelliSense can continue using the two-type-parameter version.

---

## Feature 2: Nested SelectExpr Support

### Goal

Enable nested `SelectExpr` calls:

```csharp
query.SelectExpr<IntermediateType>(d => new {
    Prop1 = d.Field1,
    Prop2 = d.Field2,
    // Nested SelectExpr generates AnotherType class
    AnotherNamedProp = d.Childs.SelectExpr<AnotherType>(c => new {
        SubProp = c.SubField
    })
});
```

### Technical Analysis

#### Current Nested Select Handling

Linqraft already handles nested `Select` calls (not `SelectExpr`):

```csharp
// This already works:
query.SelectExpr<BaseClass, IntermediateType>(d => new {
    Children = d.Childs.Select(c => new { c.Name })  // Anonymous type inside Select
});
```

The `DtoProperty.AnalyzeExpression` method (lines 143-267) already detects nested `Select` invocations and creates `DtoStructure` for anonymous types within.

#### Challenge: Detecting Nested SelectExpr

The key difference is that `SelectExpr` is a custom extension method, not a standard LINQ method. The current detection logic in `DtoProperty.cs` uses:

```csharp
private static InvocationExpressionSyntax? FindSelectInvocation(ExpressionSyntax expression)
{
    return LinqMethodHelper.FindLinqMethodInvocation(expression, "Select");
}
```

We need to add similar detection for `SelectExpr`.

### Required Modifications for Feature 2

#### 1. `src/Linqraft.Core/AnalyzerHelpers/LinqMethodHelper.cs`

**Add SelectExpr detection:**

```csharp
// Add new method or extend FindLinqMethodInvocation
public static InvocationExpressionSyntax? FindSelectExprInvocation(ExpressionSyntax expression)
{
    return FindLinqMethodInvocation(expression, "SelectExpr");
}
```

**Note:** The existing `SelectExprHelper.IsSelectExprInvocationSyntax` method in `src/Linqraft.Core/SelectExprHelper.cs` provides syntax-level detection of SelectExpr calls. This can be reused or extended for nested detection.

#### 2. `src/Linqraft.Core/DtoProperty.cs`

**Add detection for nested SelectExpr (in the AnalyzeExpression method, after SelectMany detection):**

```csharp
// After SelectMany detection block, add SelectExpr detection:

// Detect nested SelectExpr (e.g., s.Childs.SelectExpr<NestedDto>(c => new { ... }))
if (nestedStructure is null)
{
    var selectExprInvocation = FindSelectExprInvocation(expression);
    if (selectExprInvocation is not null && selectExprInvocation.ArgumentList.Arguments.Count > 0)
    {
        // Extract type argument (the explicit DTO name)
        var selectExprGenericName = GetGenericNameFromInvocation(selectExprInvocation);
        if (selectExprGenericName != null)
        {
            var typeArgs = selectExprGenericName.TypeArgumentList.Arguments;
            // Get TResult type (could be single or dual type parameter)
            var resultTypeArg = typeArgs.Count == 1 ? typeArgs[0] : 
                               (typeArgs.Count >= 2 ? typeArgs[1] : null);
            
            if (resultTypeArg != null)
            {
                var resultType = semanticModel.GetTypeInfo(resultTypeArg).Type;
                // Mark this as needing DTO generation with the explicit name
                // ... (similar to existing explicit DTO handling)
            }
        }

        var lambdaArg = selectExprInvocation.ArgumentList.Arguments[0].Expression;
        if (lambdaArg is LambdaExpressionSyntax nestedLambda)
        {
            // Get collection element type from the SelectExpr's source
            ITypeSymbol? collectionType = GetCollectionTypeFromSelectExpr(
                selectExprInvocation, semanticModel);
            
            if (collectionType is INamedTypeSymbol namedCollectionType
                && namedCollectionType.TypeArguments.Length > 0)
            {
                var elementType = namedCollectionType.TypeArguments[0];
                
                if (nestedLambda.Body is AnonymousObjectCreationExpressionSyntax nestedAnonymous)
                {
                    nestedStructure = DtoStructure.AnalyzeAnonymousType(
                        nestedAnonymous,
                        semanticModel,
                        elementType,
                        propertyName,  // Use property name as hint for DTO naming
                        configuration
                    );
                    
                    // Mark that this nested structure should use the explicit DTO name
                    // from the SelectExpr type argument
                    isNestedFromExplicitSelectExpr = true;
                }
            }
        }
    }
}
```

#### 3. `src/Linqraft.SourceGenerator/SelectExprGenerator.cs`

**Handle nested SelectExpr in the main generator:**

The main generator (`SelectExprGenerator.cs`) needs to collect nested SelectExpr invocations and generate DTOs for them.

**Option A: Recursive Analysis**
Modify `GetSelectExprInfo` to recursively analyze nested SelectExpr calls and collect all DTO generation requests.

**Option B: Multi-pass Generation**
First pass: collect all SelectExpr invocations (including nested ones)
Second pass: generate all DTOs and interceptors

**Recommendation: Option A** - Recursive analysis is more aligned with the current architecture.

```csharp
// In GetSelectExprInfo or a new helper method:
private static List<SelectExprInfo> CollectNestedSelectExprInfos(
    AnonymousObjectCreationExpressionSyntax anonymousObj,
    SemanticModel semanticModel,
    ITypeSymbol sourceType)
{
    var nestedInfos = new List<SelectExprInfo>();
    
    foreach (var initializer in anonymousObj.Initializers)
    {
        var expression = initializer.Expression;
        
        // Find any SelectExpr invocations in this expression
        var selectExprInvocations = expression
            .DescendantNodesAndSelf()
            .OfType<InvocationExpressionSyntax>()
            .Where(inv => SelectExprHelper.IsSelectExprInvocationSyntax(inv.Expression));
        
        foreach (var nestedInvocation in selectExprInvocations)
        {
            // Recursively process nested SelectExpr
            var nestedInfo = ProcessNestedSelectExpr(nestedInvocation, semanticModel);
            if (nestedInfo != null)
            {
                nestedInfos.Add(nestedInfo);
            }
        }
    }
    
    return nestedInfos;
}
```

#### 4. `src/Linqraft.Core/SelectExprInfo.cs`

**Track nested SelectExpr relationships:**

Add a property to track parent-child relationships for proper code generation:

```csharp
/// <summary>
/// Nested SelectExpr information collected from this expression's properties
/// </summary>
public List<SelectExprInfo> NestedSelectExprs { get; set; } = new();
```

#### 5. `src/Linqraft.Core/GenerateDtoClassInfo.cs`

**Ensure nested DTO generation respects explicit names:**

Modify the DTO generation to use the explicit type name from nested `SelectExpr<NestedType>` calls rather than auto-generating a name.

---

## Implementation Plan

### Phase 1: Single Type Parameter Support (Lower complexity)

| Task | File(s) | Estimated Effort |
|------|---------|------------------|
| Add new extension method signatures | `GenerateSourceCodeSnippets.cs` | Low |
| Add type argument count check in generator | `SelectExprGenerator.cs` | Low |
| Add `GetSingleTypeParamSelectExprInfo` method | `SelectExprGenerator.cs` | Medium |
| Modify interceptor generation for single param | `SelectExprInfoExplicitDto.cs` | Medium |
| Add tests for single type parameter | `tests/Linqraft.Tests/` | Medium |
| Documentation updates | `README.md`, comments | Low |

**Total Estimated Effort: 2-3 days**

### Phase 2: Nested SelectExpr Support (Higher complexity)

| Task | File(s) | Estimated Effort |
|------|---------|------------------|
| Add `FindSelectExprInvocation` helper | `LinqMethodHelper.cs` | Low |
| Add nested SelectExpr detection in DtoProperty | `DtoProperty.cs` | High |
| Recursive nested SelectExpr collection | `SelectExprGenerator.cs` | High |
| Track nested relationships in SelectExprInfo | `SelectExprInfo.cs` | Medium |
| Modify DTO generation for explicit nested names | `GenerateDtoClassInfo.cs`, `SelectExprInfoExplicitDto.cs` | High |
| Add comprehensive tests | `tests/Linqraft.Tests/` | High |
| Handle edge cases (multiple nesting levels, etc.) | Various | Medium |

**Total Estimated Effort: 5-7 days**

### Phase 3: Integration and Testing

| Task | Estimated Effort |
|------|------------------|
| Integration testing of both features together | Medium |
| Performance testing (compilation time impact) | Low |
| Edge case handling and bug fixes | Medium |
| Documentation and examples | Low |

**Total Estimated Effort: 2-3 days**

---

## Risk Assessment

### Low Risk

1. **Breaking changes to existing API**: The new single-parameter overload is additive; existing code continues to work.
2. **Build system compatibility**: No changes to build pipeline or project structure.

### Medium Risk

1. **IntelliSense degradation**: Single type parameter version loses property completion. Mitigation: document trade-off clearly.
2. **Nested SelectExpr complexity**: Recursive analysis may miss edge cases. Mitigation: comprehensive test coverage.

### High Risk

1. **Ambiguous method resolution**: New overloads might cause ambiguity in certain scenarios. Mitigation: careful testing and use of `OverloadResolutionPriorityAttribute` (available in .NET 9+). For earlier .NET versions, alternative disambiguation through method naming or additional parameters may be required.
2. **Recursive infinite loops**: Deeply nested or circular SelectExpr references. Mitigation: add recursion depth limits.

---

## Conclusion

### Feasibility Summary

| Feature | Feasibility | Complexity | Recommended Priority |
|---------|-------------|------------|---------------------|
| Single Type Parameter `SelectExpr<TOut>` | ✅ Feasible | Medium | High |
| Nested SelectExpr Support | ✅ Feasible | High | Medium |

### Recommendation

1. **Implement Feature 1 first** (Single Type Parameter): Lower complexity, high user value, and provides a foundation for Feature 2.

2. **Implement Feature 2 second** (Nested SelectExpr): More complex but builds on Feature 1. Can be delivered iteratively.

3. **Accept IntelliSense trade-off**: For the single type parameter version, users lose property IntelliSense. This is acceptable for users who prefer concise syntax over IDE assistance.

4. **Consider future enhancement**: A Roslyn analyzer could provide custom IntelliSense for SelectExpr calls, but this is out of scope for the initial implementation.

---

## File Change Summary

| File | Feature 1 | Feature 2 | Type of Change |
|------|-----------|-----------|----------------|
| `src/Linqraft.Core/GenerateSourceCodeSnippets.cs` | ✅ | ❌ | Add methods |
| `src/Linqraft.SourceGenerator/SelectExprGenerator.cs` | ✅ | ✅ | Modify/Add |
| `src/Linqraft.Core/SelectExprInfoExplicitDto.cs` | ✅ | ✅ | Modify |
| `src/Linqraft.Core/DtoProperty.cs` | ❌ | ✅ | Modify |
| `src/Linqraft.Core/AnalyzerHelpers/LinqMethodHelper.cs` | ❌ | ✅ | Add method |
| `src/Linqraft.Core/SelectExprInfo.cs` | ❌ | ✅ | Add property |
| `src/Linqraft.Core/SelectExprHelper.cs` | ❌ | ✅ | Extend existing |
| `tests/Linqraft.Tests/*.cs` | ✅ | ✅ | Add test files |

---

*Document prepared as part of feasibility study for Linqraft SelectExpr enhancement.*
