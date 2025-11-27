using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Linqraft.Core.Formatting;
using Microsoft.CodeAnalysis;

namespace Linqraft.Core;

public static class GenerateSourceCodeSnippets
{
    // Export all source codes
    public static void ExportAll(IncrementalGeneratorPostInitializationContext context)
    {
        context.AddSource("InterceptsLocationAttribute.g.cs", InterceptsLocationAttribute);
        context.AddSource("SelectExprExtensions.g.cs", SelectExprExtensions);
    }

    // Generate total code
    public static string BuildCodeSnippetAll(
        List<string> expressions,
        List<string> dtoClasses,
        string dtoNamespace
    )
    {
        var exprPart = BuildExprCodeSnippets(expressions);
        var dtoPart = BuildDtoCodeSnippets(dtoClasses, dtoNamespace);
        return $$"""
            {{GenerateCommentHeaderPart()}}
            {{GenerateHeaderFlagsPart()}}
            {{exprPart}}
            {{dtoPart}}
            """;
    }

    // Generate expression part
    public static string BuildExprCodeSnippets(List<string> expressions)
    {
        var indentedExpr = CodeFormatter.IndentCode(
            string.Join(CodeFormatter.DefaultNewLine, expressions),
            CodeFormatter.IndentSize * 2
        );
        return $$"""
            {{GenerateHeaderUsingPart()}}
            namespace Linqraft
            {
                file static partial class GeneratedExpression
                {
            {{indentedExpr}}
                }
            }
            """;
    }

    // Generate DTO part
    public static string BuildDtoCodeSnippets(List<string> dtoClasses, string namespaceName)
    {
        if (string.IsNullOrEmpty(namespaceName))
        {
            // Generate DTOs in global namespace (no namespace wrapper)
            return string.Join(CodeFormatter.DefaultNewLine, dtoClasses);
        }
        else
        {
            // Generate DTOs in the specified namespace
            var indentedClasses = CodeFormatter.IndentCode(
                string.Join(CodeFormatter.DefaultNewLine, dtoClasses),
                CodeFormatter.IndentSize
            );
            return $$"""
                namespace {{namespaceName}}
                {
                {{indentedClasses}}
                }
                """;
        }
    }

    [StringSyntax("csharp")]
    public const string InterceptsLocationAttribute = $$"""
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
    public const string SelectExprExtensions = $$""""
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
            public static IEnumerable<TResult> SelectExpr<TIn, TResult>(this IEnumerable<TIn> query, Func<TIn, object> selector)
                where TIn : class => throw InvalidException;

            /// <summary>
            /// Create select expression method with captured local variables, usable nullable operators, and generate instance DTOs.
            /// Pass local variables as an anonymous object: new { var1, var2, ... }
            /// </summary>
            public static IQueryable<TResult> SelectExpr<TIn, TResult>(this IQueryable<TIn> query, Func<TIn, TResult> selector, object capture)
                where TIn : class => throw InvalidException;

            /// <summary>
            /// Create select expression method with captured local variables, usable nullable operators, and generate instance DTOs.
            /// Pass local variables as an anonymous object: new { var1, var2, ... }
            /// </summary>
            public static IQueryable<TResult> SelectExpr<TIn, TResult>(this IQueryable<TIn> query, Func<TIn, object> selector, object capture)
                where TIn : class => throw InvalidException;

            /// <summary>
            /// Create select expression method with captured local variables, with generate instance DTOs.
            /// Works with IEnumerable where nullable operators are supported natively.
            /// Pass local variables as an anonymous object: new { var1, var2, ... }
            /// </summary>
            public static IEnumerable<TResult> SelectExpr<TIn, TResult>(this IEnumerable<TIn> query, Func<TIn, TResult> selector, object capture)
                where TIn : class => throw InvalidException;

            /// <summary>
            /// Create select expression method with captured local variables, with generate instance DTOs.
            /// Works with IEnumerable where nullable operators are supported natively.
            /// Pass local variables as an anonymous object: new { var1, var2, ... }
            /// </summary>
            public static IEnumerable<TResult> SelectExpr<TIn, TResult>(this IEnumerable<TIn> query, Func<TIn, object> selector, object capture)
                where TIn : class => throw InvalidException;

            private static Exception InvalidException
            {
                get => new System.InvalidOperationException("""
        {{SelectExprErrorMessage}}
        """); 
            }
        }
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

    private const string CommonHeader = """
        // <auto-generated />
        #nullable enable
        """;

    private static string GenerateCommentHeaderPart()
    {
#if DEBUG
        var now = DateTime.Now;
        var buildDate = BuildDateTimeAttribute.GetBuildDateTimeUtc()?.ToLocalTime();
        var buildDateString = buildDate?.ToString("yyyy-MM-dd HH:mm:ss") ?? "Unknown";
        return $"""
            // <auto-generated>
            // This file is auto-generated by Linqraft.
            //   Linqraft Version  : {ThisAssembly.AssemblyInformationalVersion}
            //   Linqraft Build at : {buildDateString}
            //   Code Generated at : {now:yyyy-MM-dd HH:mm:ss}
            // </auto-generated>
            """;
#else
        return $"""
            // <auto-generated>
            // This file is auto-generated by Linqraft (ver. {ThisAssembly.AssemblyInformationalVersion})
            // </auto-generated>
            """;
#endif
    }

    private static string GenerateHeaderFlagsPart()
    {
        return """
            #nullable enable
            #pragma warning disable IDE0060
            #pragma warning disable CS8601
            #pragma warning disable CS8602
            #pragma warning disable CS8603
            #pragma warning disable CS8604
            #pragma warning disable CS8618
            """;
    }

    private static string GenerateHeaderUsingPart()
    {
        return $"""
            using System;
            using System.Linq;
            using System.Collections.Generic;
            """;
    }
}
