# Linqraft.Tests.Configuration

This test project contains tests for the configuration options added in issues #39, #40, #41.

## Purpose

These tests verify the default behavior of the configuration options:
- `LinqraftGlobalNamespace` - Tests default namespace handling
- `LinqraftRecordGenerate` - Tests default class generation
- `LinqraftPropertyAccessor` - Tests default property accessors (get; set;)
- `LinqraftHasRequired` - Tests default required keyword usage

## Testing Custom Configurations

MSBuild properties are applied at the project level during compilation. To test non-default configurations, you would need to create additional test projects with different MSBuild property values set in their `.csproj` files.

### Example: Testing Record Generation

To test `LinqraftRecordGenerate=true`, create a new test project with:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <LinqraftRecordGenerate>true</LinqraftRecordGenerate>
  </PropertyGroup>
</Project>
```

Then verify that generated DTOs are records instead of classes.

## Why a Separate Project?

MSBuild configurations cannot be changed within a single project at test time - they're applied during compilation. Therefore, each set of configuration values requires a separate test project.
