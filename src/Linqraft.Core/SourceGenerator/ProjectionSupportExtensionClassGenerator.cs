using System;
using System.Collections.Generic;
using System.Linq;
using Linqraft.Core.Configuration;
using Linqraft.Core.Formatting;
using static Linqraft.Core.Generation.CodeTemplateContents;

namespace Linqraft.SourceGenerator;

internal abstract class ProjectionSupportExtensionClassGenerator
{
    private static readonly ProjectionSupportExtensionClassGenerator[] Generators =
    [
        new SelectExprSupportExtensionClassGenerator(),
        new SelectManyExprSupportExtensionClassGenerator(),
        new GroupByExprSupportExtensionClassGenerator(),
    ];

    protected abstract string GetClassName(LinqraftGeneratorOptionsCore generatorOptions);

    protected abstract string GetMethodName(LinqraftGeneratorOptionsCore generatorOptions);

    protected abstract string GetClassSummary(LinqraftGeneratorOptionsCore generatorOptions);

    protected abstract IEnumerable<SupportMethodSignature> GetMethodSignatures();

    protected virtual void WriteAdditionalMembers(
        IndentedStringBuilder builder,
        LinqraftGeneratorOptionsCore generatorOptions
    ) { }

    public static string CreateAllDeclarations(LinqraftGeneratorOptionsCore generatorOptions)
    {
        return string.Join(
            "\n\n",
            Generators
                .Select(generator => generator.CreateDeclaration(generatorOptions))
                .Concat(CreateProjectionHelperDeclarations(generatorOptions))
                .Concat(CreateLinqraftQueryDeclarations(generatorOptions))
        );
    }

    private static IEnumerable<string> CreateLinqraftQueryDeclarations(
        LinqraftGeneratorOptionsCore generatorOptions
    )
    {
        var extensionBuilder = new IndentedStringBuilder();
        extensionBuilder.AppendLines(
            $$"""
            /// <summary>
            /// Starts the recommended fluent {{generatorOptions.GeneratorDisplayName}} projection style for <see cref="global::System.Linq.IQueryable{T}"/>.
            /// </summary>
            [global::Microsoft.CodeAnalysis.EmbeddedAttribute]
            internal static partial class LinqraftQueryExtensions
            {
                public static global::{{generatorOptions.SupportNamespace}}.LinqraftQuery<TIn> UseLinqraft<TIn>(this global::System.Linq.IQueryable<TIn> query)
                    => new(query);
            }
            """
        );
        yield return extensionBuilder.ToString().TrimEnd();

        var queryBuilder = new IndentedStringBuilder();
        queryBuilder.AppendLines(
            $$"""
            /// <summary>
            /// Wraps an <see cref="global::System.Linq.IQueryable{T}"/> so Linqraft's fluent projection members can be used.
            /// </summary>
            [global::Microsoft.CodeAnalysis.EmbeddedAttribute]
            internal sealed class LinqraftQuery<TIn> : global::System.Linq.IQueryable<TIn>
            {
                private static global::System.InvalidOperationException ThrowInterceptionRequired => new global::System.InvalidOperationException("{{generatorOptions.GeneratorDisplayName}} source generator should replace UseLinqraft().Select invocations before execution.");

                public LinqraftQuery(global::System.Linq.IQueryable<TIn> query)
                {
                    Query = query;
                }

                internal global::System.Linq.IQueryable<TIn> Query { get; }

                public global::System.Type ElementType => Query.ElementType;

                public global::System.Linq.Expressions.Expression Expression => Query.Expression;

                public global::System.Linq.IQueryProvider Provider => Query.Provider;

                public global::System.Collections.Generic.IEnumerator<TIn> GetEnumerator() => Query.GetEnumerator();

                global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

            """
        );
        using (queryBuilder.Indent())
        {
            foreach (
                var capture in new[]
                {
                    new
                    {
                        Parameter = (string?)null,
                        Summary = "Interception stub for fluent Linqraft query projections without captures.",
                        ObsoleteMessage = (string?)null,
                    },
                    new
                    {
                        Parameter = (string?)"object capture",
                        Summary = "Interception stub for fluent Linqraft query projections with anonymous-object captures.",
                        ObsoleteMessage = (string?)
                            "Anonymous-object capture is obsolete. Use the delegate-based capture pattern instead.",
                    },
                    new
                    {
                        Parameter = (string?)"global::System.Func<object> capture",
                        Summary = "Interception stub for fluent Linqraft query projections with NativeAOT-safe delegate captures.",
                        ObsoleteMessage = (string?)null,
                    },
                }
            )
            {
                WriteLinqraftQuerySelectMethod(
                    queryBuilder,
                    summary: capture.Summary,
                    selectorType: "global::System.Func<TIn, TResult>",
                    captureParameter: capture.Parameter,
                    obsoleteMessage: capture.ObsoleteMessage,
                    isLowPriority: false,
                    generatorOptions
                );
                WriteLinqraftQuerySelectMethod(
                    queryBuilder,
                    summary: capture.Summary,
                    selectorType: "global::System.Func<TIn, object>",
                    captureParameter: capture.Parameter,
                    obsoleteMessage: capture.ObsoleteMessage,
                    isLowPriority: true,
                    generatorOptions
                );
                WriteLinqraftQuerySelectMethod(
                    queryBuilder,
                    summary: $"{capture.Summary} Accepts a projection helper as the selector's second parameter.",
                    selectorType: $"global::System.Func<TIn, global::{generatorOptions.SupportNamespace}.IProjectionHelper, TResult>",
                    captureParameter: capture.Parameter,
                    obsoleteMessage: capture.ObsoleteMessage,
                    isLowPriority: false,
                    generatorOptions
                );
                WriteLinqraftQuerySelectMethod(
                    queryBuilder,
                    summary: $"{capture.Summary} Accepts a projection helper as the selector's second parameter.",
                    selectorType: $"global::System.Func<TIn, global::{generatorOptions.SupportNamespace}.IProjectionHelper, object>",
                    captureParameter: capture.Parameter,
                    obsoleteMessage: capture.ObsoleteMessage,
                    isLowPriority: true,
                    generatorOptions
                );
            }
        }

        queryBuilder.AppendLine("}");
        yield return queryBuilder.ToString().TrimEnd();
    }

