This document describes the development guidelines, project structure, and technical background for this repository.

## Overview

This project is a C# source generator and analyzer suite that automatically generates Expression trees and DTO classes corresponding to IQueryable<T>.Select usages. It also provides code analyzers and fixes to improve LINQ query patterns and API design.

The source generator analyzes the contents of `.SelectExpr` calls and generates the corresponding `Select` expressions and DTO classes.

Here is an example:

```csharp
public class SampleClass
{
    public void GetSample(List<BaseClass> data)
    {
        var query = data.AsQueryable();
        // pattern 1: use anonymous type to specify selection
        // return type is anonymous type
        query.SelectExpr(x => new
        {
            x.Id,
            // you can use the null-conditional operator
            ChildDescription = x.Child?.Description,
        });

        // pattern 2: use an explicit DTO class
        // return type is SampleDto (auto-generated)
        query.SelectExpr<SampleDto>(x => new
        {
            x.Id,
            // you can select child properties
            ChildNames = x.Children.Select(c => c.Name).ToList(),
        });

        // pattern 3: use an already defined DTO class
        query.SelectExpr(x => new PredefinedDto
        {
            x.Name,
            x.Value,
        });
    }
}

public class PredefinedDto
{
    public string Name { get; set; }
    public int Value { get; set; }
}
```

## Project structure

The repository is organized as follows (relevant folders under `src/`):

### `src/Linqraft/`
The runtime library distributed as a NuGet package. Notable file:
- `DummyExpression.cs`: an empty extension method that acts as a marker for the Source Generator to detect `SelectExpr` usages. It performs no runtime work and exists only to be recognized at compile time.

### `src/Linqraft.Core/`
Common infrastructure and helper classes shared between analyzers and source generators. This project contains:

#### AnalyzerHelpers/
Analyzer-specific helper classes and base infrastructure:
- `BaseLinqraftAnalyzer.cs`: Abstract base class for all analyzers, eliminating boilerplate code
- `CaptureHelper.cs`: Helper for detecting captured variables in lambda expressions
- `ExpressionHelper.cs`: Helper for extracting property names and member access chains from expressions
- `LinqMethodHelper.cs`: Helper for detecting LINQ method calls (Select, SelectMany, ToList)
- `NullCheckHelper.cs`: Helper for null checking patterns
- `SyntaxGenerationHelper.cs`: Helper for generating typed SelectExpr calls
- `SyntaxHelper.cs`: Helper for syntax node analysis and manipulation
- `UsingDirectiveHelper.cs`: Helper for managing using directives

#### RoslynHelpers/
Roslyn semantic analysis helpers:
- `RoslynTypeHelper.cs`: Type checking using Roslyn semantic analysis instead of string matching. Provides methods for nullable type checking, IQueryable/IEnumerable detection, anonymous type detection, and more.

#### SyntaxHelpers/
Syntax manipulation and formatting helpers:
- `NullConditionalHelper.cs`: Helper for null-conditional operator (?.) handling and null check pattern detection
- `TriviaHelper.cs`: Helper for preserving whitespace, comments, and formatting. Includes cross-platform line ending detection (CRLF vs LF).

### `src/Linqraft.Analyzer/`
Roslyn analyzers and code fix providers that detect code patterns and suggest improvements. This project contains:

#### Analyzers (7 total)
All analyzers inherit from `BaseLinqraftAnalyzer`:
- `AnonymousTypeToDtoAnalyzer.cs`: Detects anonymous types that can be converted to DTOs
- `ApiControllerProducesResponseTypeAnalyzer.cs`: Detects API controllers missing ProducesResponseType attributes
- `LocalVariableCaptureAnalyzer.cs`: Detects local variables that should be captured in SelectExpr
- `SelectExprToTypedAnalyzer.cs`: Detects SelectExpr calls that can use explicit type parameters
- `SelectToSelectExprAnonymousAnalyzer.cs`: Detects Select calls with anonymous types that should use SelectExpr
- `SelectToSelectExprNamedAnalyzer.cs`: Detects Select calls with named types that should use SelectExpr
- `TernaryNullCheckToConditionalAnalyzer.cs`: Detects ternary null checks that can be simplified to null-conditional operators

#### Code Fix Providers (7 total)
Each analyzer has a corresponding code fix provider:
- `AnonymousTypeToDtoCodeFixProvider.cs`
- `ApiControllerProducesResponseTypeCodeFixProvider.cs`
- `LocalVariableCaptureCodeFixProvider.cs`
- `SelectExprToTypedCodeFixProvider.cs`
- `SelectToSelectExprAnonymousCodeFixProvider.cs`
- `SelectToSelectExprNamedCodeFixProvider.cs`
- `TernaryNullCheckToConditionalCodeFixProvider.cs`

#### Utilities
- `TernaryNullCheckSimplifier.cs`: Centralized logic for simplifying ternary null checks to null-conditional operators

### `src/Linqraft.SourceGenerator/`
The Source Generator implementation that performs the actual code generation. Important files include:
- `SelectExprGenerator.cs`: the generator entry point
- `SelectExprGroups.cs`: grouping SelectExpr information (grouped per namespace)
- `SelectExprInfo.cs`: holds information for each SelectExpr and provides the foundation for code generation
  - `SelectExprInfoAnonymous.cs`: handles anonymous-type SelectExpr information (pattern 1)
  - `SelectExprInfoExplicitDto.cs`: handles explicit DTO SelectExpr information (pattern 2)
  - `SelectExprInfoPredefinedDto.cs`: handles pre-existing DTO SelectExpr information (pattern 3)

