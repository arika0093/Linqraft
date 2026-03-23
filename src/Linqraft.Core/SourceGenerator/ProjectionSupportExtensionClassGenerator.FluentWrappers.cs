using System;
using System.Collections.Generic;
using System.Linq;
using Linqraft.Core.Configuration;
using Linqraft.Core.Formatting;
using Linqraft.Core.Generation;
using static Linqraft.Core.Generation.CodeTemplateContents;

namespace Linqraft.SourceGenerator;

/// <summary>
/// Generates the support declarations that expose Linqraft projection entry points.
/// </summary>
internal abstract partial class ProjectionSupportExtensionClassGenerator
{
    // Fluent wrapper declarations keep UseLinqraft()-style APIs aligned with the non-fluent interception surface.
    /// <summary>
    /// Creates linqraft query declarations.
    /// </summary>
    private static IEnumerable<string> CreateLinqraftQueryDeclarations(
        LinqraftGeneratorOptionsCore generatorOptions
    )
    {
        // Fluent wrappers expose the same intercepted operations as the classic extension methods
        // while keeping the generated support surface easy to discover from IntelliSense.
        var extensionBuilder = new IndentedStringBuilder();
        extensionBuilder.AppendLines(
            $$"""
            /// <summary>
            /// Starts the fluent {{generatorOptions.GeneratorDisplayName}} projection style for queryable and enumerable sources.
            /// Call <c>UseLinqraft</c> first when you prefer a chained API such as <c>.Select&lt;TResult&gt;(...)</c> over the extension-method entry points.
            /// </summary>
            [global::Microsoft.CodeAnalysis.EmbeddedAttribute]
            internal static partial class LinqraftQueryExtensions
            {
                /// <summary>
                /// Wraps a queryable source so that fluent {{generatorOptions.GeneratorDisplayName}} projection members become available.
                /// </summary>
                /// <typeparam name="TIn">The element type flowing through the source query.</typeparam>
                /// <param name="query">The queryable source to wrap.</param>
                /// <returns>A wrapper that exposes fluent projection members such as <c>Select</c>, <c>SelectMany</c>, and <c>GroupBy</c>.</returns>
                /// <example>
                /// <code>
                /// var projected = query.UseLinqraft().Select&lt;OrderDto&gt;(order =&gt; new OrderDto
                /// {
                ///     Id = order.Id
                /// });
                /// </code>
                /// </example>
                public static global::{{generatorOptions.SupportNamespace}}.LinqraftQuery<TIn> UseLinqraft<TIn>(this global::System.Linq.IQueryable<TIn> query)
                    where TIn : class
                    => new(query);

                /// <summary>
                /// Wraps an enumerable source so that fluent {{generatorOptions.GeneratorDisplayName}} projection members become available.
                /// </summary>
                /// <typeparam name="TIn">The element type flowing through the source sequence.</typeparam>
                /// <param name="query">The enumerable source to wrap.</param>
                /// <returns>A wrapper that exposes fluent projection members such as <c>Select</c>, <c>SelectMany</c>, and <c>GroupBy</c>.</returns>
                /// <example>
                /// <code>
                /// var projected = items.UseLinqraft().Select&lt;OrderDto&gt;(order =&gt; new OrderDto
                /// {
                ///     Id = order.Id
                /// });
                /// </code>
                /// </example>
                public static global::{{generatorOptions.SupportNamespace}}.LinqraftEnumerable<TIn> UseLinqraft<TIn>(this global::System.Collections.Generic.IEnumerable<TIn> query)
                    where TIn : class
                    => new(query);
            }
            """
        );
        yield return extensionBuilder.ToString().TrimEnd();

        yield return CreateFluentWrapperDeclaration(
            "LinqraftQuery",
            ReceiverKind.IQueryable,
            "query",
            generatorOptions
        );
        yield return CreateFluentWrapperDeclaration(
            "LinqraftEnumerable",
            ReceiverKind.IEnumerable,
            "enumerable",
            generatorOptions
        );
    }

    /// <summary>
    /// Creates fluent wrapper declaration.
    /// </summary>
    private static string CreateFluentWrapperDeclaration(
        string className,
        ReceiverKind receiverKind,
        string sourceParameterName,
        LinqraftGeneratorOptionsCore generatorOptions
    )
    {
        var builder = new IndentedStringBuilder();
        builder.AppendLines(
            $$"""
            /// <summary>
            /// Wraps a {{(
                receiverKind == ReceiverKind.IQueryable ? "queryable" : "enumerable"
            )}} source so fluent {{generatorOptions.GeneratorDisplayName}} projection members are the only visible entry points.
            /// </summary>
            [global::Microsoft.CodeAnalysis.EmbeddedAttribute]
            {{EditorBrowsableNeverAttribute}}
            internal sealed class {{className}}<TIn>
                where TIn : class
            {
                public {{className}}({{GetNonFluentReceiverTypeName(
                receiverKind
            )}}<TIn> {{sourceParameterName}})
                {
                    _source = {{sourceParameterName}};
                }

                private {{GetNonFluentReceiverTypeName(receiverKind)}}<TIn> _source { get; }

                {{CodeTemplateContents.EditorBrowsableNeverAttribute}}
                internal {{GetNonFluentReceiverTypeName(receiverKind)}}<TIn> GetSource() => _source;
                
            """
        );
        using (builder.Indent())
        {
            foreach (
                var operation in new[]
                {
                    new
                    {
                        Kind = ProjectionOperationKind.Select,
                        Description = "query projections",
                    },
                    new
                    {
                        Kind = ProjectionOperationKind.SelectMany,
                        Description = "collection-flattening projections",
                    },
                    new
                    {
                        Kind = ProjectionOperationKind.GroupBy,
                        Description = "grouped projections",
                    },
                }
            )
            {
                WriteFluentWrapperMethodFamily(
                    builder,
                    receiverKind,
                    operation.Kind,
                    operation.Description,
                    generatorOptions
                );
            }
        }

        builder.AppendLine("}");
        return builder.ToString().TrimEnd();
    }

