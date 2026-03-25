using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Linqraft.Core.Configuration;

/// <summary>
/// Identifies the built-in rewrite behavior for a projection hook method.
/// </summary>
public enum LinqraftProjectionHookKind
{
    /// <summary>
    /// Rewrites a nullable navigation access chain as an explicit null-guarded left join access.
    /// </summary>
    LeftJoin,

    /// <summary>
    /// Rewrites a nullable navigation access chain without adding null-guards so providers can keep inner-join semantics.
    /// </summary>
    InnerJoin,

    /// <summary>
    /// Inlines the body of a source-defined computed property or method into the generated projection.
    /// </summary>
    Projectable,

    /// <summary>
    /// Creates an explicit DTO projection from a member access instead of exposing the source member type directly.
    /// </summary>
    Projection,

    /// <summary>
    /// Creates a single-value projection context so a nested member can be shaped with a local Select expression.
    /// </summary>
    Project,
}

/// <summary>
/// Describes a generated projection hook extension method and the rewrite behavior it enables.
/// </summary>
/// <param name="MethodName">The hook method name exposed to user code.</param>
/// <param name="Kind">The rewrite behavior applied inside generated projections.</param>
/// <param name="ClassName">The generated extension class name. When omitted, <c>{MethodName}Extensions</c> is used.</param>
public sealed record LinqraftProjectionHookDefinition(
    string MethodName,
    LinqraftProjectionHookKind Kind,
    string? ClassName = null
);

/// <summary>
/// Defines customizable names and build-property keys for the reusable Linqraft generator core.
/// </summary>
public abstract class LinqraftGeneratorOptionsCore
{
    private static readonly ReadOnlyCollection<LinqraftProjectionHookDefinition> DefaultProjectionHooks =
        new List<LinqraftProjectionHookDefinition>
        {
            new("AsLeftJoin", LinqraftProjectionHookKind.LeftJoin),
            new("AsInnerJoin", LinqraftProjectionHookKind.InnerJoin),
            new("AsInline", LinqraftProjectionHookKind.Projectable),
            new("AsProjection", LinqraftProjectionHookKind.Projection),
            new("Project", LinqraftProjectionHookKind.Project),
        }.AsReadOnly();

    /// <summary>
    /// Gets the display name used in generated diagnostics, comments, and runtime exception messages.
    /// Provide a short library name such as <c>Linqraft</c> or your own package name.
    /// </summary>
    public virtual string GeneratorDisplayName => "Linqraft";

    /// <summary>
    /// Gets the single-line header text written into generated files.
    /// The value should describe the generator and may include a version string.
    /// </summary>
    public virtual string GeneratedHeaderComment => "This file is auto-generated.";

    /// <summary>
    /// Gets the namespace that contains generated support declarations such as extension classes and helper types.
    /// This should usually be a valid namespace that is stable for all generated support members.
    /// </summary>
    public virtual string SupportNamespace => "Linqraft";

    /// <summary>
    /// Gets the namespace added to the generated global-using file.
    /// The default uses <see cref="SupportNamespace"/> so consumers can access the generated support API.
    /// </summary>
    public virtual string GlobalUsingNamespace => SupportNamespace;

    /// <summary>
    /// Gets the hint name for the generated support declarations source file.
    /// The value should be a deterministic file name ending in <c>.g.cs</c>.
    /// </summary>
    public virtual string DeclarationSourceHintName => $"{SupportNamespace}.Declarations.g.cs";

    /// <summary>
    /// Gets the hint name for the generated global-using source file.
    /// The value should be a deterministic file name ending in <c>.g.cs</c>.
    /// </summary>
    public virtual string GlobalUsingsSourceHintName => $"{SupportNamespace}.GlobalUsings.g.cs";

    /// <summary>
    /// Gets the generated enum name for mapping visibility control.
    /// Return <see langword="null"/> to omit generation of the enum and to disable support that depends on it.
    /// </summary>
    public virtual string? MappingVisibilityEnumName => "LinqraftMapperVisibility";

    /// <summary>
    /// Gets the generated attribute name used to discover reusable mapping declarations.
    /// Return <see langword="null"/> to omit the attribute and disable mapping-declaration generation.
    /// </summary>
    public virtual string? MappingGenerateAttributeName => "LinqraftMappingAttribute";