### `tests/Linqraft.Tests/`
The test project for source generators. Contains test cases exercising various scenarios and verifies generated output.

### `tests/Linqraft.Analyzer.Tests/`
The test project for analyzers and code fix providers. Contains comprehensive tests for all 7 analyzers and their corresponding code fixes.

### `examples/Linqraft.Sample/`
A sample project demonstrating usage examples.

### `docs/developments/`
Development documentation and guides:
- `refactoring-guide.md`: Comprehensive guide documenting the refactored codebase architecture, helper class organization, code quality standards, and migration guidelines. **Read this before making significant changes to analyzers or helper classes.**

## Technical background

This project consists of three main components:

1. **C# Source Generator**: A compile-time code generation feature (available since C# 9). The generator inspects `SelectExpr` calls and emits expression trees and DTO classes.

2. **Interceptor**: A technique used to intercept method calls and replace the `SelectExpr` call with the generated expression trees at runtime.

3. **Roslyn Analyzers**: Compile-time code analyzers that detect patterns and suggest improvements. Analyzers use the Roslyn API for semantic analysis and syntax tree manipulation.

## Build and test

Always perform a clean build to avoid stale generator caches:

```bash
dotnet clean
dotnet build --no-incremental
dotnet test --no-build
```

If you want to inspect the generated sources on disk, follow these steps:

1. Remove the `(test-project)/.generated` directory if it already exists.
2. Enable `EmitCompilerGeneratedFiles` in `Linqraft.Tests.csproj`.
3. The generated code will be emitted to `(test-project)/.generated/**/*.g.cs`.

You can use the `./scripts/clean-test.sh` script as a shortcut.

## Development guidelines

### Test-driven development recommended

- When adding new features, write tests first.
- Verify the generated code in the test project to ensure it matches expectations.
- Ensure all existing tests pass before committing changes.
- For analyzers, add test cases to `tests/Linqraft.Analyzer.Tests/`.
- For source generators, add test cases to `tests/Linqraft.Tests/`.

### Source generator-specific considerations

- Cache issues: if changes to the generator are not reflected, run `dotnet clean`.
- IDE restart: if generated code is not visible in Visual Studio or Rider, an IDE restart may be required.
- Debugging: debugging source generators can be more involved than regular code. Use `EmitCompilerGeneratedFiles` to inspect emitted sources when necessary.

### Analyzer development guidelines

When developing or modifying analyzers:

1. **Inherit from BaseLinqraftAnalyzer**
   - All new analyzers should inherit from `BaseLinqraftAnalyzer`
   - Override the required abstract properties: `DiagnosticId`, `Title`, `MessageFormat`, `Description`, `Severity`, `Rule`
   - Define a public const `AnalyzerId` for use by code fix providers

2. **Use Helper Classes**
   - **Always prefer semantic analysis over string matching**: Use `RoslynTypeHelper` instead of string-based type checking
   - **Use TriviaHelper for formatting**: Preserve whitespace and comments, and detect line endings for cross-platform compatibility
   - **Centralize common patterns**: If you write the same logic twice, extract it to a helper class
   - See `docs/developments/refactoring-guide.md` for detailed guidelines on when and how to create helper classes

3. **Code Quality Standards**
   ```csharp
   // Bad: string-based type checking
   if (typeName.EndsWith("?")) { }

   // Good: semantic analysis
   if (RoslynTypeHelper.IsNullableType(typeSymbol)) { }
   ```

   ```csharp
   // Bad: hardcoded line endings
   var newNode = node.WithTrailingTrivia(SyntaxFactory.EndOfLine("\n"));

   // Good: cross-platform line ending detection
   var newNode = node.WithTrailingTrivia(TriviaHelper.EndOfLine(root));
   ```

4. **Follow Naming Conventions**
   - Analyzers: `*Analyzer.cs` inheriting from `BaseLinqraftAnalyzer`
   - Code Fix Providers: `*CodeFixProvider.cs`
   - Analyzer helpers: `*Helper.cs` in `Linqraft.Core/AnalyzerHelpers/`
   - Roslyn helpers: `Roslyn*Helper.cs` in `Linqraft.Core/RoslynHelpers/`
   - Syntax helpers: `*Helper.cs` in `Linqraft.Core/SyntaxHelpers/`

5. **Performance Considerations**
   - Helper methods are called frequently during analysis
   - Keep helper methods lightweight and focused
   - Cache expensive operations when possible
   - Use `ISymbolEqualityComparer` for symbol comparisons

### Code editing guidelines

- Do not edit `DummyExpression.cs` (it serves only as a marker).
- When modifying the source generator, always add or update tests.
- When modifying analyzers or code fix providers, always add or update tests.
- Pay attention to the readability and performance of the generated code.
- Document complex helper methods with XML comments.
- Consult `docs/developments/refactoring-guide.md` for architecture guidelines and best practices.

### Helper class organization

Helper classes are organized by purpose:

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

**When to create a new helper class:**
1. Same code appears in 3+ locations
2. You're using string-based type checking (use RoslynTypeHelper instead)
3. Repeated syntax patterns (use SyntaxHelper)
4. Complex trivia handling (use TriviaHelper)

See `docs/developments/refactoring-guide.md` for detailed examples and migration guidelines.
