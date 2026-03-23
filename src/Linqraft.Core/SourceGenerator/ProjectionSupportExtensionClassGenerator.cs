using System;
using System.Collections.Generic;
using System.Linq;
using Linqraft.Core.Configuration;
using Linqraft.Core.Formatting;
using Linqraft.Core.Generation;
using static Linqraft.Core.Generation.CodeTemplateContents;

namespace Linqraft.SourceGenerator;

internal abstract class ProjectionSupportExtensionClassGenerator
{
    private const string ThrowHelperClassName = "LinqraftDeclarationThrowHelpers";

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

    private static IEnumerable<string> CreateProjectionHelperDeclarations(
        LinqraftGeneratorOptionsCore generatorOptions
    )
    {
        var hooks = generatorOptions.GetValidatedProjectionHooks();

        var builder = new IndentedStringBuilder();
        builder.AppendLines(
            """
            /// <summary>
            /// Supplies the rewrite markers that generated selectors can use to describe how nested values should be interpreted.
            /// Each member is a compile-time marker only: the source generator recognizes the call, rewrites the projection tree,
            /// and removes the stub before any of these members can execute at runtime.
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
    }

    private static void AppendProjectionHookInterfaceMethods(
        IndentedStringBuilder builder,
        LinqraftProjectionHookDefinition hook
    )
    {
        foreach (
            var signature in GetProjectionHookMethodSignatures(hook, interfaceMethod: true)
                .Select((value, index) => (Signature: value, Index: index))
        )
        {
            AppendProjectionHookInterfaceMethodDocumentation(builder, hook, signature.Index);
            builder.AppendLine(signature.Signature);
            builder.AppendLine("");
        }
    }

    private static string CreateProjectedValueInterface()
    {
        var builder = new IndentedStringBuilder();
        builder.AppendLines(
            """
            /// <summary>
            /// Represents a single projection target that can be reshaped with a local <c>Select</c> expression inside another generated projection.
            /// </summary>
            [global::Microsoft.CodeAnalysis.EmbeddedAttribute]
            internal partial interface IProjectedValue<T>
            {
            """
        );
        using (builder.Indent())
        {
            AppendXmlDocumentation(
                builder,
                "Shapes the wrapped value into a nested projection result.",
                typeParameters: [("TResult", "The nested projection result type.")],
                parameters:
                [
                    (
                        "selector",
                        "A projection that describes how the wrapped value should be converted into the nested result."
                    ),
                ],
                returns: "A marker representing the nested projection result that the source generator inlines into the surrounding projection.",
                example: """
                var dto = query.UseLinqraft().Select&lt;OrderDto&gt;((order, helper) =&gt; new OrderDto
                {
                    Customer = helper.Project(order.Customer).Select(customer =&gt; new CustomerDto { Id = customer.Id })
                });
                """
            );
            builder.AppendLine(
                "TResult Select<TResult>(global::System.Func<T, TResult> selector);"
            );
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
            [
                $"{prefix}T {hook.MethodName}<T>(T? value){suffix}",
            ],
            LinqraftProjectionHookKind.Projection =>
            [
                $"{prefix}TResult {hook.MethodName}<TResult>(object? value){suffix}",
                $"{prefix}object {hook.MethodName}(object? value){suffix}",
            ],
            LinqraftProjectionHookKind.Project =>
            [
                $"{prefix}IProjectedValue<T> {hook.MethodName}<T>(T? value){suffix}",
            ],
            _ => throw new InvalidOperationException(
                $"Unsupported projection helper kind '{hook.Kind}'."
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

    private static IEnumerable<CapturePattern> GetCapturePatterns(
        bool includeAnonymousObjectPattern = true
    )
    {
        yield return new(CaptureKind.None, "without captures", null);
        if (includeAnonymousObjectPattern)
        {
            yield return new(
                CaptureKind.AnonymousObject,
                "with anonymous-object captures",
                "Anonymous-object capture is obsolete. Use the delegate-based capture pattern instead."
            );
        }
        yield return new(CaptureKind.Delegate, "with NativeAOT-safe delegate captures", null);
    }

    private static IEnumerable<(string TypeName, string Kind)> GetReceivers(bool includeEnumerable)
    {
        yield return ("global::System.Linq.IQueryable", "queryable sequence");
        if (includeEnumerable)
        {
            yield return ("global::System.Collections.Generic.IEnumerable", "enumerable sequence");
        }
    }

    private static (string Name, string Description)[] CreateFluentMethodTypeParameterDocumentation(
        ProjectionOperationKind operationKind
    )
    {
        return operationKind == ProjectionOperationKind.GroupBy
            ? [("TKey", "The grouping key type."), ("TResult", "The projected result type.")]
            : [("TResult", "The projected result type.")];
    }

    private static string CreateFluentMethodReturnsDocumentation(
        ReceiverKind receiverKind,
        LinqraftGeneratorOptionsCore generatorOptions
    )
    {
        var resultKind =
            receiverKind == ReceiverKind.IQueryable ? "queryable" : "deferred enumerable";
        return $"A {resultKind} sequence that {generatorOptions.GeneratorDisplayName} replaces with the generated projection pipeline.";
    }

    private static string? GetCaptureParameter(CaptureKind captureKind)
    {
        return captureKind switch
        {
            CaptureKind.None => null,
            CaptureKind.AnonymousObject => "object capture",
            CaptureKind.Delegate => "global::System.Func<object> capture",
            _ => throw new InvalidOperationException($"Unsupported capture kind '{captureKind}'."),
        };
    }

    private static (string Name, string Description)? CreateCaptureParameterDocumentation(
        CaptureKind captureKind
    )
    {
        return captureKind switch
        {
            CaptureKind.None => null,
            CaptureKind.AnonymousObject => (
                "capture",
                "Provides external values that should be inlined into the generated projection through anonymous-object capture."
            ),
            CaptureKind.Delegate => (
                "capture",
                "Returns any external values that should be inlined into the generated projection using the NativeAOT-safe delegate capture pattern."
            ),
            _ => throw new InvalidOperationException($"Unsupported capture kind '{captureKind}'."),
        };
    }

    private static void AppendProjectionHookInterfaceMethodDocumentation(
        IndentedStringBuilder builder,
        LinqraftProjectionHookDefinition hook,
        int signatureIndex
    )
    {
        var typeParameters = hook.Kind switch
        {
            LinqraftProjectionHookKind.Projection when signatureIndex == 0 => new[]
            {
                ("TResult", "The nested DTO type to generate."),
            },
            LinqraftProjectionHookKind.Projection => null,
            LinqraftProjectionHookKind.Project =>
            [
                ("T", "The source value type exposed to the nested selector."),
            ],
            _ => [("T", "The source value type being marked for rewrite.")],
        };

        var (summary, returnDescription, example) = hook.Kind switch
        {
            LinqraftProjectionHookKind.LeftJoin => (
                "Marks a nullable navigation access so the generated projection keeps left-join semantics.",
                "The projected value placeholder that the source generator rewrites into a null-safe navigation access.",
                """
                var dto = query.UseLinqraft().Select&lt;OrderDto&gt;((order, helper) =&gt; new OrderDto
                {
                    CustomerName = helper.AsLeftJoin(order.Customer).Name
                });
                """
            ),
            LinqraftProjectionHookKind.InnerJoin => (
                "Marks a navigation access so the generated projection keeps inner-join semantics.",
                "The projected value placeholder that the source generator rewrites into a required navigation access.",
                """
                var dto = query.UseLinqraft().Select&lt;OrderDto&gt;((order, helper) =&gt; new OrderDto
                {
                    CustomerName = helper.AsInnerJoin(order.Customer!).Name
                });
                """
            ),
            LinqraftProjectionHookKind.Projectable => (
                "Requests that the generated projection inline the referenced computed member instead of executing it locally.",
                "The projected value placeholder that the source generator replaces with the inlined member body.",
                """
                var dto = query.UseLinqraft().Select&lt;OrderDto&gt;((order, helper) =&gt; new OrderDto
                {
                    Total = helper.AsInline(order.TotalWithTax)
                });
                """
            ),
            LinqraftProjectionHookKind.Projection when signatureIndex == 0 => (
                "Requests that the supplied value be converted into a generated nested DTO of type <typeparamref name=\"TResult\"/>.",
                "A placeholder for the nested DTO projection that the source generator materializes.",
                """
                var dto = query.UseLinqraft().Select&lt;OrderDto&gt;((order, helper) =&gt; new OrderDto
                {
                    Customer = helper.AsProjection&lt;CustomerDto&gt;(order.Customer)
                });
                """
            ),
            LinqraftProjectionHookKind.Projection => (
                "Requests that the supplied value be converted into a generated nested DTO when the result type is determined from the assignment target.",
                "A placeholder object that the source generator replaces with the inferred nested projection.",
                """
                object nested = helper.AsProjection(order.Customer);
                """
            ),
            LinqraftProjectionHookKind.Project => (
                "Creates a single-value projection context so a nested member can be shaped with its own local <c>Select</c> call.",
                "A marker that exposes <c>Select</c> for a nested single-value projection.",
                """
                var dto = query.UseLinqraft().Select&lt;OrderDto&gt;((order, helper) =&gt; new OrderDto
                {
                    Customer = helper.Project(order.Customer).Select(customer =&gt; new CustomerDto { Id = customer.Id })
                });
                """
            ),
            _ => throw new InvalidOperationException(
                $"Unsupported projection helper kind '{hook.Kind}'."
            ),
        };

        AppendXmlDocumentation(
            builder,
            summary,
            typeParameters: typeParameters,
            parameters:
            [
                (
                    "value",
                    "The source value or navigation member that should participate in the requested rewrite."
                ),
            ],
            returns: returnDescription,
            example: example
        );
    }

    private static void AppendXmlDocumentation(
        IndentedStringBuilder builder,
        string summary,
        IEnumerable<(string Name, string Description)>? typeParameters = null,
        IEnumerable<(string Name, string Description)>? parameters = null,
        string? returns = null,
        string? example = null
    )
    {
        builder.AppendLine("/// <summary>");
        foreach (var line in SplitDocumentationLines(summary))
        {
            builder.AppendLine($"/// {line}");
        }

        builder.AppendLine("/// </summary>");
        if (typeParameters is not null)
        {
            foreach (var (name, description) in typeParameters)
            {
                builder.AppendLine($"/// <typeparam name=\"{name}\">{description}</typeparam>");
            }
        }

        if (parameters is not null)
        {
            foreach (var (name, description) in parameters)
            {
                builder.AppendLine($"/// <param name=\"{name}\">{description}</param>");
            }
        }

        if (returns is not null)
        {
            builder.AppendLine($"/// <returns>{returns}</returns>");
        }

        if (example is not null)
        {
            builder.AppendLine("/// <example>");
            foreach (var line in SplitDocumentationLines(example))
            {
                builder.AppendLine($"/// {line}");
            }

            builder.AppendLine("/// </example>");
        }
    }

    private static IEnumerable<string> SplitDocumentationLines(string text)
    {
        return text.Replace("\r\n", "\n").Split('\n');
    }

    private static (
        string Name,
        string Description
    )[] CreateProjectionEntryPointParameterDocumentation(
        bool hasKeySelector,
        bool usesProjectionHelperParameter,
        CaptureKind captureKind
    )
    {
        var parameters = new List<(string Name, string Description)>
        {
            ("query", "The source sequence whose projection should be intercepted and rewritten."),
        };

        var selectorDescription = usesProjectionHelperParameter
            ? "Describes the projection to generate. The second lambda parameter exposes rewrite markers such as <c>AsInline</c> and <c>AsProjection</c>."
            : "Describes the projection to generate.";

        if (hasKeySelector)
        {
            parameters.Add(("keySelector", "Extracts the grouping key for each source element."));
        }
        else
        {
            parameters.Add(("selector", selectorDescription));
        }

        if (hasKeySelector)
        {
            parameters.Add(
                (
                    "selector",
                    usesProjectionHelperParameter
                        ? "Describes the projection to generate for each group. The second lambda parameter exposes rewrite markers such as <c>AsInline</c> and <c>AsProjection</c>."
                        : "Describes the projection to generate for each group."
                )
            );
        }

        if (CreateCaptureParameterDocumentation(captureKind) is { } captureParameter)
        {
            parameters.Add(captureParameter);
        }

        return parameters.ToArray();
    }

    private static (string Name, string Description)[] CreateFluentMethodParameterDocumentation(
        ProjectionOperationKind operationKind,
        bool usesProjectionHelperParameter,
        CaptureKind captureKind
    )
    {
        var parameters = new List<(string Name, string Description)>();
        if (operationKind == ProjectionOperationKind.GroupBy)
        {
            parameters.Add(("keySelector", "Extracts the grouping key for each source element."));
        }

        parameters.Add(
            (
                "selector",
                usesProjectionHelperParameter
                    ? "Describes the projection to generate. The second lambda parameter exposes rewrite markers such as <c>AsInline</c> and <c>AsProjection</c>."
                    : "Describes the projection to generate."
            )
        );

        if (CreateCaptureParameterDocumentation(captureKind) is { } captureParameter)
        {
            parameters.Add(captureParameter);
        }

        return parameters.ToArray();
    }

    private static string CreateProjectionEntryPointExample(SupportMethodSignature signature)
    {
        var invocation = signature.OperationKind switch
        {
            ProjectionOperationKind.Select => signature.CaptureKind switch
            {
                CaptureKind.AnonymousObject =>
                    "query.UseLinqraft().Select&lt;OrderDto&gt;(order =&gt; new OrderDto { Total = order.Total + offset }, new { offset })",
                CaptureKind.Delegate =>
                    "query.UseLinqraft().Select&lt;OrderDto&gt;(order =&gt; new OrderDto { Total = order.Total + offset }, () =&gt; new { offset })",
                _ when signature.UsesProjectionHelperParameter =>
                    "query.UseLinqraft().Select&lt;OrderDto&gt;((order, helper) =&gt; new OrderDto { Total = helper.AsInline(order.TotalWithTax) })",
                _ =>
                    "query.UseLinqraft().Select&lt;OrderDto&gt;(order =&gt; new OrderDto { Id = order.Id })",
            },
            ProjectionOperationKind.SelectMany => signature.CaptureKind switch
            {
                CaptureKind.Delegate =>
                    "query.UseLinqraft().SelectMany&lt;OrderItemDto&gt;(order =&gt; order.Items.Select(item =&gt; new OrderItemDto { Total = item.Total + offset }), () =&gt; new { offset })",
                _ when signature.UsesProjectionHelperParameter =>
                    "query.UseLinqraft().SelectMany&lt;OrderItemDto&gt;((order, helper) =&gt; order.Items.Select(item =&gt; new OrderItemDto { Total = helper.AsInline(item.TotalWithTax) }))",
                _ =>
                    "query.UseLinqraft().SelectMany&lt;OrderItemDto&gt;(order =&gt; order.Items.Select(item =&gt; new OrderItemDto { Id = item.Id }))",
            },
            ProjectionOperationKind.GroupBy => signature.CaptureKind switch
            {
                CaptureKind.Delegate =>
                    "query.UseLinqraft().GroupBy&lt;int, OrderSummaryDto&gt;(order =&gt; order.CustomerId, group =&gt; new OrderSummaryDto { OffsetKey = group.Key + offset }, () =&gt; new { offset })",
                _ when signature.UsesProjectionHelperParameter =>
                    "query.UseLinqraft().GroupBy&lt;int, OrderSummaryDto&gt;(order =&gt; order.CustomerId, (group, helper) =&gt; new OrderSummaryDto { Key = helper.AsInline(group.Key) })",
                _ =>
                    "query.UseLinqraft().GroupBy&lt;int, OrderSummaryDto&gt;(order =&gt; order.CustomerId, group =&gt; new OrderSummaryDto { Key = group.Key })",
            },
            _ => throw new InvalidOperationException(
                $"Unsupported projection operation '{signature.OperationKind}'."
            ),
        };

        return $"<code>var projected = {invocation};</code>";
    }

    private static string CreateFluentMethodExample(
        ProjectionOperationKind operationKind,
        bool usesProjectionHelperParameter,
        CaptureKind captureKind
    )
    {
        var invocation = operationKind switch
        {
            ProjectionOperationKind.Select => captureKind switch
            {
                CaptureKind.Delegate =>
                    "query.UseLinqraft().Select&lt;OrderDto&gt;(order =&gt; new OrderDto { Total = order.Total + offset }, () =&gt; new { offset })",
                _ when usesProjectionHelperParameter =>
                    "query.UseLinqraft().Select&lt;OrderDto&gt;((order, helper) =&gt; new OrderDto { Total = helper.AsInline(order.TotalWithTax) })",
                _ =>
                    "query.UseLinqraft().Select&lt;OrderDto&gt;(order =&gt; new OrderDto { Id = order.Id })",
            },
            ProjectionOperationKind.SelectMany => captureKind switch
            {
                CaptureKind.Delegate =>
                    "query.UseLinqraft().SelectMany&lt;OrderItemDto&gt;(order =&gt; order.Items.Select(item =&gt; new OrderItemDto { Total = item.Total + offset }), () =&gt; new { offset })",
                _ when usesProjectionHelperParameter =>
                    "query.UseLinqraft().SelectMany&lt;OrderItemDto&gt;((order, helper) =&gt; order.Items.Select(item =&gt; new OrderItemDto { Total = helper.AsInline(item.TotalWithTax) }))",
                _ =>
                    "query.UseLinqraft().SelectMany&lt;OrderItemDto&gt;(order =&gt; order.Items.Select(item =&gt; new OrderItemDto { Id = item.Id }))",
            },
            ProjectionOperationKind.GroupBy => captureKind switch
            {
                CaptureKind.Delegate =>
                    "query.UseLinqraft().GroupBy&lt;int, OrderSummaryDto&gt;(order =&gt; order.CustomerId, group =&gt; new OrderSummaryDto { OffsetKey = group.Key + offset }, () =&gt; new { offset })",
                _ when usesProjectionHelperParameter =>
                    "query.UseLinqraft().GroupBy&lt;int, OrderSummaryDto&gt;(order =&gt; order.CustomerId, (group, helper) =&gt; new OrderSummaryDto { Key = helper.AsInline(group.Key) })",
                _ =>
                    "query.UseLinqraft().GroupBy&lt;int, OrderSummaryDto&gt;(order =&gt; order.CustomerId, group =&gt; new OrderSummaryDto { Key = group.Key })",
            },
            _ => throw new InvalidOperationException(
                $"Unsupported projection operation '{operationKind}'."
            ),
        };

        return $"<code>var projected = {invocation};</code>";
    }

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

    private static string GetNonFluentReceiverTypeName(ReceiverKind receiverKind)
    {
        return receiverKind == ReceiverKind.IQueryable
            ? "global::System.Linq.IQueryable"
            : "global::System.Collections.Generic.IEnumerable";
    }

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

    internal enum CaptureKind
    {
        None,
        AnonymousObject,
        Delegate,
    }

    private sealed record CapturePattern(
        CaptureKind Kind,
        string SummarySuffix,
        string? ObsoleteMessage
    );

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
            CreateQueryOverloads(ProjectionOperationKind.Select, _ => "TResult");
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
            CreateQueryOverloads(
                ProjectionOperationKind.SelectMany,
                _ => "global::System.Collections.Generic.IEnumerable<TResult>",
                includeAnonymousObjectPattern: false
            );
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
            CreateQueryOverloads(
                ProjectionOperationKind.GroupBy,
                _ => "TResult",
                hasKeySelector: true,
                includeAnonymousObjectPattern: false
            );
    }
}
