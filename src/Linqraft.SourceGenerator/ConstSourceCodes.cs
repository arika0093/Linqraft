using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;

internal static class ConstSourceCodes
{
    // Export all source codes
    public static void ExportAll(IncrementalGeneratorPostInitializationContext context)
    {
        context.AddSource("InterceptsLocationAttribute.g.cs", InterceptsLocationAttribute);
        context.AddSource("SelectExprExtensions.g.cs", SelectExprExtensions);
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
    private const string SelectExprExtensions = $$""""
        {{CommonHeader}}

        using System;
        using System.Collections.Generic;
        using System.Linq;

        /// <summary>
        /// Dummy expression methods for Linqraft to compile correctly.
        /// </summary>
        internal static class SelectExprExtensions
        {
            /// <summary>
            /// Create select expression method, usable nullable operators, and generate instance DTOs.
            /// </summary>
            public static IQueryable<TResult> SelectExpr<TIn, TResult>(this IQueryable<TIn> query, Func<TIn, TResult> selector)
                where TIn : class => throw InvalidException;

            /// <summary>
            /// Create select expression method, usable nullable operators, and generate instance DTOs.
            /// </summary>
            [global::System.Runtime.CompilerServices.OverloadResolutionPriority(-1)]
            public static IQueryable<TResult> SelectExpr<TIn, TResult>(this IQueryable<TIn> query, Func<TIn, object> selector)
                where TIn : class => throw InvalidException;

            /// <summary>
            /// Create select expression method, with generate instance DTOs.
            /// Works with IEnumerable where nullable operators are supported natively.
            /// </summary>
            public static IEnumerable<TResult> SelectExpr<TIn, TResult>(this IEnumerable<TIn> query, Func<TIn, TResult> selector)
                where TIn : class => throw InvalidException;

            /// <summary>
            /// Create select expression method, with generate instance DTOs.
            /// Works with IEnumerable where nullable operators are supported natively.
            /// </summary>
            [global::System.Runtime.CompilerServices.OverloadResolutionPriority(-1)]
            public static IEnumerable<TResult> SelectExpr<TIn, TResult>(this IEnumerable<TIn> query, Func<TIn, object> selector)
                where TIn : class => throw InvalidException;

            private static Exception InvalidException
            {
                get => new System.InvalidOperationException("""
        {{SelectExprErrorMessage}}
        """); 
            }

            /// <summary>
            /// Create select expression method with captured local variables, usable nullable operators, and generate instance DTOs.
            /// Pass local variables as an anonymous object: new { var1, var2, ... }
            /// </summary>
            public static IQueryable<TResult> SelectExpr<TIn, TResult>(this IQueryable<TIn> query, Func<TIn, TResult> selector, object capture)
                where TIn : class
            {
                throw new NotImplementedException();
            }

            /// <summary>
            /// Create select expression method with captured local variables, usable nullable operators, and generate instance DTOs.
            /// Pass local variables as an anonymous object: new { var1, var2, ... }
            /// </summary>
            [global::System.Runtime.CompilerServices.OverloadResolutionPriority(-1)]
            public static IQueryable<TResult> SelectExpr<TIn, TResult>(this IQueryable<TIn> query, Func<TIn, object> selector, object capture)
                where TIn : class
            {
                throw new NotImplementedException();
            }

            /// <summary>
            /// Create select expression method with captured local variables, with generate instance DTOs.
            /// Works with IEnumerable where nullable operators are supported natively.
            /// Pass local variables as an anonymous object: new { var1, var2, ... }
            /// </summary>
            public static IEnumerable<TResult> SelectExpr<TIn, TResult>(this IEnumerable<TIn> query, Func<TIn, TResult> selector, object capture)
                where TIn : class
            {
                throw new NotImplementedException();
            }

            /// <summary>
            /// Create select expression method with captured local variables, with generate instance DTOs.
            /// Works with IEnumerable where nullable operators are supported natively.
            /// Pass local variables as an anonymous object: new { var1, var2, ... }
            /// </summary>
            [global::System.Runtime.CompilerServices.OverloadResolutionPriority(-1)]
            public static IEnumerable<TResult> SelectExpr<TIn, TResult>(this IEnumerable<TIn> query, Func<TIn, object> selector, object capture)
                where TIn : class
            {
                throw new NotImplementedException();
            }
        }

        {{OverloadResolutionPriorityAttribute}}
        """";

    private const string SelectExprErrorMessage = """
        This is a dummy method for Linqraft source generator and should not be called directly.
        If you see this message, it may be due to one of the following reasons:
        (1) You are calling SelectExpr in a razor file. Linqraft source generator does not work in razor files due to Source Generator limitations.
            Please separate the method into a razor.cs file or use Linqraft in a regular .cs file.
        (2) The Linqraft source generator is not functioning correctly. If this is the case, it is likely due to a bug.
            Please contact us via the Linqraft Issue page.
            https://github.com/arika0093/Linqraft/issues
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

    private const string CommonHeader = """
        // <auto-generated />
        #nullable enable
        """;
}
