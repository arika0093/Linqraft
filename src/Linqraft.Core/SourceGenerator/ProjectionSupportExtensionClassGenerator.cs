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
            /// Starts the recommended fluent {{generatorOptions.GeneratorDisplayName}} projection style
            /// for queryable sources and enumerable sources that are normalized via
            /// <see cref="global::System.Linq.Queryable.AsQueryable{TElement}(global::System.Collections.Generic.IEnumerable{TElement})"/>.
            /// </summary>
            [global::Microsoft.CodeAnalysis.EmbeddedAttribute]
            internal static partial class LinqraftQueryExtensions
            {
                public static global::{{generatorOptions.SupportNamespace}}.LinqraftQuery<TIn> UseLinqraft<TIn>(this global::System.Linq.IQueryable<TIn> query)
                    => new(query);

                [global::System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Fluent IEnumerable support normalizes the source via AsQueryable inside Linqraft query aids.")]
                [global::System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Fluent IEnumerable support normalizes the source via AsQueryable inside Linqraft query aids.")]
                public static global::{{generatorOptions.SupportNamespace}}.LinqraftQuery<TIn> UseLinqraft<TIn>(this global::System.Collections.Generic.IEnumerable<TIn> query)
                    => new(query);
            }
            """
        );
        yield return extensionBuilder.ToString().TrimEnd();

        var queryBuilder = new IndentedStringBuilder();
        queryBuilder.AppendLines(
            $$"""
            /// <summary>
            /// Wraps a query source so only fluent {{generatorOptions.GeneratorDisplayName}} projection members are available.
            /// </summary>
            [global::Microsoft.CodeAnalysis.EmbeddedAttribute]
            internal sealed class LinqraftQuery<TIn>
            {
                private static global::System.InvalidOperationException ThrowInterceptionRequired => new global::System.InvalidOperationException("{{generatorOptions.GeneratorDisplayName}} source generator should replace UseLinqraft projection invocations before execution.");

                public LinqraftQuery(global::System.Linq.IQueryable<TIn> query)
                {
                    Query = query;
                }

                [global::System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Fluent IEnumerable support normalizes the source via AsQueryable inside Linqraft query aids.")]
                [global::System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Fluent IEnumerable support normalizes the source via AsQueryable inside Linqraft query aids.")]
                public LinqraftQuery(global::System.Collections.Generic.IEnumerable<TIn> query)
                    : this(query.AsQueryable()) { }

                internal global::System.Linq.IQueryable<TIn> Query { get; }
            """
        );
        using (queryBuilder.Indent())
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
                WriteLinqraftQueryMethodFamily(
                    queryBuilder,
                    operation.Kind,
                    operation.Description,
                    generatorOptions
                );
            }
        }

        queryBuilder.AppendLine("}");
        yield return queryBuilder.ToString().TrimEnd();
    }

    private static void WriteLinqraftQueryMethodFamily(
        IndentedStringBuilder builder,
        ProjectionOperationKind operationKind,
        string operationDescription,
        LinqraftGeneratorOptionsCore generatorOptions
    )
    {
        foreach (var capture in GetCapturePatterns())
        {
            WriteLinqraftQueryMethod(
                builder,
                summary: $"Interception stub for fluent Linqraft {operationDescription} {capture.SummarySuffix}.",
                operationKind,
                selectorUsesObjectResult: false,
                usesProjectionHelperParameter: false,
                capture.Parameter,
                capture.ObsoleteMessage,
                isLowPriority: false,
                generatorOptions
            );
            WriteLinqraftQueryMethod(
                builder,
                summary: $"Interception stub for fluent Linqraft {operationDescription} {capture.SummarySuffix}.",
                operationKind,
                selectorUsesObjectResult: true,
                usesProjectionHelperParameter: false,
                capture.Parameter,
                capture.ObsoleteMessage,
                isLowPriority: true,
                generatorOptions
            );
            WriteLinqraftQueryMethod(
                builder,
                summary: $"Interception stub for fluent Linqraft {operationDescription} {capture.SummarySuffix}. Accepts a projection helper as the selector's second parameter.",
                operationKind,
                selectorUsesObjectResult: false,
                usesProjectionHelperParameter: true,
                capture.Parameter,
                capture.ObsoleteMessage,
                isLowPriority: false,
                generatorOptions
            );
            WriteLinqraftQueryMethod(
                builder,
                summary: $"Interception stub for fluent Linqraft {operationDescription} {capture.SummarySuffix}. Accepts a projection helper as the selector's second parameter.",
                operationKind,
                selectorUsesObjectResult: true,
                usesProjectionHelperParameter: true,
                capture.Parameter,
                capture.ObsoleteMessage,
                isLowPriority: true,
                generatorOptions
            );
        }
    }

    private static void WriteLinqraftQueryMethod(
        IndentedStringBuilder builder,
        string summary,
        ProjectionOperationKind operationKind,
        bool selectorUsesObjectResult,
        bool usesProjectionHelperParameter,
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

        builder.AppendLine(
            CreateLinqraftQueryMethodSignature(
                operationKind,
                selectorUsesObjectResult,
                usesProjectionHelperParameter,
                captureParameter,
                generatorOptions
            )
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
            foreach (var capture in GetCapturePatterns())
            {
                yield return new SupportMethodSignature
                {
                    CreateSummary = _ =>
                        $"Interception stub for {receiver.Kind} projections {capture.SummarySuffix}.",
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
                    CreateSummary = _ =>
                        $"Interception stub for {receiver.Kind} projections {capture.SummarySuffix}.",
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
                        $"Interception stub for {receiver.Kind} projections {capture.SummarySuffix}. Accepts a projection helper as the selector's second parameter.",
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
                        $"Interception stub for {receiver.Kind} projections {capture.SummarySuffix}. Accepts a projection helper as the selector's second parameter.",
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

    private static IEnumerable<(string? Parameter, string SummarySuffix, string? ObsoleteMessage)> GetCapturePatterns()
    {
        yield return (null, "without captures", null);
        yield return (
            "object capture",
            "with anonymous-object captures",
            "Anonymous-object capture is obsolete. Use the delegate-based capture pattern instead."
        );
        yield return (
            "global::System.Func<object> capture",
            "with NativeAOT-safe delegate captures",
            null
        );
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

    private static string CreateLinqraftQueryMethodSignature(
        ProjectionOperationKind operationKind,
        bool selectorUsesObjectResult,
        bool usesProjectionHelperParameter,
        string? captureParameter,
        LinqraftGeneratorOptionsCore generatorOptions
    )
    {
        var selectorResultType = operationKind switch
        {
            ProjectionOperationKind.SelectMany when !selectorUsesObjectResult =>
                "global::System.Collections.Generic.IEnumerable<TResult>",
            _ => selectorUsesObjectResult ? "object" : "TResult",
        };
        var methodName = operationKind switch
        {
            ProjectionOperationKind.Select => "Select",
            ProjectionOperationKind.SelectMany => "SelectMany",
            ProjectionOperationKind.GroupBy => "GroupBy",
            _ => throw new InvalidOperationException(
                $"Unsupported projection operation '{operationKind}'."
            ),
        };
        var helperType = $"global::{generatorOptions.SupportNamespace}.IProjectionHelper";
        var selectorType = operationKind switch
        {
            ProjectionOperationKind.GroupBy when usesProjectionHelperParameter =>
                $"global::System.Func<global::System.Linq.IGrouping<TKey, TIn>, {helperType}, {selectorResultType}>",
            ProjectionOperationKind.GroupBy =>
                $"global::System.Func<global::System.Linq.IGrouping<TKey, TIn>, {selectorResultType}>",
            _ when usesProjectionHelperParameter =>
                $"global::System.Func<TIn, {helperType}, {selectorResultType}>",
            _ => $"global::System.Func<TIn, {selectorResultType}>",
        };
        var parameters = operationKind == ProjectionOperationKind.GroupBy
            ? new[] { "global::System.Func<TIn, TKey> keySelector", $"{selectorType} selector", captureParameter }
            : new[] { $"{selectorType} selector", captureParameter };
        var typeParameters = operationKind == ProjectionOperationKind.GroupBy
            ? "<TKey, TResult>"
            : "<TResult>";
        return $"public global::System.Linq.IQueryable<TResult> {methodName}{typeParameters}({string.Join(", ", parameters.Where(parameter => parameter is not null))})";
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
