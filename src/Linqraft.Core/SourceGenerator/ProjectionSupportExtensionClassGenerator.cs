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

    public static string CreateAllDeclarations(LinqraftGeneratorOptionsCore generatorOptions)
    {
        return string.Join(
            "\n\n",
            Generators
                .Select(generator => generator.CreateDeclaration(generatorOptions))
                .Concat(CreateProjectionHelperDeclarations(generatorOptions))
        );
    }

    private static IEnumerable<string> CreateProjectionHelperDeclarations(
        LinqraftGeneratorOptionsCore generatorOptions
    )
    {
        var hooks = generatorOptions.GetValidatedProjectionHooks();

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
            foreach (var hook in hooks)
            {
                AppendProjectionHookInterfaceMethods(builder, hook);
            }
        }

        builder.AppendLine("}");
        yield return builder.ToString().TrimEnd();

        if (hooks.Any(hook => hook.Kind == LinqraftProjectionHookKind.Project))
        {
            yield return CreateProjectedValueInterface().TrimEnd();
        }

        foreach (
            var group in hooks.GroupBy(
                LinqraftGeneratorOptionsCore.GetProjectionHookClassName,
                StringComparer.Ordinal
            )
        )
        {
            yield return CreateProjectionHookExtensionClass(
                group.Key,
                group.ToArray(),
                generatorOptions
            ).TrimEnd();
        }
    }

    private static void AppendProjectionHookInterfaceMethods(
        IndentedStringBuilder builder,
        LinqraftProjectionHookDefinition hook
    )
    {
        builder.AppendLines(
            $$"""
            /// <summary>
            /// Marker used by generated projections to trigger the {{hook.Kind}} rewrite behavior.
            /// </summary>
            """
        );
        foreach (var signature in GetProjectionHookMethodSignatures(hook, interfaceMethod: true))
        {
            builder.AppendLine(signature);
        }
    }

    private static string CreateProjectedValueInterface()
    {
        var builder = new IndentedStringBuilder();
        builder.AppendLines(
            """
            /// <summary>
            /// Represents a single projection target that can be shaped with a local Select expression.
            /// </summary>
            [global::Microsoft.CodeAnalysis.EmbeddedAttribute]
            internal partial interface IProjectedValue<T>
            {
            """
        );
        using (builder.Indent())
        {
            builder.AppendLines(
                """
                /// <summary>
                /// Shapes the wrapped projection target into a new result.
                /// </summary>
                TResult Select<TResult>(global::System.Func<T, TResult> selector);
                """
            );
        }

        builder.AppendLine("}");
        return builder.ToString();
    }

    private static string CreateProjectionHookExtensionClass(
        string className,
        IReadOnlyList<LinqraftProjectionHookDefinition> hooks,
        LinqraftGeneratorOptionsCore generatorOptions
    )
    {
        var builder = new IndentedStringBuilder();
        builder.AppendLines(
            $$"""
            /// <summary>
            /// Provides marker extension methods for projection hook rewrites.
            /// </summary>
            [global::Microsoft.CodeAnalysis.EmbeddedAttribute]
            internal static partial class {{className}}
            {
            """
        );
        using (builder.Indent())
        {
            builder.AppendLine(
                $"private static global::System.InvalidOperationException ThrowInterceptionRequired => new global::System.InvalidOperationException(\"{generatorOptions.GeneratorDisplayName} source generator should replace projection hook invocations before execution.\");"
            );
            builder.AppendLine();

            foreach (var hook in hooks)
            {
                builder.AppendLines(
                    $$"""
                    /// <summary>
                    /// Marker used by generated projections to trigger the {{hook.Kind}} rewrite behavior.
                    /// </summary>
                    """
                );
                foreach (
                    var signature in GetProjectionHookMethodSignatures(hook, interfaceMethod: false)
                )
                {
                    builder.AppendLine(signature);
                    using (builder.Indent())
                    {
                        builder.AppendLine("=> throw ThrowInterceptionRequired;");
                    }
                    builder.AppendLine();
                }
            }
        }

        builder.AppendLine("}");
        return builder.ToString();
    }

    private static IEnumerable<string> GetProjectionHookMethodSignatures(
        LinqraftProjectionHookDefinition hook,
        bool interfaceMethod
    )
    {
        var prefix = interfaceMethod ? string.Empty : "public static ";
        var suffix = interfaceMethod ? ";" : string.Empty;
        return hook.Kind switch
        {
            LinqraftProjectionHookKind.LeftJoin
            or LinqraftProjectionHookKind.InnerJoin
            or LinqraftProjectionHookKind.Projectable =>
                [$"{prefix}T {hook.MethodName}<T>(T value){suffix}"],
            LinqraftProjectionHookKind.Projection =>
                [
                    $"{prefix}TResult {hook.MethodName}<TResult>(object value){suffix}",
                    $"{prefix}object {hook.MethodName}(object value){suffix}",
                ],
            LinqraftProjectionHookKind.Project =>
                [
                    $"{prefix}IProjectedValue<T> {hook.MethodName}<T>(T value){suffix}",
                ],
            _ => throw new InvalidOperationException(
                $"Unsupported projection hook kind '{hook.Kind}'."
            ),
        };
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
        bool hasKeySelector = false
    )
    {
        foreach (
            var receiver in new[]
            {
                (TypeName: "global::System.Linq.IQueryable", Kind: "queryable sequence"),
                (
                    TypeName: "global::System.Collections.Generic.IEnumerable",
                    Kind: "enumerable sequence"
                ),
            }
        )
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
