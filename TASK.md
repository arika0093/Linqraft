# Current Tasks - Test Failures Resolution

## Overview
27 tests are failing in `Linqraft.Analyzer.Tests`. The main issues are:
1. Using directive spacing issues
2. Line ending handling (CR+LF vs LF)
3. Object initializer formatting (indentation and spacing)

## Test Failure Categories

### 1. Using Directive Spacing Issues (3 tests)
**Files affected**: Tests using `Issue98Tests`
- `Issue98_AddsUsingDirective_ForSourceType_LQRS001`
- `Issue98_AddsUsingDirective_ForSourceType`
- `Issue98_AddsUsingDirective_ForSourceType_LQRS003`

**Problem**: Missing space after `using` keyword
- Expected: `using MyNamespace;`
- Actual: `usingMyNamespace;`

**Root cause**: `UsingDirectiveHelper` or code fix providers adding using directives

### 2. Object Initializer Formatting Issues (13 tests)
**Files affected**:
- `AnonymousTypeToDtoCodeFixProviderTests` (7 tests)
- `LocalVariableCaptureCodeFixProviderTests` (6 tests)

**Problems**:
- Object initializers compressed to single line without spaces
- Expected: Multi-line with proper indentation
- Actual: `newResultDto{ Id = 1,Name = "Test" }` (missing spaces after keywords and commas)

**Root cause**: `CodeFixFormattingHelper` not preserving proper formatting

### 3. Ternary to Null-Conditional Conversion Issues (5 tests)
**Files affected**: `TernaryNullCheckToConditionalCodeFixProviderTests`
- `CodeFix_ConvertsTernaryToNullConditional_WithNullableCast`
- `CodeFix_ConvertsTernaryToNullConditional_InvertedCondition`
- `CodeFix_ConvertsTernaryToNullConditional_IssueScenario`
- `CodeFix_ConvertsTernaryToNullConditional_NestedNullChecks`
- `CodeFix_ConvertsTernaryToNullConditional_SimpleCase`

**Problem**: Object initializers not properly formatted after conversion

### 4. Line Ending Issues (2 tests)
**Files affected**:
- `Issue102Tests.Issue102_DoesNotConvert_EmptyListInitializers`
- `Issue97TestsV2.Issue97_PreservesWhitespace_InConversionAll`

**Problem**: Test output showing `<CR><LF>` instead of actual line breaks
**Root cause**: Line ending normalization not working correctly

### 5. Select Expression Conversion (1 test)
**Files affected**: `SelectToSelectExprAnonymousCodeFixProviderTests`
- `CodeFix_DoesNotSimplifyTernaryNullCheck_WhenReturningObject`

**Problem**: Object initializer formatting after conversion

### 6. Local Variable Capture Formatting (1 test)
**Files affected**: `LocalVariableCaptureCodeFixProviderTests`
- `MultipleLocalVariables_AddsCapture`

**Problem**: Missing space after comma in capture object
- Expected: `new { localVar1, localVar2 }`
- Actual: `new { localVar1,localVar2 }`

## Action Plan

### Phase 1: Fix Using Directive Spacing (High Priority)
- [ ] Investigate `UsingDirectiveHelper.AddUsingDirectiveIfMissing()`
- [ ] Ensure proper spacing after `using` keyword
- [ ] Test with Issue98 test cases

### Phase 2: Fix Object Initializer Formatting (Critical)
- [ ] Investigate `CodeFixFormattingHelper`
- [ ] Check how object initializers are being normalized
- [ ] Ensure proper spacing after `new`, around `{`, `}`, and after commas
- [ ] Preserve multi-line formatting where appropriate
- [ ] Test with one AnonymousTypeToDtoCodeFixProvider test first
- [ ] Apply to remaining tests if successful

### Phase 3: Fix Line Ending Normalization (Medium Priority)
- [ ] Review `TriviaHelper.NormalizeLineEndings()`
- [ ] Decide: normalize all to LF OR use environment-specific
- [ ] User preference: **normalize all to LF or match environment**
- [ ] Update test expectations accordingly

### Phase 4: Verification
- [ ] Run all tests and verify 27 failures are resolved
- [ ] Ensure no regressions in passing tests
- [ ] Clean build and test

## Decision Needed
**Line Ending Strategy**: Choose one approach:
- Option A: Normalize all to LF (`\n`) for consistency across platforms
- Option B: Use environment-specific line endings (current platform)

**Recommendation**: Option A (normalize to LF) for better cross-platform compatibility.

## Progress Tracking
- Total failing tests: 27
- Using directive issues: 3
- Object initializer issues: 13
- Ternary conversion issues: 5
- Line ending issues: 2
- Select expression issues: 1
- Capture formatting issues: 1
- Mixed issues: 2

## Next Steps
1. Start with **Phase 1** (using directive spacing) - smallest, isolated issue
2. Move to **Phase 2** (object initializer formatting) - affects most tests
3. Then **Phase 3** (line endings) if needed
4. Final verification in **Phase 4**