    /// <summary>
    /// Writes fluent wrapper method family.
    /// </summary>
    private static void WriteFluentWrapperMethodFamily(
        IndentedStringBuilder builder,
        ReceiverKind receiverKind,
        ProjectionOperationKind operationKind,
        string operationDescription,
        LinqraftGeneratorOptionsCore generatorOptions
    )
    {
        foreach (var capture in GetCapturePatterns(includeAnonymousObjectPattern: false))
        {
            WriteLinqraftQueryMethod(
                builder,
                summary: $"Interception stub for fluent Linqraft {operationDescription} {capture.SummarySuffix}.",
                receiverKind,
                operationKind,
                selectorUsesObjectResult: false,
                usesProjectionHelperParameter: false,
                capture.Kind,
                capture.ObsoleteMessage,
                isLowPriority: false,
                generatorOptions
            );
            WriteLinqraftQueryMethod(
                builder,
                summary: $"Interception stub for fluent Linqraft {operationDescription} {capture.SummarySuffix}.",
                receiverKind,
                operationKind,
                selectorUsesObjectResult: true,
                usesProjectionHelperParameter: false,
                capture.Kind,
                capture.ObsoleteMessage,
                isLowPriority: true,
                generatorOptions
            );
            WriteLinqraftQueryMethod(
                builder,
                summary: $"Interception stub for fluent Linqraft {operationDescription} {capture.SummarySuffix}. Accepts a projection helper as the selector's second parameter.",
                receiverKind,
                operationKind,
                selectorUsesObjectResult: false,
                usesProjectionHelperParameter: true,
                capture.Kind,
                capture.ObsoleteMessage,
                isLowPriority: false,
                generatorOptions
            );
            WriteLinqraftQueryMethod(
                builder,
                summary: $"Interception stub for fluent Linqraft {operationDescription} {capture.SummarySuffix}. Accepts a projection helper as the selector's second parameter.",
                receiverKind,
                operationKind,
                selectorUsesObjectResult: true,
                usesProjectionHelperParameter: true,
                capture.Kind,
                capture.ObsoleteMessage,
                isLowPriority: true,
                generatorOptions
            );
        }
    }

    /// <summary>
    /// Writes linqraft query method.
    /// </summary>
    private static void WriteLinqraftQueryMethod(
        IndentedStringBuilder builder,
        string summary,
        ReceiverKind receiverKind,
        ProjectionOperationKind operationKind,
        bool selectorUsesObjectResult,
        bool usesProjectionHelperParameter,
        CaptureKind captureKind,
        string? obsoleteMessage,
        bool isLowPriority,
        LinqraftGeneratorOptionsCore generatorOptions
    )
    {
        var typeParameters = CreateFluentMethodTypeParameterDocumentation(operationKind);
        var parameterDocumentation = CreateFluentMethodParameterDocumentation(
            operationKind,
            usesProjectionHelperParameter,
            captureKind
        );
        var returnsDocumentation = CreateFluentMethodReturnsDocumentation(
            receiverKind,
            generatorOptions
        );
        var example = CreateFluentMethodExample(
            operationKind,
            usesProjectionHelperParameter,
            captureKind
        );

        AppendXmlDocumentation(
            builder,
            summary,
            typeParameters: typeParameters,
            parameters: parameterDocumentation,
            returns: returnsDocumentation,
            example: example
        );
        if (obsoleteMessage is not null)
        {
            builder.AppendLine($"[global::System.Obsolete(\"{obsoleteMessage}\", false)]");
        }
        if (isLowPriority)
        {
            builder.AppendLine(OverloadResolutionLowPriority);
        }

        builder.AppendLine(
            CreateLinqraftQueryMethodSignature(
                receiverKind,
                operationKind,
                selectorUsesObjectResult,
                usesProjectionHelperParameter,
                captureKind,
                generatorOptions
            )
        );
        using (builder.Indent())
        {
            builder.AppendLine($"=> throw {ThrowHelperClassName}.ForFluentProjection();");
        }
        builder.AppendLine();
    }

}
