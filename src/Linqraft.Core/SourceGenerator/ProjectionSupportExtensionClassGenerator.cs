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
            Generators.Select(generator => generator.CreateDeclaration(generatorOptions))
        );
    }

    private string CreateDeclaration(LinqraftGeneratorOptionsCore generatorOptions)
    {
        var builder = new IndentedStringBuilder();
        builder.AppendLine("/// <summary>");
        builder.AppendLine($"/// {GetClassSummary(generatorOptions)}");
        builder.AppendLine("/// </summary>");
        builder.AppendLine("[global::Microsoft.CodeAnalysis.EmbeddedAttribute]");
        builder.AppendLine(
            $"internal static partial class {GetClassName(generatorOptions)}"
        );
        builder.AppendLine("{");
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

    private void WriteMethod(
        IndentedStringBuilder builder,
        SupportMethodSignature signature,
        LinqraftGeneratorOptionsCore generatorOptions
    )
    {
        builder.AppendLine("/// <summary>");
        builder.AppendLine($"/// {signature.CreateSummary(generatorOptions)}");
        builder.AppendLine("/// </summary>");
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
        string? captureParameter,
        bool hasKeySelector,
        LinqraftGeneratorOptionsCore generatorOptions
    )
    {
        var selectorReturnType = selectorUsesObjectResult ? "object" : resultType;
        var parameters = hasKeySelector
            ? new[]
            {
                $"this {receiverType}<TIn> query",
                "global::System.Func<TIn, TKey> keySelector",
                $"global::System.Func<global::System.Linq.IGrouping<TKey, TIn>, {selectorReturnType}> selector",
                captureParameter,
            }
            : new[]
            {
                $"this {receiverType}<TIn> query",
                $"global::System.Func<TIn, {selectorReturnType}> selector",
                captureParameter,
            };
        var filteredParameters = parameters.Where(parameter => parameter is not null);
        var typeParameters = hasKeySelector ? "<TIn, TKey, TResult>" : "<TIn, TResult>";
        return
            $"public static {receiverType}<TResult> {GetMethodName(generatorOptions)}{typeParameters}({string.Join(", ", filteredParameters)}) where TIn : class";
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
