using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;

internal static class ConstSourceCodes
{
    // Export all source codes
    public static void ExportAll(IncrementalGeneratorPostInitializationContext context)
    {
        context.AddSource("InterceptsLocationAttribute.g.cs", InterceptsLocationAttribute);
        context.AddSource("DummyExpression.g.cs", DummyExpression);
        context.AddSource("LinqraftAccessibilityAttribute.g.cs", LinqraftAccessibilityAttribute);
    }

    [StringSyntax("csharp")]
    private const string InterceptsLocationAttribute = $$"""
        {{CommonHeader}}
        using System;

        namespace System.Runtime.CompilerServices
        {
            [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
            internal sealed class InterceptsLocationAttribute : Attribute
            {
                public InterceptsLocationAttribute(int version, string data)
                {
                    Version = version;
                    Data = data;
                }

                public int Version { get; }
                public string Data { get; }
            }
        }
        """;

    [StringSyntax("csharp")]
    private const string DummyExpression = $$"""
        {{CommonHeader}}

        using System;
        using System.Collections.Generic;
        using System.Linq;

        /// <summary>
        /// Dummy expression methods for Linqraft to compile correctly.
        /// </summary>
        internal static class DummyExpression
        {
            /// <summary>
            /// Create select expression method, usable nullable operators, and generate instance DTOs.
            /// </summary>
            public static IQueryable<TResult> SelectExpr<TIn, TResult>(this IQueryable<TIn> query, Func<TIn, TResult> selector)
                where TIn : class
            {
                throw new NotImplementedException();
            }

            /// <summary>
            /// Create select expression method, usable nullable operators, and generate instance DTOs.
            /// </summary>
            [global::System.Runtime.CompilerServices.OverloadResolutionPriority(-1)]
            public static IQueryable<TResult> SelectExpr<TIn, TResult>(this IQueryable<TIn> query, Func<TIn, object> selector)
                where TIn : class
            {
                throw new NotImplementedException();
            }

            /// <summary>
            /// Create select expression method, with generate instance DTOs.
            /// Works with IEnumerable where nullable operators are supported natively.
            /// </summary>
            public static IEnumerable<TResult> SelectExpr<TIn, TResult>(this IEnumerable<TIn> query, Func<TIn, TResult> selector)
                where TIn : class
            {
                throw new NotImplementedException();
            }

            /// <summary>
            /// Create select expression method, with generate instance DTOs.
            /// Works with IEnumerable where nullable operators are supported natively.
            /// </summary>
            [global::System.Runtime.CompilerServices.OverloadResolutionPriority(-1)]
            public static IEnumerable<TResult> SelectExpr<TIn, TResult>(this IEnumerable<TIn> query, Func<TIn, object> selector)
                where TIn : class
            {
                throw new NotImplementedException();
            }
        }

        {{OverloadResolutionPriorityAttribute}}
        """;

    [StringSyntax("csharp")]
    private const string OverloadResolutionPriorityAttribute = $$"""
        namespace System.Runtime.CompilerServices
        {
            file sealed class OverloadResolutionPriorityAttribute(int priority) : global::System.Attribute
            {
                public int Priority { get; } = priority;
            }
        }
        """;

    [StringSyntax("csharp")]
    private const string LinqraftAccessibilityAttribute = $$"""
        {{CommonHeader}}
        using System;

        namespace Linqraft
        {
            /// <summary>
            /// Specifies the accessibility level for a property in a generated DTO.
            /// This attribute provides an alternative to predefining properties with specific accessibility modifiers.
            /// </summary>
            /// <remarks>
            /// Usage example:
            /// <code>
            /// public partial class SampleDto
            /// {
            ///     [LinqraftAccessibility("internal")]
            ///     public string InternalProperty { get; set; }
            /// }
            /// </code>
            /// Valid accessibility values: "public", "internal", "protected", "protected internal", "private protected", "private"
            /// </remarks>
            [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
            public sealed class LinqraftAccessibilityAttribute : Attribute
            {
                /// <summary>
                /// Gets the accessibility level for the property.
                /// </summary>
                public string Accessibility { get; }

                /// <summary>
                /// Initializes a new instance of the LinqraftAccessibilityAttribute class.
                /// </summary>
                /// <param name="accessibility">The accessibility level (e.g., "public", "internal", "protected internal")</param>
                public LinqraftAccessibilityAttribute(string accessibility)
                {
                    Accessibility = accessibility;
                }
            }
        }
        """;

    private const string CommonHeader = """
        // <auto-generated />
        #nullable enable
        #pragma warning disable
        """;
}
