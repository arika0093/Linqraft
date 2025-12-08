using System.Collections.Generic;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Linqraft.Core;

/// <summary>
/// Configuration options for Linqraft source generator
/// </summary>
public record LinqraftConfiguration
{
    const string LinqraftGlobalNamespaceOptionKey = "build_property.LinqraftGlobalNamespace";
    const string LinqraftRecordGenerateOptionKey = "build_property.LinqraftRecordGenerate";
    const string LinqraftPropertyAccessorOptionKey = "build_property.LinqraftPropertyAccessor";
    const string LinqraftHasRequiredOptionKey = "build_property.LinqraftHasRequired";
    const string LinqraftCommentOutputOptionKey = "build_property.LinqraftCommentOutput";
    const string LinqraftArrayNullabilityRemovalOptionKey =
        "build_property.LinqraftArrayNullabilityRemoval";
    const string LinqraftNestedDtoUseHashNamespaceOptionKey =
        "build_property.LinqraftNestedDtoUseHashNamespace";

    /// <summary>
    /// The namespace where global namespace DTOs should exist.
    /// Default is "" (empty, meaning DTOs will be generated in the global namespace)
    /// </summary>
    public string GlobalNamespace { get; init; } = "";

    /// <summary>
    /// Whether to generate records instead of classes.
    /// Default is false (generate classes)
    /// </summary>
    public bool RecordGenerate { get; init; } = false;

    /// <summary>
    /// The property accessor pattern to use for generated DTOs.
    /// Default is GetAndSet for classes, GetAndInit for records
    /// </summary>
    public PropertyAccessor PropertyAccessor { get; init; } = PropertyAccessor.Default;

    /// <summary>
    /// Whether to use the 'required' keyword on generated DTO properties.
    /// Default is true
    /// </summary>
    public bool HasRequired { get; init; } = true;

    /// <summary>
    /// The comment output mode for generated DTO classes and properties.
    /// Default is All (include all comments and source information)
    /// </summary>
    public CommentOutputMode CommentOutput { get; init; } = CommentOutputMode.All;

    /// <summary>
    /// Whether to remove nullable annotation from collection types with Select/SelectMany
    /// and use empty collection fallback instead of null.
    /// Default is true (remove nullable, use empty collection fallback)
    /// </summary>
    public bool ArrayNullabilityRemoval { get; init; } = true;

    /// <summary>
    /// Whether to generate nested DTOs in a hash-named namespace instead of using hash suffix on the class name.
    /// When true: LinqraftGenerated_(Hash).ClassName format (e.g., LinqraftGenerated_A1470000.ItemsDto)
    /// When false: ClassName_Hash format (e.g., ItemsDto_A1470000)
    /// Default is true (use hash-named namespace)
    /// </summary>
    public bool NestedDtoUseHashNamespace { get; init; } = true;

    /// <summary>
    /// Gets the actual property accessor to use based on configuration
    /// </summary>
    public PropertyAccessor GetEffectivePropertyAccessor()
    {
        if (PropertyAccessor != PropertyAccessor.Default)
        {
            return PropertyAccessor;
        }
        // Default: GetAndInit for records, GetAndSet for classes
        return RecordGenerate ? PropertyAccessor.GetAndInit : PropertyAccessor.GetAndSet;
    }

    public static LinqraftConfiguration GenerateFromGlobalOptions(
        AnalyzerConfigOptionsProvider globalOptions
    )
    {
        globalOptions.GlobalOptions.TryGetValue(
            LinqraftGlobalNamespaceOptionKey,
            out var globalNamespace
        );
        globalOptions.GlobalOptions.TryGetValue(
            LinqraftRecordGenerateOptionKey,
            out var recordGenerateStr
        );
        globalOptions.GlobalOptions.TryGetValue(
            LinqraftPropertyAccessorOptionKey,
            out var propertyAccessorStr
        );
        globalOptions.GlobalOptions.TryGetValue(
            LinqraftHasRequiredOptionKey,
            out var hasRequiredStr
        );
        globalOptions.GlobalOptions.TryGetValue(
            LinqraftCommentOutputOptionKey,
            out var commentOutputStr
        );
        globalOptions.GlobalOptions.TryGetValue(
            LinqraftArrayNullabilityRemovalOptionKey,
            out var arrayNullabilityRemovalStr
        );
        globalOptions.GlobalOptions.TryGetValue(
            LinqraftNestedDtoUseHashNamespaceOptionKey,
            out var NestedDtoUseHashNamespaceStr
        );

        var linqraftOptions = new LinqraftConfiguration();
        if (!string.IsNullOrWhiteSpace(globalNamespace))
        {
            linqraftOptions = linqraftOptions with { GlobalNamespace = globalNamespace! };
        }
        if (bool.TryParse(recordGenerateStr, out var recordGenerate))
        {
            linqraftOptions = linqraftOptions with { RecordGenerate = recordGenerate };
        }
        if (
            System.Enum.TryParse<PropertyAccessor>(
                propertyAccessorStr,
                ignoreCase: true,
                out var propertyAccessorEnum
            )
        )
        {
            linqraftOptions = linqraftOptions with { PropertyAccessor = propertyAccessorEnum };
        }
        if (bool.TryParse(hasRequiredStr, out var hasRequired))
        {
            linqraftOptions = linqraftOptions with { HasRequired = hasRequired };
        }
        if (
            System.Enum.TryParse<CommentOutputMode>(
                commentOutputStr,
                ignoreCase: true,
                out var commentOutputEnum
            )
        )
        {
            linqraftOptions = linqraftOptions with { CommentOutput = commentOutputEnum };
        }
        if (bool.TryParse(arrayNullabilityRemovalStr, out var arrayNullabilityRemoval))
        {
            linqraftOptions = linqraftOptions with
            {
                ArrayNullabilityRemoval = arrayNullabilityRemoval,
            };
        }
        if (bool.TryParse(NestedDtoUseHashNamespaceStr, out var NestedDtoUseHashNamespace))
        {
            linqraftOptions = linqraftOptions with
            {
                NestedDtoUseHashNamespace = NestedDtoUseHashNamespace,
            };
        }
        return linqraftOptions;
    }