    /// <summary>
    /// Gets the generated attribute name used to mark auto-generated nested DTOs.
    /// Return <see langword="null"/> to omit the marker attribute from support source and generated DTO output.
    /// </summary>
    public virtual string? AutoGeneratedDtoAttributeName => "LinqraftAutoGeneratedDtoAttribute";

    /// <summary>
    /// Gets the generated declaration type name for reusable mapping declarations.
    /// Return <see langword="null"/> to omit the declaration type and disable mapping generation.
    /// </summary>
    public virtual string? MappingDeclareClassName => "LinqraftMapper";

    /// <summary>
    /// Gets the generated static helper type name that owns object-generation entry points such as <c>Generate</c>.
    /// Return <see langword="null"/> to omit the helper type and disable object-generation interception.
    /// </summary>
    public virtual string? GeneratorKitClassName => "LinqraftKit";

    /// <summary>
    /// Gets the method name used for object generation on <see cref="GeneratorKitClassName"/>.
    /// This should be a valid C# method identifier.
    /// </summary>
    public virtual string ObjectGenerationMethodName => "Generate";

    /// <summary>
    /// Gets the interception method name for projection over <c>IQueryable</c> and <c>IEnumerable</c>.
    /// This should be a valid C# method identifier.
    /// </summary>
    public virtual string SelectExprMethodName => "SelectExpr";

    /// <summary>
    /// Gets the generated extension class name for <see cref="SelectExprMethodName"/>.
    /// The default appends <c>Extensions</c> to the method name.
    /// </summary>
    public virtual string SelectExprClassName => $"{SelectExprMethodName}Extensions";

    /// <summary>
    /// Gets the interception method name for projection over child collections.
    /// This should be a valid C# method identifier.
    /// </summary>
    public virtual string SelectManyExprMethodName => "SelectManyExpr";

    /// <summary>
    /// Gets the generated extension class name for <see cref="SelectManyExprMethodName"/>.
    /// The default appends <c>Extensions</c> to the method name.
    /// </summary>
    public virtual string SelectManyExprClassName => $"{SelectManyExprMethodName}Extensions";

    /// <summary>
    /// Gets the interception method name for grouped projections.
    /// This should be a valid C# method identifier.
    /// </summary>
    public virtual string GroupByExprMethodName => "GroupByExpr";

    /// <summary>
    /// Gets the generated extension class name for <see cref="GroupByExprMethodName"/>.
    /// The default appends <c>Extensions</c> to the method name.
    /// </summary>
    public virtual string GroupByExprClassName => $"{GroupByExprMethodName}Extensions";

    /// <summary>
    /// Gets the generated no-op extension methods that act as explicit rewrite hooks inside generated projections.
    /// </summary>
    public virtual IReadOnlyList<LinqraftProjectionHookDefinition> ProjectionHooks =>
        DefaultProjectionHooks;

    /// <summary>
    /// Gets the analyzer config key for the generated global namespace option.
    /// Return <see langword="null"/> to skip reading analyzer config and keep the built-in default.
    /// </summary>
    public virtual string? GlobalNamespacePropertyName => "build_property.LinqraftGlobalNamespace";

    /// <summary>
    /// Gets the analyzer config key that enables record generation.
    /// Return <see langword="null"/> to skip reading analyzer config and keep the built-in default.
    /// </summary>
    public virtual string? RecordGeneratePropertyName => "build_property.LinqraftRecordGenerate";

    /// <summary>
    /// Gets the analyzer config key that controls generated property accessors.
    /// Return <see langword="null"/> to skip reading analyzer config and keep the built-in default.
    /// </summary>
    public virtual string? PropertyAccessorPropertyName =>
        "build_property.LinqraftPropertyAccessor";

    /// <summary>
    /// Gets the analyzer config key that controls whether generated members use <c>required</c>.
    /// Return <see langword="null"/> to skip reading analyzer config and keep the built-in default.
    /// </summary>
    public virtual string? HasRequiredPropertyName => "build_property.LinqraftHasRequired";

