using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Linqraft.Core.Formatting;

namespace Linqraft.SourceGenerator;

/// <summary>
/// Emits projection body.
/// </summary>
internal static class ProjectionBodyEmitter
{
    /// <summary>
    /// Builds projection body.
    /// </summary>
    public static string BuildProjectionBody(
        ProjectionTemplateModel projection,
        ProjectionPattern pattern,
        string resultTypeName,
        bool useFallbackTemplates,
        IReadOnlyDictionary<string, string> typeReplacements,
        CancellationToken cancellationToken = default
    )
    {
        var constructorArguments = projection
            .Members.Where(member => member.IsSuppressed)
            .OrderBy(member => member.Name, System.StringComparer.Ordinal)
            .Select(member =>
                ResolveTemplateValue(
                    member,
                    useFallbackTemplates,
                    typeReplacements,
                    cancellationToken
                )
            )
            .ToList();
        var assignments = projection
            .Members.Where(member => !member.IsSuppressed)
            .Select(member =>
                AppendValueWithContinuation(
                    $"{member.Name} = ",
                    ResolveTemplateValue(
                        member,
                        useFallbackTemplates,
                        typeReplacements,
                        cancellationToken
                    )
                )
            )
            .ToList();

        return pattern == ProjectionPattern.Anonymous
            ? BuildInitializerExpression("new", assignments, cancellationToken)
            : WriteNamedProjection(
                resultTypeName,
                constructorArguments,
                assignments,
                cancellationToken
            );
    }

    /// <summary>
    /// Resolves template value.
    /// </summary>
    public static string ResolveTemplateValue(
        ProjectionMemberTemplateModel member,
        bool useFallbackTemplates,
        IReadOnlyDictionary<string, string> typeReplacements,
        CancellationToken cancellationToken = default
    )
    {
        var template = useFallbackTemplates ? member.FallbackValueTemplate : member.ValueTemplate;
        return ReplaceTokens(template, typeReplacements, cancellationToken);
    }

    /// <summary>
    /// Resolves type template.
    /// </summary>
    public static string ResolveTypeTemplate(
        GeneratedPropertyTemplateModel property,
        bool useFallbackTemplates,
        IReadOnlyDictionary<string, string> typeReplacements,
        CancellationToken cancellationToken = default
    )
    {
        var template = useFallbackTemplates ? property.FallbackTypeTemplate : property.TypeTemplate;
        return ReplaceTokens(template, typeReplacements, cancellationToken);
    }

    /// <summary>
    /// Handles replace tokens.
    /// </summary>
    public static string ReplaceTokens(
        string template,
        IReadOnlyDictionary<string, string> typeReplacements,
        CancellationToken cancellationToken = default
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
            cancellationToken.ThrowIfCancellationRequested();
            result = result.Replace(replacement.Key, replacement.Value);
        }

        return result;
    }

    /// <summary>
    /// Appends value inline.
    /// </summary>
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

    /// <summary>
    /// Writes named projection.
    /// </summary>
    private static string WriteNamedProjection(
        string targetType,
        IReadOnlyList<string> constructorArguments,
        IReadOnlyList<string> assignments,
        CancellationToken cancellationToken = default
    )
    {
        var constructorSuffix =
            constructorArguments.Count == 0 ? "()" : $"({string.Join(", ", constructorArguments)})";
        if (assignments.Count == 0)
        {
            return $"new {targetType}{constructorSuffix}";
        }

        return BuildInitializerExpression(
            $"new {targetType}{constructorSuffix}",
            assignments,
            cancellationToken
        );
    }

    /// <summary>
    /// Builds initializer expression.
    /// </summary>
    private static string BuildInitializerExpression(
        string header,
        IReadOnlyList<string> items,
        CancellationToken cancellationToken = default
    )
    {
        if (!ShouldExpandInitializer(items))
        {
            return items.Count == 0
                ? $"{header} {{ }}"
                : $"{header} {{ {string.Join(", ", items)} }}";
        }

        var builder = new IndentedStringBuilder();
        builder.AppendLine($"{header} {{", cancellationToken);
        using (builder.Indent())
        {
            foreach (var item in items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                AppendMultilineItem(builder, item, ",", cancellationToken);
            }
        }

        builder.Append("}");
        return builder.ToString();
    }

    /// <summary>
    /// Determines whether the items should expand initializer.
    /// </summary>
    private static bool ShouldExpandInitializer(IReadOnlyList<string> items)
    {
        return items.Count > 1 || items.Any(ContainsLineBreak);
    }

    /// <summary>
    /// Appends multiline item.
    /// </summary>
    private static void AppendMultilineItem(
        IndentedStringBuilder builder,
        string value,
        string suffix,
        CancellationToken cancellationToken = default
    )
    {
        var lines = SplitLines(value);
        for (var index = 0; index < lines.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = index == lines.Length - 1 ? lines[index] + suffix : lines[index];
            builder.AppendLine(line, cancellationToken);
        }
    }

    /// <summary>
    /// Appends value with continuation.
    /// </summary>
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

    /// <summary>
    /// Handles indent all lines.
    /// </summary>
    private static string IndentAllLines(string value, int indentLevel = 1)
    {
        var prefix = new string(' ', indentLevel * 4);
        return string.Join("\n", SplitLines(value).Select(line => prefix + line));
    }

    /// <summary>
    /// Determines whether the value contains a line break.
    /// </summary>
    private static bool ContainsLineBreak(string value)
    {
        return value.IndexOf('\n') >= 0 || value.IndexOf('\r') >= 0;
    }

    /// <summary>
    /// Splits lines.
    /// </summary>
    private static string[] SplitLines(string value)
    {
        return value.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
    }
}
