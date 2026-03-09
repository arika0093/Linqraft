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
    public string GlobalNamespace { get; init; } = string.Empty;

    public bool RecordGenerate { get; init; }

    public LinqraftPropertyAccessor PropertyAccessor { get; init; } =
        LinqraftPropertyAccessor.Default;

    public bool HasRequired { get; init; } = true;

    public LinqraftCommentOutput CommentOutput { get; init; } = LinqraftCommentOutput.All;

    public bool ArrayNullabilityRemoval { get; init; } = true;

    public bool NestedDtoUseHashNamespace { get; init; } = true;

    public bool UsePrebuildExpression { get; init; }

    public static LinqraftConfiguration Parse(AnalyzerConfigOptions options)
    {
        return new LinqraftConfiguration
        {
            GlobalNamespace = GetOption(
                options,
                "build_property.LinqraftGlobalNamespace",
                string.Empty
            ),
            RecordGenerate = GetBool(options, "build_property.LinqraftRecordGenerate", false),
            PropertyAccessor = GetEnum(
                options,
                "build_property.LinqraftPropertyAccessor",
                LinqraftPropertyAccessor.Default
            ),
            HasRequired = GetBool(options, "build_property.LinqraftHasRequired", true),
            CommentOutput = GetEnum(
                options,
                "build_property.LinqraftCommentOutput",
                LinqraftCommentOutput.All
            ),
            ArrayNullabilityRemoval = GetBool(
                options,
                "build_property.LinqraftArrayNullabilityRemoval",
                true
            ),
            NestedDtoUseHashNamespace = GetBool(
                options,
                "build_property.LinqraftNestedDtoUseHashNamespace",
                true
            ),
            UsePrebuildExpression = GetBool(
                options,
                "build_property.LinqraftUsePrebuildExpression",
                false
            ),
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
