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

    /// <summary>
    /// The namespace where global namespace DTOs should exist.
    /// Default is "Linqraft"
    /// </summary>
    public string GlobalNamespace { get; init; } = "Linqraft";

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

        var linqraftOptions = new LinqraftConfiguration();
        if (!string.IsNullOrWhiteSpace(globalNamespace))
        {
            linqraftOptions = linqraftOptions with { GlobalNamespace = globalNamespace! };
        }
        if (!bool.TryParse(recordGenerateStr, out var recordGenerate))
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
        if (!bool.TryParse(hasRequiredStr, out var hasRequired))
        {
            linqraftOptions = linqraftOptions with { HasRequired = hasRequired };
        }
        return linqraftOptions;
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
