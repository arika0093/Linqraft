This document describes the development guidelines, project structure, and technical background for this repository.

## Overview

This project is a C# source generator that automatically generates Expression trees and DTO classes corresponding to IQueryable<T>.Select usages.
It analyzes the contents of `.SelectExpr` calls and generates the corresponding `Select` expressions and DTO classes.
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

### `src/Linqraft.SourceGenerator/`
The Source Generator implementation that performs the actual code generation. Important files include:
- `SelectExprGenerator.cs`: the generator entry point
- `SelectExprGroups.cs`: grouping SelectExpr information (grouped per namespace)
- `SelectExprInfo.cs`: holds information for each SelectExpr and provides the foundation for code generation
  - `SelectExprInfoAnonymous.cs`: handles anonymous-type SelectExpr information (pattern 1)
  - `SelectExprInfoExplicitDto.cs`: handles explicit DTO SelectExpr information (pattern 2)
  - `SelectExprInfoPredefinedDto.cs`: handles pre-existing DTO SelectExpr information (pattern 3)

### `tests/Linqraft.Tests/`
The test project. It contains test cases exercising various scenarios and verifies generated output.

### `examples/Linqraft.Sample/`
A sample project demonstrating usage examples.

## Technical background

This project consists of a C# Source Generator and an interceptor mechanism:

- C# Source Generator: a compile-time code generation feature (available since C# 9). The generator inspects `SelectExpr` calls and emits expression trees and DTO classes.
- Interceptor: a technique used to intercept method calls and replace the `SelectExpr` call with the generated expression trees at runtime.

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

### Source generator-specific considerations

- Cache issues: if changes to the generator are not reflected, run `dotnet clean`.
- IDE restart: if generated code is not visible in Visual Studio or Rider, an IDE restart may be required.
- Debugging: debugging source generators can be more involved than regular code. Use `EmitCompilerGeneratedFiles` to inspect emitted sources when necessary.

### Code editing guidelines

- Do not edit `DummyExpression.cs` (it serves only as a marker).
- When modifying the source generator, always add or update tests.
- Pay attention to the readability and performance of the generated code.
