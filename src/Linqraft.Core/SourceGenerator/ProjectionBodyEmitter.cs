using System.Collections.Generic;
using System.Linq;
using Linqraft.Core.Formatting;

namespace Linqraft.SourceGenerator;

internal static class ProjectionBodyEmitter
{
    public static string BuildProjectionBody(
        ProjectionTemplateModel projection,
        ProjectionPattern pattern,
        string resultTypeName,
        bool useFallbackTemplates,
        IReadOnlyDictionary<string, string> typeReplacements
    )
    {
        var constructorArguments = projection
            .Members.Where(member => member.IsSuppressed)
            .OrderBy(member => member.Name, System.StringComparer.Ordinal)
            .Select(member => ResolveTemplateValue(member, useFallbackTemplates, typeReplacements))
            .ToList();
        var assignments = projection
            .Members.Where(member => !member.IsSuppressed)
            .Select(member =>
                AppendValueWithContinuation(
                    $"{member.Name} = ",
                    ResolveTemplateValue(member, useFallbackTemplates, typeReplacements)
                )
            )
            .ToList();

        return pattern == ProjectionPattern.Anonymous
            ? BuildInitializerExpression("new", assignments)
            : WriteNamedProjection(resultTypeName, constructorArguments, assignments);
    }

    public static string ResolveTemplateValue(
        ProjectionMemberTemplateModel member,
        bool useFallbackTemplates,
        IReadOnlyDictionary<string, string> typeReplacements
    )
    {
        var template = useFallbackTemplates ? member.FallbackValueTemplate : member.ValueTemplate;
        return ReplaceTokens(template, typeReplacements);
    }

    public static string ResolveTypeTemplate(
        GeneratedPropertyTemplateModel property,
        bool useFallbackTemplates,
        IReadOnlyDictionary<string, string> typeReplacements
    )
    {
        var template = useFallbackTemplates ? property.FallbackTypeTemplate : property.TypeTemplate;
        return ReplaceTokens(template, typeReplacements);
    }

    public static string ReplaceTokens(
        string template,
        IReadOnlyDictionary<string, string> typeReplacements
    )
    {
        if (typeReplacements.Count == 0)
        {
            return template;
        }

        var result = template;
        foreach (
            var replacement in typeReplacements.OrderByDescending(
                pair => pair.Key.Length,
                System.Collections.Generic.Comparer<int>.Default
            )
        )
        {
            result = result.Replace(replacement.Key, replacement.Value);
        }

        return result;
    }

    public static string AppendValueInline(string prefix, string value)
    {
        var lines = SplitLines(value);
        if (lines.Length == 0)
        {
            return prefix;
        }

        lines[0] = prefix + lines[0];
        return string.Join("\n", lines);
    }

    private static string WriteNamedProjection(
        string targetType,
        IReadOnlyList<string> constructorArguments,
        IReadOnlyList<string> assignments
    )
    {
        var constructorSuffix =
            constructorArguments.Count == 0 ? "()" : $"({string.Join(", ", constructorArguments)})";
        if (assignments.Count == 0)
        {
            return $"new {targetType}{constructorSuffix}";
        }

        return BuildInitializerExpression($"new {targetType}{constructorSuffix}", assignments);
    }

    private static string BuildInitializerExpression(string header, IReadOnlyList<string> items)
    {
        if (!ShouldExpandInitializer(items))
        {
            return items.Count == 0
                ? $"{header} {{ }}"
                : $"{header} {{ {string.Join(", ", items)} }}";
        }

        var builder = new IndentedStringBuilder();
        builder.AppendLine($"{header} {{");
        using (builder.Indent())
        {
            foreach (var item in items)
            {
                AppendMultilineItem(builder, item, ",");
            }
        }

        builder.Append("}");
        return builder.ToString();
    }

    private static bool ShouldExpandInitializer(IReadOnlyList<string> items)
    {
        return items.Count > 1 || items.Any(ContainsLineBreak);
    }

    private static void AppendMultilineItem(
        IndentedStringBuilder builder,
        string value,
        string suffix
    )
    {
        var lines = SplitLines(value);
        for (var index = 0; index < lines.Length; index++)
        {
            var line = index == lines.Length - 1 ? lines[index] + suffix : lines[index];
            builder.AppendLine(line);
        }
    }

    private static string AppendValueWithContinuation(string prefix, string value)
    {
        var lines = SplitLines(value);
        if (lines.Length == 0)
        {
            return prefix;
        }

        if (lines.Length == 1)
        {
            return prefix + lines[0];
        }

        var formattedLines = new List<string> { prefix + lines[0] };
        formattedLines.AddRange(lines.Skip(1).Select(IndentAllLines));
        return string.Join("\n", formattedLines);
    }

    private static string IndentAllLines(string value, int indentLevel = 1)
    {
        var prefix = new string(' ', indentLevel * 4);
        return string.Join("\n", SplitLines(value).Select(line => prefix + line));
    }

    private static bool ContainsLineBreak(string value)
    {
        return value.IndexOf('\n') >= 0 || value.IndexOf('\r') >= 0;
    }

    private static string[] SplitLines(string value)
    {
        return value.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
    }
}
