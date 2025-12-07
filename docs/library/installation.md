# Installation

This guide covers the installation and setup requirements for Linqraft.

## Prerequisites

Linqraft requires **C# 12.0 or later** because it uses the [interceptor](https://learn.microsoft.com/en-us/dotnet/csharp/whats-new/csharp-12#interceptors) feature.

### .NET 7 or below setup

If you're using .NET 7 or below, you'll need to enable C# 12 features manually:

1. Set the `LangVersion` property to `12.0` or later
2. Use [PolySharp](https://github.com/Sergio0694/PolySharp/) to enable C# latest features

```xml
<Project>
  <PropertyGroup>
    <LangVersion>12.0</LangVersion>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Polysharp" Version="1.*" />
  </ItemGroup>
</Project>
```

### SDK Requirements

Due to the constraints of `Microsoft.CodeAnalysis.CSharp`, one of the following environment is [required](https://andrewlock.net/supporting-multiple-sdk-versions-in-analyzers-and-source-generators/):

* .NET 8.0.400 or later **SDK**
* Visual Studio 2022 version 17.11 or later

> [!NOTE]
> This is only a constraint on the SDK side, so the runtime (target framework) can be older versions.

## Installing Linqraft

Install `Linqraft` from NuGet using the following command:

```bash
dotnet add package Linqraft
```

When you open your `.csproj` file, you should see the package added like below:

```xml
<PackageReference Include="Linqraft" Version="x.y.z">
  <PrivateAssets>all</PrivateAssets>
  <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
</PackageReference>
```

The `PrivateAssets` attribute might look unfamiliar, but it indicates that this is a development-only dependency. This means:
* The library will not be included in the production environment
* It only affects compile-time code generation
* Your deployed application has zero runtime dependencies from Linqraft

## Verifying Installation

To verify that Linqraft is installed correctly:

1. Build your project: `dotnet build`
2. Try using `SelectExpr` in your code
3. The generated code should be available via IntelliSense

If you encounter any issues, try:
* Cleaning and rebuilding: `dotnet clean && dotnet build --no-incremental`
* Restarting your IDE
* Checking that your SDK version meets the requirements

## Next Steps

* [Usage Patterns](./usage-patterns.md) - Learn how to use SelectExpr with different patterns
* [Customization](./customization.md) - Customize the generated code to fit your needs
