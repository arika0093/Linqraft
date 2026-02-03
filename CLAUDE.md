# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Overview

Linqraft is a C# source generator and analyzer suite that automatically generates Expression trees and DTO classes for IQueryable<T>.Select operations. It enables writing LINQ queries with null-propagation operators (?.) in expression trees and auto-generates DTOs from anonymous types.

The project consists of:
- **Source Generator**: Analyzes `.SelectExpr` calls and generates Select expressions and DTO classes
- **Interceptors**: Replaces SelectExpr calls with generated expressions at compile time
- **Roslyn Analyzers**: Detects patterns and suggests code improvements with automatic fixes

## Build and Test Commands

### Clean build (required to avoid stale generator caches)
```bash
sh scripts/cleanup.sh
dotnet build --no-incremental
```

### Instant feedback build (runtime library only)
```bash
sh scripts/instant-build.sh
```

### Quick test (single framework)
```bash
sh scripts/clean-test.sh
```

### Run specific test
```bash
dotnet test --filter "FullyQualifiedName~YourTestName" --no-build
```

### Inspect generated sources
1. Run `sh scripts/instant-build.sh` to build the project
2. Generated code appears in `tests/Linqraft.Tests/.generated/**/*.g.cs`

## Architecture

### Three SelectExpr patterns

The codebase handles three distinct patterns, each with its own SelectExprInfo implementation:

1. **Anonymous pattern** (`SelectExprInfoAnonymous.cs`): `query.SelectExpr(x => new { x.Id, x.Name })`
   - Returns anonymous type
   - No DTO generation needed

2. **Explicit DTO pattern** (`SelectExprInfoExplicitDto.cs`): `query.SelectExpr<Entity, EntityDto>(x => new { x.Id })`
   - Auto-generates EntityDto class from anonymous type structure
   - Type parameter specifies desired DTO name

3. **Predefined DTO pattern** (`SelectExprInfoPredefinedDto.cs`): `query.SelectExpr(x => new PredefinedDto { x.Id })`
   - Uses existing DTO class
   - No generation, only expression tree creation

### Core components

**Source Generator** (`src/Linqraft.SourceGenerator/`):
- `SelectExprGenerator.cs`: Entry point, orchestrates generation
- `SelectExprGroups.cs`: Groups SelectExpr calls by namespace
- `SelectExprInfo.cs` and subclasses: Parse and hold information for each SelectExpr call

**Analyzer Infrastructure** (`src/Linqraft.Core/`):
- `AnalyzerHelpers/`: Analyzer-specific helpers (BaseLinqraftAnalyzer, CaptureHelper, ExpressionHelper, etc.)
- `RoslynHelpers/`: Roslyn semantic analysis helpers (RoslynTypeHelper for type checking)
- `SyntaxHelpers/`: Syntax manipulation helpers (TriviaHelper for formatting, NullConditionalHelper)

**Analyzers** (`src/Linqraft.Analyzer/`):
- 7 analyzers inheriting from `BaseLinqraftAnalyzer`
- Each analyzer has a corresponding code fix provider
- Examples: `SelectToSelectExprAnonymousAnalyzer`, `LocalVariableCaptureAnalyzer`, `TernaryNullCheckToConditionalAnalyzer`

**Runtime Library** (`src/Linqraft/`):
- `DummyExpression.cs`: Marker method for generator detection (do not edit)

### Key technical details

**Null-propagation conversion**: The generator converts `x.Customer?.Name` to `x.Customer != null ? x.Customer.Name : null` in expression trees (EF Core/IQueryable compatible).

**Interceptors**: Uses C# 12 interceptor feature to replace SelectExpr calls with generated code at compile time, enabling zero-runtime-dependency.

**Helper class organization**:
- **RoslynTypeHelper**: Use for semantic type checking (never use string-based type matching)
- **TriviaHelper**: Preserves formatting and detects cross-platform line endings
- **BaseLinqraftAnalyzer**: Abstract base for all analyzers, reduces boilerplate

Please refer to README.md and docs/library/*.md for more details.

## Development Guidelines

### Analyzer development

1. **Inherit from BaseLinqraftAnalyzer** for all new analyzers
2. **Use semantic analysis over string matching**: Always use `RoslynTypeHelper` instead of string-based type checks
3. **Use TriviaHelper for formatting**: Preserve whitespace/comments and handle cross-platform line endings
4. **Follow naming conventions**:
   - Analyzers: `*Analyzer.cs`
   - Code fixes: `*CodeFixProvider.cs`
   - Helpers: `*Helper.cs` in appropriate subfolder

Example:
```csharp
// Bad: string-based type checking
if (typeName.EndsWith("?")) { }

// Good: semantic analysis
if (RoslynTypeHelper.IsNullableType(typeSymbol)) { }
```

### Source generator development

- **Cache issues**: Run `sh scripts/cleanup.sh` if changes aren't reflected
- **IDE issues**: Restart IDE if generated code isn't visible
- **Always add/update tests** when modifying generators

### Test-driven development

- Write tests first when adding features
- Verify generated code matches expectations
- Ensure all existing tests pass before committing
- Analyzer tests: `tests/Linqraft.Analyzer.Tests/`
- Source generator tests: `tests/Linqraft.Tests/`

## Project Structure

```
src/
├── Linqraft/                    # Runtime library (NuGet package)
├── Linqraft.Core/               # Shared helpers and infrastructure
│   ├── AnalyzerHelpers/         # Analyzer-specific helpers
│   ├── RoslynHelpers/           # Semantic analysis helpers
│   └── SyntaxHelpers/           # Syntax manipulation helpers
├── Linqraft.Analyzer/           # 7 analyzers + code fix providers
└── Linqraft.SourceGenerator/    # Source generator implementation

tests/
├── Linqraft.Tests/              # Source generator tests
└── Linqraft.Analyzer.Tests/     # Analyzer and code fix tests

examples/
├── Linqraft.Sample/             # Basic usage with EF Core
├── Linqraft.MinimumSample/      # Minimal example
└── Linqraft.ApiSample/          # API integration example
```
