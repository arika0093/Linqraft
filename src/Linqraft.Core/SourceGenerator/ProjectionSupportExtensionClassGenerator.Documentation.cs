using System;
using System.Collections.Generic;
using System.Linq;
using Linqraft.Core.Configuration;
using Linqraft.Core.Formatting;
using Linqraft.Core.Generation;
using static Linqraft.Core.Generation.CodeTemplateContents;

namespace Linqraft.SourceGenerator;

/// <summary>
/// Generates projection support extension class.
/// </summary>
internal abstract partial class ProjectionSupportExtensionClassGenerator
{
    // XML docs and examples are generated alongside signatures so the support surface stays self-describing.
    /// <summary>
    /// Gets capture patterns.
    /// </summary>
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

    /// <summary>
    /// Gets receivers.
    /// </summary>
    private static IEnumerable<(string TypeName, string Kind)> GetReceivers(bool includeEnumerable)
    {
        yield return ("global::System.Linq.IQueryable", "queryable sequence");
        if (includeEnumerable)
        {
            yield return ("global::System.Collections.Generic.IEnumerable", "enumerable sequence");
        }
    }

    /// <summary>
    /// Creates fluent method type parameter documentation.
    /// </summary>
    private static (string Name, string Description)[] CreateFluentMethodTypeParameterDocumentation(
        ProjectionOperationKind operationKind
    )
    {
        return operationKind == ProjectionOperationKind.GroupBy
            ? [("TKey", "The grouping key type."), ("TResult", "The projected result type.")]
            : [("TResult", "The projected result type.")];
    }

    /// <summary>
    /// Creates fluent method returns documentation.
    /// </summary>
    private static string CreateFluentMethodReturnsDocumentation(
        ReceiverKind receiverKind,
        LinqraftGeneratorOptionsCore generatorOptions
    )
    {
        var resultKind =
            receiverKind == ReceiverKind.IQueryable ? "queryable" : "deferred enumerable";
        return $"A {resultKind} sequence that {generatorOptions.GeneratorDisplayName} replaces with the generated projection pipeline.";
    }

    /// <summary>
    /// Gets capture parameter.
    /// </summary>
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

    /// <summary>
    /// Creates capture parameter documentation.
    /// </summary>
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

    /// <summary>
    /// Appends projection hook interface method documentation.
    /// </summary>
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

    /// <summary>
    /// Appends xml documentation.
    /// </summary>
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

    /// <summary>
    /// Splits documentation lines.
    /// </summary>
    private static IEnumerable<string> SplitDocumentationLines(string text)
    {
        return text.Replace("\r\n", "\n").Split('\n');
    }

    /// <summary>
    /// Creates projection entry point parameter documentation.
    /// </summary>
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

    /// <summary>
    /// Creates fluent method parameter documentation.
    /// </summary>
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

    /// <summary>
    /// Creates projection entry point example.
    /// </summary>
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

    /// <summary>
    /// Creates fluent method example.
    /// </summary>
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
}
