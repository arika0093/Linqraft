# Linqraft Development Guidelines

This document provides GitHub Copilot with repository-specific context and guidelines for working with this C# Source Generator project.

## Project Overview

Linqraft is a C# source generator that automatically generates Expression trees and DTO classes corresponding to `IQueryable<T>.Select` usages. It analyzes `.SelectExpr` calls and generates the corresponding `Select` expressions and DTO classes.

### Key Features
- Automatic DTO generation from Select expressions
- Support for null-propagating expressions (`?.` operator)
- Three patterns supported: anonymous types, explicit DTOs, and predefined DTOs
- EntityFramework Core optimization by selecting only needed fields

## Project Structure

### `src/Linqraft/`
The runtime library distributed as a NuGet package.
- **`DummyExpression.cs`**: Empty extension method that acts as a marker for the Source Generator. **Do not edit** - it performs no runtime work and exists only to be recognized at compile time.

### `src/Linqraft.SourceGenerator/`
The Source Generator implementation:
- **`SelectExprGenerator.cs`**: Generator entry point
- **`SelectExprGroups.cs`**: Grouping SelectExpr information (grouped per namespace)
- **`SelectExprInfo.cs`**: Holds information for each SelectExpr and provides the foundation for code generation
  - `SelectExprInfoAnonymous.cs`: Handles anonymous-type SelectExpr information (pattern 1)
  - `SelectExprInfoExplicitDto.cs`: Handles explicit DTO SelectExpr information (pattern 2)
  - `SelectExprInfoPredefinedDto.cs`: Handles pre-existing DTO SelectExpr information (pattern 3)

### `tests/Linqraft.Tests/`
The test project containing test cases exercising various scenarios and verifying generated output.

### `examples/Linqraft.Sample/`
A sample project demonstrating usage examples.

## Build and Test Commands

### Clean Build (Required for Source Generators)
Always perform a clean build to avoid stale generator caches:

```bash
dotnet clean
dotnet build --no-incremental
dotnet test --no-build
```

### Quick Clean and Test
Use the provided script:
```bash
./scripts/clean-test.sh
```

### Inspecting Generated Code
To view generated sources on disk:
1. Remove the `tests/Linqraft.Tests/.generated` directory if it exists
2. Enable `EmitCompilerGeneratedFiles` in `Linqraft.Tests.csproj`
3. Generated code will be emitted to `tests/Linqraft.Tests/.generated/**/*.g.cs`

## Development Guidelines

### Test-Driven Development
- **Always write tests first** when adding new features
- Verify the generated code in the test project to ensure it matches expectations
- **All existing tests must pass** before committing changes
- Do not remove or modify unrelated tests

### Source Generator-Specific Considerations
- **Cache issues**: If changes to the generator are not reflected, run `dotnet clean`
- **IDE restart**: If generated code is not visible in Visual Studio or Rider, an IDE restart may be required
- **Debugging**: Use `EmitCompilerGeneratedFiles` to inspect emitted sources when necessary

### Code Editing Rules
- **Never edit `DummyExpression.cs`** - it serves only as a marker for the generator
- When modifying the source generator, **always add or update tests**
- Pay attention to the **readability and performance of the generated code**
- Make minimal, surgical changes to achieve the goal
- Do not modify working code unless absolutely necessary

### Technical Background
This project uses:
- **C# Source Generator**: Compile-time code generation feature (C# 9+) that inspects `SelectExpr` calls and emits expression trees and DTO classes
- **Interceptor**: A technique used to intercept method calls and replace the `SelectExpr` call with the generated expression trees at runtime

## Coding Conventions

### C# Style
- Follow standard C# naming conventions
- Use nullable reference types appropriately
- Maintain consistency with existing code style

### Testing
- Test files should be in the `tests/` directory
- Test classes should clearly describe what they're testing
- Use descriptive test method names

## Important Notes

- **Always run `dotnet clean` before building** when working with source generators
- The project targets .NET Standard 2.0 for the library and .NET 6+ for tests
- Generated code should be efficient and readable
- Performance matters: we're generating code that will run in production applications
