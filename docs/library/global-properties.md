# Global Properties

Linqraft supports several MSBuild properties to customize code generation globally.

## Available Properties

```xml
<Project>
  <PropertyGroup>
    <!-- Set namespace for DTOs in global namespace. Empty means use global namespace -->
    <LinqraftGlobalNamespace></LinqraftGlobalNamespace>

    <!-- Generate records instead of classes -->
    <LinqraftRecordGenerate>false</LinqraftRecordGenerate>

    <!-- Set property accessor pattern: Default, GetAndSet, GetAndInit, GetAndInternalSet -->
    <!-- Default is GetAndSet for classes, GetAndInit for records -->
    <LinqraftPropertyAccessor>Default</LinqraftPropertyAccessor>

    <!-- Add 'required' keyword on properties -->
    <LinqraftHasRequired>true</LinqraftHasRequired>

    <!-- Generate XML documentation comments -->
    <!-- All (summary+reference), SummaryOnly (summary only), None (no comments) -->
    <LinqraftCommentOutput>All</LinqraftCommentOutput>

    <!-- Remove nullability from array-type properties -->
    <LinqraftArrayNullabilityRemoval>true</LinqraftArrayNullabilityRemoval>

    <!-- Use hash-named namespace for nested DTOs (e.g., LinqraftGenerated_HASH.ItemsDto) -->
    <!-- When false, uses hash-suffixed class names (e.g., ItemsDto_HASH) -->
    <LinqraftNestedDtoUseHashNamespace>true</LinqraftNestedDtoUseHashNamespace>
  </PropertyGroup>
</Project>
```

## Property Details

### LinqraftGlobalNamespace

Controls the namespace for DTOs when the source entity is in the global namespace.

```xml
<!-- Use global namespace (default) -->
<LinqraftGlobalNamespace></LinqraftGlobalNamespace>

<!-- Use specific namespace -->
<LinqraftGlobalNamespace>MyProject.Dtos</LinqraftGlobalNamespace>
```

### LinqraftRecordGenerate

Generate records instead of classes:

```xml
<LinqraftRecordGenerate>true</LinqraftRecordGenerate>
```

```csharp
// Generated as record
public partial record OrderDto
{
    public required int Id { get; init; }
    public required string CustomerName { get; init; }
    public required decimal TotalAmount { get; init; }
}
```

### LinqraftPropertyAccessor

Control property accessor patterns:

* `Default`: `get; set;` for classes, `get; init;` for records
* `GetAndSet`: `get; set;`
* `GetAndInit`: `get; init;`
* `GetAndInternalSet`: `get; internal set;`

```xml
<LinqraftPropertyAccessor>GetAndInit</LinqraftPropertyAccessor>
```

```csharp
public partial class OrderDto
{
    public required int Id { get; init; }
    public required string CustomerName { get; init; }
}
```

### LinqraftHasRequired

Control the `required` keyword on properties:

```xml
<LinqraftHasRequired>false</LinqraftHasRequired>
```

```csharp
public partial class OrderDto
{
    public int Id { get; set; } // No 'required' keyword
    public string CustomerName { get; set; }
}
```

### LinqraftCommentOutput

See [Auto-Generated Comments](./auto-generated-comments.md) for details.

### LinqraftArrayNullabilityRemoval

See [Array Nullability Removal](./array-nullability.md) for details.

### LinqraftNestedDtoUseHashNamespace

See [Nested DTO Naming](./nested-dto-naming.md) for details.

## Viewing Generated Code

To inspect the generated code:

1. **F12 (Go to Definition)** on the DTO class name
2. **Output to files** by adding these settings:

```xml
<PropertyGroup>
  <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
  <CompilerGeneratedFilesOutputPath>Generated</CompilerGeneratedFilesOutputPath>
</PropertyGroup>
```

Generated files will be written to the `Generated/` folder in your project.
