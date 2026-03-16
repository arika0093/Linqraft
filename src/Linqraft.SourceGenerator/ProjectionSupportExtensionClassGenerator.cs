using System.Collections.Generic;
using System.Linq;
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

    protected abstract string ClassName { get; }

    protected abstract string MethodName { get; }

    protected abstract string ClassSummary { get; }

    protected abstract IEnumerable<SupportMethodSignature> GetMethodSignatures();

    public static string CreateAllDeclarations()
    {
        return string.Join("\n\n", Generators.Select(generator => generator.CreateDeclaration()));
    }

    private string CreateDeclaration()
    {
        var builder = new IndentedStringBuilder();
        builder.AppendLine("/// <summary>");
        builder.AppendLine($"/// {ClassSummary}");
        builder.AppendLine("/// </summary>");
        builder.AppendLine("[global::Microsoft.CodeAnalysis.EmbeddedAttribute]");
        builder.AppendLine($"internal static partial class {ClassName}");
        builder.AppendLine("{");
        using (builder.Indent())
        {
            builder.AppendLine(
                $"private static global::System.InvalidOperationException ThrowInterceptionRequired => new global::System.InvalidOperationException(\"Linqraft source generator should replace {MethodName} invocations before execution.\");"
            );
            builder.AppendLine();

            foreach (var signature in GetMethodSignatures())
            {
                WriteMethod(builder, signature);
                builder.AppendLine();
            }
        }

        builder.AppendLine("}");
        return builder.ToString().TrimEnd();
    }

    private void WriteMethod(IndentedStringBuilder builder, SupportMethodSignature signature)
    {
        builder.AppendLine("/// <summary>");
        builder.AppendLine($"/// {signature.Summary}");
        builder.AppendLine("/// </summary>");
        if (signature.ObsoleteMessage is not null)
        {
            builder.AppendLine(
                $"[global::System.Obsolete(\"{signature.ObsoleteMessage}\", false)]"
            );
        }
        if (signature.IsLowPriority)
        {
            builder.AppendLine(OverloadResolutionLowPriority);
        }

        builder.AppendLine(signature.Signature);
        using (builder.Indent())
        {
            builder.AppendLine("=> throw ThrowInterceptionRequired;");
        }
    }

    protected IEnumerable<SupportMethodSignature> CreateQueryOverloads(
        string resultType,
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
                        ObsoleteMessage = (string?)"Anonymous-object capture is obsolete. Use the delegate-based capture pattern instead.",
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
                    Summary = capture.Summary,
                    Signature = CreateMethodSignature(
                        receiver.TypeName,
                        resultType,
                        selectorUsesObjectResult: false,
                        capture.Parameter,
                        hasKeySelector
                    ),
                    ObsoleteMessage = capture.ObsoleteMessage,
                    IsLowPriority = false,
                };

                yield return new SupportMethodSignature
                {
                    Summary = capture.Summary,
                    Signature = CreateMethodSignature(
                        receiver.TypeName,
                        resultType,
                        selectorUsesObjectResult: true,
                        capture.Parameter,
                        hasKeySelector
                    ),
                    ObsoleteMessage = capture.ObsoleteMessage,
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
        bool hasKeySelector
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
        return $"public static {receiverType}<TResult> {MethodName}{typeParameters}({string.Join(", ", filteredParameters)}) where TIn : class";
    }

    protected sealed record SupportMethodSignature
    {
        public required string Summary { get; init; }

        public required string Signature { get; init; }

        public string? ObsoleteMessage { get; init; }

        public required bool IsLowPriority { get; init; }
    }

    private sealed class SelectExprSupportExtensionClassGenerator
        : ProjectionSupportExtensionClassGenerator
    {
        protected override string ClassName => "SelectExprExtensions";

        protected override string MethodName => "SelectExpr";

        protected override string ClassSummary =>
            "Provides the SelectExpr entry points that Linqraft intercepts ahead of time.";

        protected override IEnumerable<SupportMethodSignature> GetMethodSignatures() =>
            CreateQueryOverloads("TResult");
    }

    private sealed class SelectManyExprSupportExtensionClassGenerator
        : ProjectionSupportExtensionClassGenerator
    {
        protected override string ClassName => "SelectManyExprExtensions";

        protected override string MethodName => "SelectManyExpr";

        protected override string ClassSummary =>
            "Provides the SelectManyExpr entry points that Linqraft intercepts ahead of time.";

        protected override IEnumerable<SupportMethodSignature> GetMethodSignatures() =>
            CreateQueryOverloads("global::System.Collections.Generic.IEnumerable<TResult>");
    }

    private sealed class GroupByExprSupportExtensionClassGenerator
        : ProjectionSupportExtensionClassGenerator
    {
        protected override string ClassName => "GroupByExprExtensions";

        protected override string MethodName => "GroupByExpr";

        protected override string ClassSummary =>
            "Provides the GroupByExpr entry points that Linqraft intercepts ahead of time.";

        protected override IEnumerable<SupportMethodSignature> GetMethodSignatures() =>
            CreateQueryOverloads("TResult", hasKeySelector: true);
    }
}
