using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Linqraft.Core.Configuration;
using Linqraft.Core.Formatting;
using Linqraft.Core.Generation;
using Linqraft.Core.Utilities;

namespace Linqraft.SourceGenerator;

/// <summary>
/// Assembles generated source files from finalized projection models.
/// </summary>
internal static partial class SourceWriters
{
    // Capture extraction and namespace helpers keep the generated source layout consistent across all entry points.
    /// <summary>
    /// Writes capture extraction.
    /// </summary>
    private static void WriteCaptureExtraction(
        IndentedStringBuilder builder,
        IEnumerable<CaptureParameterModel> captures,
        CaptureTransportKind transportKind,
        string? transportTypeName,
        CancellationToken cancellationToken = default
    )
    {
        // Generated interceptors restore captures up front so the rewritten selector can use
        // them like ordinary locals, regardless of how the caller transported them.
        if (transportKind == CaptureTransportKind.Delegate)
        {
            WriteDelegateCaptureExtraction(builder, captures, transportTypeName, cancellationToken);
            return;
        }

#pragma warning disable LSG013 // Reflection API usage detected. Source generators should generate static code without using reflection, which defeats the purpose of compile-time code generation.
        builder.AppendLine("var captureType = capture.GetType();", cancellationToken);
        foreach (var capture in captures)
        {
            cancellationToken.ThrowIfCancellationRequested();
            builder.AppendLines(
                $$"""
                var {{capture.LocalName}}Property = captureType.GetProperty("{{EscapeStringLiteral(
                    capture.PropertyName
                )}}", global::System.Reflection.BindingFlags.Instance | global::System.Reflection.BindingFlags.Public);
                if ({{capture.LocalName}}Property is null)
                {
                    throw new global::System.InvalidOperationException("Captured value '{{EscapeStringLiteral(
                    capture.PropertyName
                )}}' was not found.");
                }
                var {{capture.LocalName}}Value = {{capture.LocalName}}Property.GetValue(capture);
                var {{capture.LocalName}} = {{capture.LocalName}}Value is null ? default! : ({{capture.TypeName}}){{capture.LocalName}}Value;
                """,
                cancellationToken
            );
        }
#pragma warning restore LSG013
    }

    /// <summary>
    /// Writes delegate capture extraction.
    /// </summary>
    private static void WriteDelegateCaptureExtraction(
        IndentedStringBuilder builder,
        IEnumerable<CaptureParameterModel> captures,
        string? transportTypeName,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrWhiteSpace(transportTypeName))
        {
            throw new InvalidOperationException("Delegate captures require a transport type.");
        }

        var captureArray = captures.ToArray();
        builder.AppendLine("var captureValueBoxed = capture();", cancellationToken);
        builder.AppendLine(
            $"var captureValue = captureValueBoxed is null ? default! : ({transportTypeName})captureValueBoxed;",
            cancellationToken
        );
        foreach (var capture in captureArray)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var valueExpression = capture.ValueAccessor is null
                ? "captureValue"
                : $"captureValue.{capture.ValueAccessor}";
            builder.AppendLine($"var {capture.LocalName} = {valueExpression};", cancellationToken);
        }
    }

    /// <summary>
    /// Gets accessor.
    /// </summary>
    private static string GetAccessor(LinqraftConfiguration configuration, bool isRecord)
    {
        return configuration.PropertyAccessor switch
        {
            LinqraftPropertyAccessor.GetAndSet => "get; set;",
            LinqraftPropertyAccessor.GetAndInit => "get; init;",
            LinqraftPropertyAccessor.GetAndInternalSet => "get; internal set;",
            _ => isRecord ? "get; init;" : "get; set;",
        };
    }

    /// <summary>
    /// Writes documentation.
    /// </summary>
    private static void WriteDocumentation(
        IndentedStringBuilder builder,
        Linqraft.Core.Documentation.DocumentationInfo? documentation,
        LinqraftCommentOutput outputMode,
        CancellationToken cancellationToken = default
    )
    {
        if (documentation is null || outputMode == LinqraftCommentOutput.None)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(documentation.Summary))
        {
            var summary = documentation.Summary!;
            builder.AppendLine("/// <summary>", cancellationToken);
            foreach (var line in summary.Split('\n'))
            {
                cancellationToken.ThrowIfCancellationRequested();
                builder.AppendLine($"/// {line.Trim()}", cancellationToken);
            }

            builder.AppendLine("/// </summary>", cancellationToken);
        }

        if (
            outputMode == LinqraftCommentOutput.All
            && !string.IsNullOrWhiteSpace(documentation.Remarks)
        )
        {
            var remarks = documentation.Remarks!;
            builder.AppendLine("/// <remarks>", cancellationToken);
            foreach (var line in remarks.Split('\n'))
            {
                cancellationToken.ThrowIfCancellationRequested();
                builder.AppendLine($"/// {line.Trim()}", cancellationToken);
            }

            builder.AppendLine("/// </remarks>", cancellationToken);
        }
    }

    /// <summary>
    /// Writes namespace and containing types start.
    /// </summary>
    private static void WriteNamespaceAndContainingTypesStart(
        IndentedStringBuilder builder,
        string namespaceName,
        Linqraft.Core.Collections.EquatableArray<ContainingTypeInfo> containingTypes,
        CancellationToken cancellationToken = default
    )
    {
        if (!string.IsNullOrWhiteSpace(namespaceName))
        {
            builder.AppendLine($"namespace {namespaceName}", cancellationToken);
            builder.AppendLine("{", cancellationToken);
            builder.IncreaseIndent();
        }

        foreach (var containingType in containingTypes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            builder.AppendLine(
                $"{containingType.AccessibilityKeyword} partial {containingType.DeclarationKeyword} {containingType.Name}",
                cancellationToken
            );
            builder.AppendLine("{", cancellationToken);
            builder.IncreaseIndent();
        }
    }

    /// <summary>
    /// Writes namespace and containing types end.
    /// </summary>
    private static void WriteNamespaceAndContainingTypesEnd(
        IndentedStringBuilder builder,
        string namespaceName,
        Linqraft.Core.Collections.EquatableArray<ContainingTypeInfo> containingTypes,
        CancellationToken cancellationToken = default
    )
    {
        for (var index = containingTypes.Length - 1; index >= 0; index--)
        {
            cancellationToken.ThrowIfCancellationRequested();
            builder.DecreaseIndent();
            builder.AppendLine("}", cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(namespaceName))
        {
            builder.DecreaseIndent();
            builder.AppendLine("}", cancellationToken);
        }
    }

    /// <summary>
    /// Escapes string literal.
    /// </summary>
    private static string EscapeStringLiteral(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    /// <summary>
    /// Appends multiline line.
    /// </summary>
    private static void AppendMultilineLine(
        IndentedStringBuilder builder,
        string value,
        CancellationToken cancellationToken = default
    )
    {
        foreach (var line in SplitLines(value))
        {
            cancellationToken.ThrowIfCancellationRequested();
            builder.AppendLine(line, cancellationToken);
        }
    }

    /// <summary>
    /// Splits lines.
    /// </summary>
    private static string[] SplitLines(string value)
    {
        return value.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
    }

    /// <summary>
    /// Converts to parameter name.
    /// </summary>
    private static string ToParameterName(string propertyName)
    {
        return string.IsNullOrEmpty(propertyName)
            ? "value"
            : char.ToLowerInvariant(propertyName[0]) + propertyName[1..];
    }
}
