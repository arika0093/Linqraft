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
    // Support declarations are emitted in stages: entry-point classes, fluent wrappers, helper interfaces, and XML docs.

    private const string ThrowHelperClassName = "LinqraftDeclarationThrowHelpers";

    private static readonly ProjectionSupportExtensionClassGenerator[] Generators =
    [
        new SelectExprSupportExtensionClassGenerator(),
        new SelectManyExprSupportExtensionClassGenerator(),
        new GroupByExprSupportExtensionClassGenerator(),
    ];

    /// <summary>
    /// Gets class name.
    /// </summary>
    protected abstract string GetClassName(LinqraftGeneratorOptionsCore generatorOptions);

    /// <summary>
    /// Gets method name.
    /// </summary>
    protected abstract string GetMethodName(LinqraftGeneratorOptionsCore generatorOptions);

    /// <summary>
    /// Gets class summary.
    /// </summary>
    protected abstract string GetClassSummary(LinqraftGeneratorOptionsCore generatorOptions);

    /// <summary>
    /// Gets method signatures.
    /// </summary>
    protected abstract IEnumerable<SupportMethodSignature> GetMethodSignatures();

    /// <summary>
    /// Writes additional members.
    /// </summary>
    protected virtual void WriteAdditionalMembers(
        IndentedStringBuilder builder,
        LinqraftGeneratorOptionsCore generatorOptions
    ) { }

    /// <summary>
    /// Creates all declarations.
    /// </summary>
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

    /// <summary>
    /// Creates declaration.
    /// </summary>
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

    /// <summary>
    /// Writes method.
    /// </summary>
    private static void WriteMethod(
        IndentedStringBuilder builder,
        SupportMethodSignature signature,
        LinqraftGeneratorOptionsCore generatorOptions
    )
    {
        AppendXmlDocumentation(
            builder,
            signature.CreateSummary(generatorOptions),
            typeParameters: signature.HasKeySelector
                ?
                [
                    ("TIn", "The source element type."),
                    ("TKey", "The grouping key type."),
                    ("TResult", "The projected result type."),
                ]
                : [("TIn", "The source element type."), ("TResult", "The projected result type.")],
            parameters: CreateProjectionEntryPointParameterDocumentation(
                signature.HasKeySelector,
                signature.UsesProjectionHelperParameter,
                signature.CaptureKind
            ),
            returns: $"A deferred sequence of <typeparamref name=\"TResult\"/> values that {generatorOptions.GeneratorDisplayName} replaces with generated projection code.",
            example: CreateProjectionEntryPointExample(signature)
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
            builder.AppendLine($"=> throw {ThrowHelperClassName}.ForProjectionMethod();");
        }
    }

    /// <summary>
    /// Creates query overloads.
    /// </summary>
    protected IEnumerable<SupportMethodSignature> CreateQueryOverloads(
        ProjectionOperationKind operationKind,
        Func<LinqraftGeneratorOptionsCore, string> resultTypeFactory,
        bool includeEnumerable = true,
        bool hasKeySelector = false,
        bool includeAnonymousObjectPattern = true
    )
    {
        foreach (var receiver in GetReceivers(includeEnumerable))
        {
            foreach (var capture in GetCapturePatterns(includeAnonymousObjectPattern))
            {
                yield return new SupportMethodSignature
                {
                    CreateSummary = _ =>
                        $"Interception stub for {receiver.Kind} projections {capture.SummarySuffix}.",
                    CreateMethodName = GetMethodName,
                    CreateSignature = generatorOptions =>
                        CreateMethodSignature(
                            receiver.TypeName,
                            resultTypeFactory(generatorOptions),
                            selectorUsesObjectResult: false,
                            usesProjectionHelperParameter: false,
                            capture.Kind,
                            hasKeySelector,
                            generatorOptions
                        ),
                    CreateObsoleteMessage = _ => capture.ObsoleteMessage,
                    OperationKind = operationKind,
                    HasKeySelector = hasKeySelector,
                    UsesProjectionHelperParameter = false,
                    CaptureKind = capture.Kind,
                    IsLowPriority = false,
                };

                yield return new SupportMethodSignature
                {
                    CreateSummary = _ =>
                        $"Interception stub for {receiver.Kind} projections {capture.SummarySuffix}.",
                    CreateMethodName = GetMethodName,
                    CreateSignature = generatorOptions =>
                        CreateMethodSignature(
                            receiver.TypeName,
                            resultTypeFactory(generatorOptions),
                            selectorUsesObjectResult: true,
                            usesProjectionHelperParameter: false,
                            capture.Kind,
                            hasKeySelector,
                            generatorOptions
                        ),
                    CreateObsoleteMessage = _ => capture.ObsoleteMessage,
                    OperationKind = operationKind,
                    HasKeySelector = hasKeySelector,
                    UsesProjectionHelperParameter = false,
                    CaptureKind = capture.Kind,
                    IsLowPriority = true,
                };

                yield return new SupportMethodSignature
                {
                    CreateSummary = _ =>
                        $"Interception stub for {receiver.Kind} projections {capture.SummarySuffix}. Accepts a projection helper as the selector's second parameter.",
                    CreateMethodName = GetMethodName,
                    CreateSignature = generatorOptions =>
                        CreateMethodSignature(
                            receiver.TypeName,
                            resultTypeFactory(generatorOptions),
                            selectorUsesObjectResult: false,
                            usesProjectionHelperParameter: true,
                            capture.Kind,
                            hasKeySelector,
                            generatorOptions
                        ),
                    CreateObsoleteMessage = _ => capture.ObsoleteMessage,
                    OperationKind = operationKind,
                    HasKeySelector = hasKeySelector,
                    UsesProjectionHelperParameter = true,
                    CaptureKind = capture.Kind,
                    IsLowPriority = false,
                };

                yield return new SupportMethodSignature
                {
                    CreateSummary = _ =>
                        $"Interception stub for {receiver.Kind} projections {capture.SummarySuffix}. Accepts a projection helper as the selector's second parameter.",
                    CreateMethodName = GetMethodName,
                    CreateSignature = generatorOptions =>
                        CreateMethodSignature(
                            receiver.TypeName,
                            resultTypeFactory(generatorOptions),
                            selectorUsesObjectResult: true,
                            usesProjectionHelperParameter: true,
                            capture.Kind,
                            hasKeySelector,
                            generatorOptions
                        ),
                    CreateObsoleteMessage = _ => capture.ObsoleteMessage,
                    OperationKind = operationKind,
                    HasKeySelector = hasKeySelector,
                    UsesProjectionHelperParameter = true,
                    CaptureKind = capture.Kind,
                    IsLowPriority = true,
                };
            }
        }
    }

    /// <summary>
    /// Creates method signature.
    /// </summary>
    private string CreateMethodSignature(
        string receiverType,
        string resultType,
        bool selectorUsesObjectResult,
        bool usesProjectionHelperParameter,
        CaptureKind captureKind,
        bool hasKeySelector,
        LinqraftGeneratorOptionsCore generatorOptions
    )
    {
        var selectorReturnType = selectorUsesObjectResult ? "object" : resultType;
        var captureParameter = GetCaptureParameter(captureKind);
        string selectorType;
        if (hasKeySelector)
        {
            selectorType = usesProjectionHelperParameter
                ? $"global::System.Func<global::System.Linq.IGrouping<TKey, TIn>, global::{generatorOptions.SupportNamespace}.IProjectionHelper, {selectorReturnType}>"
                : $"global::System.Func<global::System.Linq.IGrouping<TKey, TIn>, {selectorReturnType}>";
        }
        else
        {
            selectorType = usesProjectionHelperParameter
                ? $"global::System.Func<TIn, global::{generatorOptions.SupportNamespace}.IProjectionHelper, {selectorReturnType}>"
                : $"global::System.Func<TIn, {selectorReturnType}>";
        }
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

    /// <summary>
    /// Creates linqraft query method signature.
    /// </summary>
    private static string CreateLinqraftQueryMethodSignature(
        ReceiverKind receiverKind,
        ProjectionOperationKind operationKind,
        bool selectorUsesObjectResult,
        bool usesProjectionHelperParameter,
        CaptureKind captureKind,
        LinqraftGeneratorOptionsCore generatorOptions
    )
    {
        var captureParameter = GetCaptureParameter(captureKind);
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
        var parameters =
            operationKind == ProjectionOperationKind.GroupBy
                ? new[]
                {
                    "global::System.Func<TIn, TKey> keySelector",
                    $"{selectorType} selector",
                    captureParameter,
                }
                : new[] { $"{selectorType} selector", captureParameter };
        var typeParameters =
            operationKind == ProjectionOperationKind.GroupBy ? "<TKey, TResult>" : "<TResult>";
        return $"public {GetNonFluentReceiverTypeName(receiverKind)}<TResult> {methodName}{typeParameters}({string.Join(", ", parameters.Where(parameter => parameter is not null))})";
    }

    /// <summary>
    /// Gets non fluent receiver type name.
    /// </summary>
    private static string GetNonFluentReceiverTypeName(ReceiverKind receiverKind)
    {
        return receiverKind == ReceiverKind.IQueryable
            ? "global::System.Linq.IQueryable"
            : "global::System.Collections.Generic.IEnumerable";
    }

    /// <summary>
    /// Provides support method signature.
    /// </summary>
    protected sealed record SupportMethodSignature
    {
        public required Func<LinqraftGeneratorOptionsCore, string> CreateSummary { get; init; }

        public required Func<LinqraftGeneratorOptionsCore, string> CreateMethodName { get; init; }

        public required Func<LinqraftGeneratorOptionsCore, string> CreateSignature { get; init; }

        public Func<LinqraftGeneratorOptionsCore, string?> CreateObsoleteMessage { get; init; } =
            _ => null;

        public required ProjectionOperationKind OperationKind { get; init; }

        public required bool HasKeySelector { get; init; }

        public required bool UsesProjectionHelperParameter { get; init; }

        public required CaptureKind CaptureKind { get; init; }

        public required bool IsLowPriority { get; init; }
    }

    /// <summary>
    /// Specifies capture kind.
    /// </summary>
    internal enum CaptureKind
    {
        None,
        AnonymousObject,
        Delegate,
    }

    /// <summary>
    /// Represents capture.
    /// </summary>
    private sealed record CapturePattern(
        CaptureKind Kind,
        string SummarySuffix,
        string? ObsoleteMessage
    );

}