    // NOTE: RUNTIME CONFIGURATION SYNCHRONIZATION
    // The following section contains definitions that must be kept in sync with
    // the runtime LinqraftConfiguration in GenerateSourceCodeSnippets.cs
    // Any changes to property names, types, or structure must be reflected in both places.

    /// <summary>
    /// Extracts per-invocation configuration from ConfigurationExpression and merges with global config
    /// NOTE: This method is duplicated in SelectExprInfo.cs and must be kept in sync
    /// </summary>
    public static LinqraftConfiguration MergeWithRuntimeConfig(
        LinqraftConfiguration globalConfig,
        Dictionary<string, object?> configValues)
    {
        var merged = globalConfig;

        if (configValues.TryGetValue("GlobalNamespace", out var globalNamespace) &&
            globalNamespace is string globalNs)
        {
            merged = merged with { GlobalNamespace = globalNs };
        }

        if (configValues.TryGetValue("RecordGenerate", out var recordGenerate) &&
            recordGenerate is bool recordGen)
        {
            merged = merged with { RecordGenerate = recordGen };
        }

        if (configValues.TryGetValue("PropertyAccessor", out var propertyAccessor) &&
            propertyAccessor is PropertyAccessor propAccessor)
        {
            merged = merged with { PropertyAccessor = propAccessor };
        }

        if (configValues.TryGetValue("HasRequired", out var hasRequired) &&
            hasRequired is bool hasReq)
        {
            merged = merged with { HasRequired = hasReq };
        }

        if (configValues.TryGetValue("CommentOutput", out var commentOutput) &&
            commentOutput is CommentOutputMode commentOut)
        {
            merged = merged with { CommentOutput = commentOut };
        }

        if (configValues.TryGetValue("ArrayNullabilityRemoval", out var arrayNullabilityRemoval) &&
            arrayNullabilityRemoval is bool arrayNullRemoval)
        {
            merged = merged with { ArrayNullabilityRemoval = arrayNullRemoval };
        }

        if (configValues.TryGetValue("NestedDtoUseHashNamespace", out var nestedDtoUseHashNamespace) &&
            nestedDtoUseHashNamespace is bool nestedUseHash)
        {
            merged = merged with { NestedDtoUseHashNamespace = nestedUseHash };
        }

        return merged;
    }
}

/// <summary>
/// Property accessor patterns for generated DTOs
/// </summary>
public enum PropertyAccessor
{
    /// <summary>
    /// Use default based on record/class type
    /// </summary>
    Default = 0,

    /// <summary>
    /// get; set; - mutable properties
    /// </summary>
    GetAndSet = 1,

    /// <summary>
    /// get; init; - init-only properties
    /// </summary>
    GetAndInit = 2,

    /// <summary>
    /// get; internal set; - read-only outside assembly
    /// </summary>
    GetAndInternalSet = 3,
}

/// <summary>
/// Comment output modes for generated DTOs
/// </summary>
public enum CommentOutputMode
{
    /// <summary>
    /// Include all comments: XML documentation, source references, and attribute information
    /// </summary>
    All = 0,

    /// <summary>
    /// Include only comments from the source class/property (XML documentation, Comment attribute, Display attribute)
    /// Does not include source references (From:) or attribute information
    /// </summary>
    SummaryOnly = 1,

    /// <summary>
    /// Do not include any comments in generated DTOs
    /// </summary>
    None = 2,
}
