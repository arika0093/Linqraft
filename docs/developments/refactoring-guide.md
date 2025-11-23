# Linqraft Codebase Refactoring Guide

This document describes the refactoring work completed to improve code maintainability, reduce duplication, and establish common patterns across the Linqraft codebase.

## Overview

The refactoring focused on creating shared helper classes and base classes to eliminate duplication and improve consistency across analyzers and code fix providers.

## Completed Phases

### Phase 2: Helper Class Creation (High Priority)

####Phase 2-1: LinqMethodHelper
**Purpose**: Centralize LINQ method detection logic

**Location**: `src/Linqraft.Core/AnalyzerHelpers/LinqMethodHelper.cs`

**Key Methods**:
- `IsSelectMethod(IMethodSymbol)` - Identifies Select method calls
- `IsSelectManyMethod(IMethodSymbol)` - Identifies SelectMany method calls
- `IsToListMethod(IMethodSymbol)` - Identifies ToList method calls

**Benefits**: Eliminates string-based method name checking, provides type-safe LINQ method detection

---

#### Phase 2-2: ExpressionHelper
**Purpose**: Centralize property name extraction from expressions

**Location**: `src/Linqraft.Core/AnalyzerHelpers/ExpressionHelper.cs`

**Key Methods**:
- `ExtractPropertyName(ExpressionSyntax)` - Extracts property names from expressions
- `ExtractMemberAccessChain(ExpressionSyntax)` - Extracts full member access chains

**Benefits**: Unified approach to analyzing expression trees

---

#### Phase 2-3: RoslynTypeHelper
**Purpose**: Replace string-based type checking with semantic analysis

**Location**: `src/Linqraft.Core/RoslynHelpers/RoslynTypeHelper.cs`

**Key Methods**:
- `IsNullableType(ITypeSymbol)` - Checks if type is nullable
- `GetNonNullableType(ITypeSymbol)` - Removes nullable annotation
- `ImplementsIQueryable(ITypeSymbol, Compilation)` - Checks IQueryable implementation
- `ImplementsIEnumerable(ITypeSymbol, Compilation)` - Checks IEnumerable implementation
- `IsAnonymousType(ITypeSymbol)` - Checks for anonymous types
- `GetGenericTypeArgument(ITypeSymbol, int)` - Safely extracts generic type arguments

**Benefits**: More accurate type checking, eliminates fragile string comparisons

---

#### Phase 2-4: NullConditionalHelper
**Purpose**: Centralize null-conditional operator handling

**Location**: `src/Linqraft.Core/SyntaxHelpers/NullConditionalHelper.cs`

**Key Methods**:
- `HasNullCheck(ExpressionSyntax)` - Detects null check patterns
- `ExtractNullChecks(ExpressionSyntax)` - Extracts null-checked expressions
- `BuildNullConditionalChain(ExpressionSyntax, List<ExpressionSyntax>)` - Builds ?. chains
- `IsNullOrNullCast(ExpressionSyntax)` - Identifies null values
- `RemoveNullableCast(ExpressionSyntax)` - Removes nullable cast expressions

**Benefits**: Consistent handling of null-conditional operators across code fixes

---

#### Phase 2-5: TriviaHelper
**Purpose**: Centralize trivia (whitespace, comments) handling

**Location**: `src/Linqraft.Core/SyntaxHelpers/TriviaHelper.cs`

**Key Methods**:
- `PreserveTrivia<T>(T, T)` - Preserves leading and trailing trivia
- `NormalizeWhitespace<T>(T)` - Normalizes whitespace with standard EOL
- `EndOfLine()` - Creates EOL trivia
- `EndOfLine(SyntaxNode)` - Creates EOL trivia matching document format
- `DetectLineEnding(SyntaxNode)` - Detects CRLF vs LF line endings
- `Whitespace(int)` - Creates whitespace trivia
- `Indentation(int)` - Creates indentation trivia

**Benefits**: Consistent formatting, cross-platform line ending support

---

### Phase 3: Analyzer/CodeFix Commonization (Medium Priority)

#### Phase 3-1: BaseLinqraftAnalyzer
**Purpose**: Create base class for all analyzers

**Location**: `src/Linqraft.Core/AnalyzerHelpers/BaseLinqraftAnalyzer.cs`

