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
    // Projection helper interfaces describe compile-time-only markers that the generator rewrites away during emission.
    /// <summary>
    /// Creates projection helper declarations.
    /// </summary>
    private static IEnumerable<string> CreateProjectionHelperDeclarations(
        LinqraftGeneratorOptionsCore generatorOptions
    )
    {
        // Helper interfaces model compile-time markers only; every member is expected to be
        // recognized and rewritten by the source generator before runtime execution is possible.
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

    /// <summary>
    /// Appends projection hook interface methods.
    /// </summary>
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

    /// <summary>
    /// Creates projected value interface.
    /// </summary>
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

    /// <summary>
    /// Gets projection hook method signatures.
    /// </summary>
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
}