    /// <summary>
    /// Gets the analyzer config key that controls XML documentation emission.
    /// Return <see langword="null"/> to skip reading analyzer config and keep the built-in default.
    /// </summary>
    public virtual string? CommentOutputPropertyName => "build_property.LinqraftCommentOutput";

    /// <summary>
    /// Gets the analyzer config key that controls nullable-collection fallback rewrites.
    /// Return <see langword="null"/> to skip reading analyzer config and keep the built-in default.
    /// </summary>
    public virtual string? ArrayNullabilityRemovalPropertyName =>
        "build_property.LinqraftArrayNullabilityRemoval";

    /// <summary>
    /// Gets the analyzer config key that controls nested DTO namespace hashing.
    /// Return <see langword="null"/> to skip reading analyzer config and keep the built-in default.
    /// </summary>
    public virtual string? NestedDtoUseHashNamespacePropertyName =>
        "build_property.LinqraftNestedDtoUseHashNamespace";

    /// <summary>
    /// Gets the analyzer config key that controls generated global using emission.
    /// Return <see langword="null"/> to skip reading analyzer config and keep the built-in default.
    /// </summary>
    public virtual string? GlobalUsingPropertyName => "build_property.LinqraftGlobalUsing";

    /// <summary>
    /// Gets the file-name prefix used for mapping-generated source files.
    /// The value should be stable and file-name safe.
    /// </summary>
    public virtual string MappingHintNamePrefix => "Mapping";

    /// <summary>
    /// Gets the file-name prefix used for object-generation source files.
    /// The value should be stable and file-name safe.
    /// </summary>
    public virtual string ObjectGenerationHintNamePrefix => "Generate";

    /// <summary>
    /// Gets the file-name prefix used for standalone DTO source files.
    /// The value should be stable and file-name safe.
    /// </summary>
    public virtual string StandaloneDtoHintNamePrefix => "Model";

    /// <summary>
    /// Gets the namespace prefix used for hash-based nested DTO namespaces.
    /// The value should be a namespace-safe identifier fragment.
    /// </summary>
    public virtual string NestedDtoNamespacePrefix => "LinqraftGenerated";

    /// <summary>
    /// Gets the placeholder token prefix used while composing DTO templates.
    /// The value should be unique enough to avoid collisions with user code.
    /// </summary>
    public virtual string DtoPlaceholderPrefix => "__LinqraftDto";

    internal string? MappingGenerateAttributeMetadataName =>
        MappingGenerateAttributeName is null
            ? null
            : $"{SupportNamespace}.{MappingGenerateAttributeName}";

    internal string? GeneratorKitMetadataName =>
        GeneratorKitClassName is null ? null : $"{SupportNamespace}.{GeneratorKitClassName}";

    /// <summary>
    /// Gets validated projection hooks.
    /// </summary>
    internal IReadOnlyList<LinqraftProjectionHookDefinition> GetValidatedProjectionHooks()
    {
        var hooks = ProjectionHooks;
        var duplicates = hooks
            .GroupBy(hook => hook.MethodName, System.StringComparer.Ordinal)
            .Where(group => group.Skip(1).Any())
            .Select(group => group.Key)
            .OrderBy(name => name, System.StringComparer.Ordinal)
            .ToArray();
        if (duplicates.Length != 0)
        {
            throw new System.InvalidOperationException(
                $"ProjectionHooks contains duplicate method name(s): {string.Join(", ", duplicates)}."
            );
        }

        return hooks;
    }

    /// <summary>
    /// Handles find projection hook.
    /// </summary>
    internal LinqraftProjectionHookDefinition? FindProjectionHook(string methodName)
    {
        return GetValidatedProjectionHooks()
            .FirstOrDefault(hook =>
                string.Equals(hook.MethodName, methodName, System.StringComparison.Ordinal)
            );
    }

    /// <summary>
    /// Gets projection hook class name.
    /// </summary>
    internal static string GetProjectionHookClassName(LinqraftProjectionHookDefinition hook)
    {
        return string.IsNullOrWhiteSpace(hook.ClassName)
            ? $"{hook.MethodName}Extensions"
            : hook.ClassName!;
    }
}