**Structure**:
```csharp
public abstract class BaseLinqraftAnalyzer : DiagnosticAnalyzer
{
    protected abstract string DiagnosticId { get; }
    protected abstract LocalizableString Title { get; }
    protected abstract LocalizableString MessageFormat { get; }
    protected abstract LocalizableString Description { get; }
    protected abstract DiagnosticSeverity Severity { get; }
    protected abstract DiagnosticDescriptor Rule { get; }

    protected abstract void RegisterActions(AnalysisContext context);
}
```

**Inheriting Analyzers** (7 total):
- AnonymousTypeToDtoAnalyzer
- ApiControllerProducesResponseTypeAnalyzer
- SelectExprToTypedAnalyzer
- SelectToSelectExprAnonymousAnalyzer
- SelectToSelectExprNamedAnalyzer
- LocalVariableCaptureAnalyzer
- TernaryNullCheckToConditionalAnalyzer

**Benefits**: Eliminated ~100 lines of boilerplate, consistent diagnostic creation

---

#### Phase 3-2: Common Analyzer Helpers
**Purpose**: Extract common analyzer patterns

**Created Helpers**:

1. **SyntaxHelper** (`src/Linqraft.Core/AnalyzerHelpers/SyntaxHelper.cs`)
   - `GetMethodNameLocation(ExpressionSyntax)` - Gets location for diagnostics
   - `IsPartOfMemberAccess(IdentifierNameSyntax)` - Checks member access context

2. **SyntaxGenerationHelper** (`src/Linqraft.Core/AnalyzerHelpers/SyntaxGenerationHelper.cs`)
   - `CreateTypedSelectExpr(ExpressionSyntax, string, string)` - Generates SelectExpr calls

3. **UsingDirectiveHelper** (`src/Linqraft.Core/AnalyzerHelpers/UsingDirectiveHelper.cs`)
   - `AddUsingDirectiveForType(SyntaxNode, ITypeSymbol)` - Adds using directives

4. **NullCheckHelper** (`src/Linqraft.Core/AnalyzerHelpers/NullCheckHelper.cs`)
   - Helper methods for null checking patterns

5. **CaptureHelper** (`src/Linqraft.Core/AnalyzerHelpers/CaptureHelper.cs`)
   - `GetCapturedVariables(InvocationExpressionSyntax)` - Finds captured variables
   - `NeedsCapture(ISymbol, LambdaExpressionSyntax, ...)` - Determines if capture needed

**Benefits**: Eliminated ~700-1000 lines of duplicated code

---

#### Phase 3-3: TernaryNullCheckSimplifier Integration
**Purpose**: Centralize ternary null check simplification

**Location**: `src/Linqraft.Analyzer/TernaryNullCheckSimplifier.cs`

**Added Method**:
- `SimplifyTernaryNullChecksInInvocation(InvocationExpressionSyntax)` - Simplifies ternary null checks in lambda bodies

**Updated Files**:
- SelectToSelectExprAnonymousCodeFixProvider
- SelectToSelectExprNamedCodeFixProvider

**Benefits**: Eliminated ~80 lines of duplicate code

---

## Architecture Guidelines

### When to Create Helper Classes

Create a helper class when you find:
1. **Same code in 3+ locations** - Extract to common helper
2. **String-based type checking** - Use RoslynTypeHelper instead
3. **Repeated syntax patterns** - Use SyntaxHelper
4. **Complex trivia handling** - Use TriviaHelper

### Helper Class Organization

```
src/Linqraft.Core/
├── AnalyzerHelpers/          # Analyzer-specific helpers
│   ├── BaseLinqraftAnalyzer.cs
│   ├── CaptureHelper.cs
│   ├── ExpressionHelper.cs
│   ├── LinqMethodHelper.cs
│   ├── NullCheckHelper.cs
│   ├── SyntaxGenerationHelper.cs
│   ├── SyntaxHelper.cs
│   └── UsingDirectiveHelper.cs
├── RoslynHelpers/            # Roslyn semantic analysis helpers
│   └── RoslynTypeHelper.cs
└── SyntaxHelpers/            # Syntax manipulation helpers
    ├── NullConditionalHelper.cs
    └── TriviaHelper.cs
```

### Naming Conventions

- **Analyzer helpers**: `*Helper.cs` in `AnalyzerHelpers/`
- **Roslyn helpers**: `Roslyn*Helper.cs` in `RoslynHelpers/`
- **Syntax helpers**: `*Helper.cs` in `SyntaxHelpers/`
- **Base classes**: `Base*.cs` in `AnalyzerHelpers/`