    private static void WriteLinqraftQuerySelectMethod(
        IndentedStringBuilder builder,
        string summary,
        string selectorType,
        string? captureParameter,
        string? obsoleteMessage,
        bool isLowPriority,
        LinqraftGeneratorOptionsCore generatorOptions
    )
    {
        builder.AppendLines(
            $$"""
            /// <summary>
            /// {{summary}}
            /// </summary>
            """
        );
        if (obsoleteMessage is not null)
        {
            builder.AppendLine($"[global::System.Obsolete(\"{obsoleteMessage}\", false)]");
        }
        if (isLowPriority)
        {
            builder.AppendLine(OverloadResolutionLowPriority);
        }

        var parameters = new[] { $"{selectorType} selector", captureParameter }.Where(parameter =>
            parameter is not null
        );
        builder.AppendLine(
            $"public global::System.Linq.IQueryable<TResult> Select<TResult>({string.Join(", ", parameters)})"
        );
        using (builder.Indent())
        {
            builder.AppendLine("=> throw ThrowInterceptionRequired;");
        }
        builder.AppendLine();
    }

    private static IEnumerable<string> CreateProjectionHelperDeclarations(
        LinqraftGeneratorOptionsCore generatorOptions
    )
    {
        var builder = new IndentedStringBuilder();
        builder.AppendLines(
            """
            /// <summary>
            /// Provides projection helper hooks that generated selectors can use to request special rewrite behavior.
            /// </summary>
            [global::Microsoft.CodeAnalysis.EmbeddedAttribute]
            internal partial interface IProjectionHelper
            {
            """
        );
        using (builder.Indent())
        {
            foreach (var hook in generatorOptions.GetValidatedProjectionHooks())
            {
                builder.AppendLines(
                    $$"""
                    /// <summary>
                    /// Marker used by generated projections to trigger the {{hook.Kind}} rewrite behavior.
                    /// </summary>
                    T {{hook.MethodName}}<T>(T value);
                    """
                );
            }
        }

        builder.AppendLine("}");
        yield return builder.ToString().TrimEnd();
    }

    private string CreateDeclaration(LinqraftGeneratorOptionsCore generatorOptions)
    {
        var builder = new IndentedStringBuilder();
        builder.AppendLines(
            $$"""
            /// <summary>
            /// {{GetClassSummary(generatorOptions)}}
            /// </summary>
            [global::Microsoft.CodeAnalysis.EmbeddedAttribute]
            internal static partial class {{GetClassName(generatorOptions)}}
            {
            """
        );
        using (builder.Indent())
        {
            builder.AppendLine(
                $"private static global::System.InvalidOperationException ThrowInterceptionRequired => new global::System.InvalidOperationException(\"{generatorOptions.GeneratorDisplayName} source generator should replace {GetMethodName(generatorOptions)} invocations before execution.\");"
            );
            builder.AppendLine();
            WriteAdditionalMembers(builder, generatorOptions);

            foreach (var signature in GetMethodSignatures())
            {
                WriteMethod(builder, signature, generatorOptions);
                builder.AppendLine();
            }
        }

        builder.AppendLine("}");
        return builder.ToString().TrimEnd();
    }

