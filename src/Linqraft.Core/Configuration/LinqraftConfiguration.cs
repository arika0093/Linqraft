using Microsoft.CodeAnalysis.Diagnostics;

namespace Linqraft.Core.Configuration;

internal enum LinqraftPropertyAccessor
{
    Default,
    GetAndSet,
    GetAndInit,
    GetAndInternalSet,
}

internal enum LinqraftCommentOutput
{
    All,
    SummaryOnly,
    None,
}

internal sealed record LinqraftConfiguration
{
    public required LinqraftGeneratorOptionsCore GeneratorOptions { get; init; }

    public string GlobalNamespace { get; init; } = string.Empty;

    public bool RecordGenerate { get; init; }

    public LinqraftPropertyAccessor PropertyAccessor { get; init; } =
        LinqraftPropertyAccessor.Default;

    public bool HasRequired { get; init; } = true;

    public LinqraftCommentOutput CommentOutput { get; init; } = LinqraftCommentOutput.All;

    public bool ArrayNullabilityRemoval { get; init; } = true;

    public bool NestedDtoUseHashNamespace { get; init; } = true;

    public bool UsePrebuildExpression { get; init; }

    public bool GlobalUsing { get; init; } = true;

    public static LinqraftConfiguration Parse(
        AnalyzerConfigOptions options,
        LinqraftGeneratorOptionsCore generatorOptions
    )
    {
        return new LinqraftConfiguration
        {
            GeneratorOptions = generatorOptions,
            GlobalNamespace = GetOption(
                options,
                generatorOptions.GlobalNamespacePropertyName,
                string.Empty
            ),
            RecordGenerate = GetBool(options, generatorOptions.RecordGeneratePropertyName, false),
            PropertyAccessor = GetEnum(
                options,
                generatorOptions.PropertyAccessorPropertyName,
                LinqraftPropertyAccessor.Default
            ),
            HasRequired = GetBool(options, generatorOptions.HasRequiredPropertyName, true),
            CommentOutput = GetEnum(
                options,
                generatorOptions.CommentOutputPropertyName,
                LinqraftCommentOutput.All
            ),
            ArrayNullabilityRemoval = GetBool(
                options,
                generatorOptions.ArrayNullabilityRemovalPropertyName,
                true
            ),
            NestedDtoUseHashNamespace = GetBool(
                options,
                generatorOptions.NestedDtoUseHashNamespacePropertyName,
                true
            ),
            UsePrebuildExpression = GetBool(
                options,
                generatorOptions.UsePrebuildExpressionPropertyName,
                false
            ),
            GlobalUsing = GetBool(options, generatorOptions.GlobalUsingPropertyName, true),
        };
    }

    private static bool GetBool(AnalyzerConfigOptions options, string key, bool defaultValue)
    {
        if (!options.TryGetValue(key, out var value))
        {
            return defaultValue;
        }

        return bool.TryParse(value, out var parsed) ? parsed : defaultValue;
    }

    private static TEnum GetEnum<TEnum>(
        AnalyzerConfigOptions options,
        string key,
        TEnum defaultValue
    )
        where TEnum : struct
    {
        if (!options.TryGetValue(key, out var value))
        {
            return defaultValue;
        }

        return System.Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed)
            ? parsed
            : defaultValue;
    }

    private static string GetOption(AnalyzerConfigOptions options, string key, string defaultValue)
    {
        return options.TryGetValue(key, out var value) ? value : defaultValue;
    }
}