### Code Quality Standards

1. **Prefer Semantic Analysis Over String Matching**
   ```csharp
   // Bad
   if (typeName.EndsWith("?")) { }

   // Good
   if (RoslynTypeHelper.IsNullableType(typeSymbol)) { }
   ```

2. **Use TriviaHelper for Formatting**
   ```csharp
   // Bad
   var newNode = node.WithTrailingTrivia(SyntaxFactory.EndOfLine("\n"));

   // Good
   var newNode = node.WithTrailingTrivia(TriviaHelper.EndOfLine(root));
   ```

3. **Centralize Common Patterns**
   - If you write the same logic twice, consider extraction
   - Document the extracted helper with XML comments
   - Add unit tests for complex helpers

---

## Future Refactoring Opportunities

### Phase 4: Additional Optimizations (Low Priority)

1. **SelectExprHelper Extension**
   - `IsSelectExprWithTypeArguments(ExpressionSyntax)`
   - `IsSelectExprWithoutTypeArguments(ExpressionSyntax)`
   - `GetSelectExprMethodNameLocation(ExpressionSyntax)`

2. **Namespace Handling**
   - Consolidate namespace retrieval logic
   - Unify namespace generation patterns

3. **Comment Utilities**
   - Comment stripping helpers
   - Documentation comment generators

---

## Testing Guidelines

### Test Coverage

All helper classes should have corresponding unit tests:
- Positive cases (expected behavior)
- Negative cases (edge cases)
- Null handling
- Performance considerations for frequently-called methods

### Test Organization

```
tests/Linqraft.Tests/
├── AnalyzerHelpers/
│   ├── CaptureHelperTests.cs
│   ├── ExpressionHelperTests.cs
│   └── ...
└── SyntaxHelpers/
    ├── TriviaHelperTests.cs
    └── ...
```

---

## Migration Guide

### Updating Existing Code

When refactoring existing analyzers or code fixes:

1. **Check for String-Based Type Checks**
   - Search for `.Contains("IQueryable")`, `.EndsWith("?")`, etc.
   - Replace with `RoslynTypeHelper` methods

2. **Look for Duplicated Logic**
   - Search for similar code patterns
   - Extract to appropriate helper class

3. **Update Trivia Handling**
   - Replace manual trivia with `TriviaHelper` methods
   - Ensure cross-platform line ending support

4. **Inherit from Base Classes**
   - Analyzers should inherit `BaseLinqraftAnalyzer`
   - Override required abstract members
   - Remove boilerplate code

---

## Performance Considerations

### Helper Method Efficiency

- Helper methods are called frequently during analysis
- Keep helper methods lightweight and focused
- Cache expensive operations when possible
- Use `ISymbolEqualityComparer` for symbol comparisons

### Example: Efficient Type Checking

```csharp
// Efficient - uses symbol comparison
public static bool ImplementsIQueryable(ITypeSymbol typeSymbol, Compilation compilation)
{
    var iqueryableSymbol = compilation.GetTypeByMetadataName("System.Linq.IQueryable`1");
    return typeSymbol is INamedTypeSymbol namedType
        && namedType.AllInterfaces.Any(i =>
            SymbolEqualityComparer.Default.Equals(i.ConstructedFrom, iqueryableSymbol));
}
```

---

## Troubleshooting

### Common Issues

1. **Line Ending Mismatches**
   - Use `TriviaHelper.DetectLineEnding(root)` to detect document line endings
   - Apply detected line ending to generated code
   - Test on both Windows (CRLF) and Unix (LF) systems

2. **Null Reference Exceptions**
   - Always null-check semantic model results
   - Use null-conditional operators: `typeSymbol?.Property`
   - Validate ISymbol before casting

3. **Performance Issues**
   - Profile analyzer performance with large solutions
   - Cache compilation-scoped lookups
   - Avoid repeated tree traversals

---

## Contributing

When adding new functionality:

1. Check if a helper class already exists for your needs
2. If creating a new helper, document it here
3. Add comprehensive XML documentation
4. Include unit tests
5. Update this guide with examples

---

## References

- [Roslyn API Documentation](https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/)
- [C# Analyzer Development](https://github.com/dotnet/roslyn/blob/main/docs/analyzers/Analyzer%20Actions%20Semantics.md)
- [Code Fix Providers](https://github.com/dotnet/roslyn/blob/main/docs/analyzers/FixAllProvider.md)