    private static void WriteMethod(
        IndentedStringBuilder builder,
        SupportMethodSignature signature,
        LinqraftGeneratorOptionsCore generatorOptions
    )
    {
        builder.AppendLines(
            $$"""
            /// <summary>
            /// {{signature.CreateSummary(generatorOptions)}}
            /// </summary>
            """
        );
        if (signature.CreateObsoleteMessage(generatorOptions) is { } obsoleteMessage)
        {
            builder.AppendLine($"[global::System.Obsolete(\"{obsoleteMessage}\", false)]");
        }
        if (signature.IsLowPriority)
        {
            builder.AppendLine(OverloadResolutionLowPriority);
        }

        builder.AppendLine(signature.CreateSignature(generatorOptions));
        using (builder.Indent())
        {
            builder.AppendLine("=> throw ThrowInterceptionRequired;");
        }
    }

    protected IEnumerable<SupportMethodSignature> CreateQueryOverloads(
        Func<LinqraftGeneratorOptionsCore, string> resultTypeFactory,
        bool includeEnumerable = true,
        bool hasKeySelector = false
    )
    {
        foreach (var receiver in GetReceivers(includeEnumerable))
        {
            foreach (
                var capture in new[]
                {
                    new
                    {
                        Parameter = (string?)null,
                        Summary = $"Interception stub for {receiver.Kind} projections without captures.",
                        ObsoleteMessage = (string?)null,
                    },
                    new
                    {
                        Parameter = (string?)"object capture",
                        Summary = $"Interception stub for {receiver.Kind} projections with anonymous-object captures.",
                        ObsoleteMessage = (string?)
                            "Anonymous-object capture is obsolete. Use the delegate-based capture pattern instead.",
                    },
                    new
                    {
                        Parameter = (string?)"global::System.Func<object> capture",
                        Summary = $"Interception stub for {receiver.Kind} projections with NativeAOT-safe delegate captures.",
                        ObsoleteMessage = (string?)null,
                    },
                }
            )
            {
                yield return new SupportMethodSignature
                {
                    CreateSummary = _ => capture.Summary,
                    CreateSignature = generatorOptions =>
                        CreateMethodSignature(
                            receiver.TypeName,
                            resultTypeFactory(generatorOptions),
                            selectorUsesObjectResult: false,
                            usesProjectionHelperParameter: false,
                            capture.Parameter,
                            hasKeySelector,
                            generatorOptions
                        ),
                    CreateObsoleteMessage = _ => capture.ObsoleteMessage,
                    IsLowPriority = false,
                };

                yield return new SupportMethodSignature
                {
                    CreateSummary = _ => capture.Summary,
                    CreateSignature = generatorOptions =>
                        CreateMethodSignature(
                            receiver.TypeName,
                            resultTypeFactory(generatorOptions),
                            selectorUsesObjectResult: true,
                            usesProjectionHelperParameter: false,
                            capture.Parameter,
                            hasKeySelector,
                            generatorOptions
                        ),
                    CreateObsoleteMessage = _ => capture.ObsoleteMessage,
                    IsLowPriority = true,
                };

                yield return new SupportMethodSignature
                {
                    CreateSummary = _ =>
                        $"{capture.Summary} Accepts a projection helper as the selector's second parameter.",
                    CreateSignature = generatorOptions =>
                        CreateMethodSignature(
                            receiver.TypeName,
                            resultTypeFactory(generatorOptions),
                            selectorUsesObjectResult: false,
                            usesProjectionHelperParameter: true,
                            capture.Parameter,
                            hasKeySelector,
                            generatorOptions
                        ),
                    CreateObsoleteMessage = _ => capture.ObsoleteMessage,
                    IsLowPriority = false,
                };

                yield return new SupportMethodSignature
                {
                    CreateSummary = _ =>
                        $"{capture.Summary} Accepts a projection helper as the selector's second parameter.",
                    CreateSignature = generatorOptions =>
                        CreateMethodSignature(
                            receiver.TypeName,
                            resultTypeFactory(generatorOptions),
                            selectorUsesObjectResult: true,
                            usesProjectionHelperParameter: true,
                            capture.Parameter,
                            hasKeySelector,
                            generatorOptions
                        ),
                    CreateObsoleteMessage = _ => capture.ObsoleteMessage,
                    IsLowPriority = true,
                };
            }
        }
    }

    private static IEnumerable<(string TypeName, string Kind)> GetReceivers(bool includeEnumerable)
    {
        yield return ("global::System.Linq.IQueryable", "queryable sequence");
        if (includeEnumerable)
        {
            yield return ("global::System.Collections.Generic.IEnumerable", "enumerable sequence");
        }
    }

    private string CreateMethodSignature(
        string receiverType,
        string resultType,
        bool selectorUsesObjectResult,
        bool usesProjectionHelperParameter,
        string? captureParameter,
        bool hasKeySelector,
        LinqraftGeneratorOptionsCore generatorOptions
    )
    {
        var selectorReturnType = selectorUsesObjectResult ? "object" : resultType;
        var selectorType = hasKeySelector
            ? usesProjectionHelperParameter
                ? $"global::System.Func<global::System.Linq.IGrouping<TKey, TIn>, global::{generatorOptions.SupportNamespace}.IProjectionHelper, {selectorReturnType}>"
                : $"global::System.Func<global::System.Linq.IGrouping<TKey, TIn>, {selectorReturnType}>"
            : usesProjectionHelperParameter
                ? $"global::System.Func<TIn, global::{generatorOptions.SupportNamespace}.IProjectionHelper, {selectorReturnType}>"
                : $"global::System.Func<TIn, {selectorReturnType}>";
        var parameters = hasKeySelector
            ? new[]
            {
                $"this {receiverType}<TIn> query",
                "global::System.Func<TIn, TKey> keySelector",
                $"{selectorType} selector",
                captureParameter,
            }
            : new[]
            {
                $"this {receiverType}<TIn> query",
                $"{selectorType} selector",
                captureParameter,
            };
        var filteredParameters = parameters.Where(parameter => parameter is not null);
        var typeParameters = hasKeySelector ? "<TIn, TKey, TResult>" : "<TIn, TResult>";
        return $"public static {receiverType}<TResult> {GetMethodName(generatorOptions)}{typeParameters}({string.Join(", ", filteredParameters)}) where TIn : class";
    }

    protected sealed record SupportMethodSignature
    {
        public required Func<LinqraftGeneratorOptionsCore, string> CreateSummary { get; init; }

        public required Func<LinqraftGeneratorOptionsCore, string> CreateSignature { get; init; }

        public Func<LinqraftGeneratorOptionsCore, string?> CreateObsoleteMessage { get; init; } =
            _ => null;

        public required bool IsLowPriority { get; init; }
    }

    private sealed class SelectExprSupportExtensionClassGenerator
        : ProjectionSupportExtensionClassGenerator
    {
        protected override string GetClassName(LinqraftGeneratorOptionsCore generatorOptions) =>
            generatorOptions.SelectExprClassName;

        protected override string GetMethodName(LinqraftGeneratorOptionsCore generatorOptions) =>
            generatorOptions.SelectExprMethodName;

        protected override string GetClassSummary(LinqraftGeneratorOptionsCore generatorOptions) =>
            $"Provides the {generatorOptions.SelectExprMethodName} entry points that {generatorOptions.GeneratorDisplayName} intercepts ahead of time.";

        protected override IEnumerable<SupportMethodSignature> GetMethodSignatures() =>
            CreateQueryOverloads(_ => "TResult");
    }

    private sealed class SelectManyExprSupportExtensionClassGenerator
        : ProjectionSupportExtensionClassGenerator
    {
        protected override string GetClassName(LinqraftGeneratorOptionsCore generatorOptions) =>
            generatorOptions.SelectManyExprClassName;

        protected override string GetMethodName(LinqraftGeneratorOptionsCore generatorOptions) =>
            generatorOptions.SelectManyExprMethodName;

        protected override string GetClassSummary(LinqraftGeneratorOptionsCore generatorOptions) =>
            $"Provides the {generatorOptions.SelectManyExprMethodName} entry points that {generatorOptions.GeneratorDisplayName} intercepts ahead of time.";

        protected override IEnumerable<SupportMethodSignature> GetMethodSignatures() =>
            CreateQueryOverloads(_ => "global::System.Collections.Generic.IEnumerable<TResult>");
    }

    private sealed class GroupByExprSupportExtensionClassGenerator
        : ProjectionSupportExtensionClassGenerator
    {
        protected override string GetClassName(LinqraftGeneratorOptionsCore generatorOptions) =>
            generatorOptions.GroupByExprClassName;

        protected override string GetMethodName(LinqraftGeneratorOptionsCore generatorOptions) =>
            generatorOptions.GroupByExprMethodName;

        protected override string GetClassSummary(LinqraftGeneratorOptionsCore generatorOptions) =>
            $"Provides the {generatorOptions.GroupByExprMethodName} entry points that {generatorOptions.GeneratorDisplayName} intercepts ahead of time.";

        protected override IEnumerable<SupportMethodSignature> GetMethodSignatures() =>
            CreateQueryOverloads(_ => "TResult", hasKeySelector: true);
    }
}
