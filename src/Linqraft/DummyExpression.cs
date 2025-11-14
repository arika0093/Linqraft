using System;
using System.Collections.Generic;
using System.Linq;

namespace Linqraft
{
    /// <summary>
    /// Dummy expression methods for Linqraft to compile correctly.
    /// </summary>
    /// <remarks>
    /// These methods are placeholder declarations that are intercepted at compile-time by source generators.
    /// The actual implementation is generated and can be found in the generated files.
    /// <para>
    /// To view the generated implementation:
    /// <list type="bullet">
    /// <item>Use "Go to Implementation" (Ctrl+F12 in Visual Studio) instead of "Go to Definition" (F12)</item>
    /// <item>Open SelectExprNavigationHelper.g.cs for an index of all SelectExpr usages and their generated methods</item>
    /// <item>Check the generated files in: obj/Debug/[TargetFramework]/generated/Linqraft.SourceGenerator/</item>
    /// <item>Look for GeneratedExpression_[Namespace].g.cs files</item>
    /// <item>Enable EmitCompilerGeneratedFiles in your .csproj to see generated files in a .generated folder</item>
    /// </list>
    /// </para>
    /// </remarks>
    public static class DummyExpression
    {
        /// <summary>
        /// Create select expression method, usable nullable operators, and generate instance DTOs.
        /// </summary>
        /// <remarks>
        /// This is a marker method that is intercepted by a source generator.
        /// The actual implementation is generated at compile-time.
        /// To view the generated code, use "Go to Implementation" (Ctrl+F12) or check SelectExprNavigationHelper.g.cs.
        /// </remarks>
        public static IQueryable<TResult> SelectExpr<T, TResult>(
            this IQueryable<T> query,
            Func<T, TResult> selector
        )
            where T : class
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Create select expression method, usable nullable operators, and generate instance DTOs.
        /// </summary>
        /// <remarks>
        /// This is a marker method that is intercepted by a source generator.
        /// The actual implementation is generated at compile-time.
        /// To view the generated code, use "Go to Implementation" (Ctrl+F12) or check SelectExprNavigationHelper.g.cs.
        /// </remarks>
        [global::System.Runtime.CompilerServices.OverloadResolutionPriority(-1)]
        public static IQueryable<TResult> SelectExpr<T, TResult>(
            this IQueryable<T> query,
            Func<T, object> selector
        )
            where T : class
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Create select expression method, with generate instance DTOs.
        /// Works with IEnumerable where nullable operators are supported natively.
        /// </summary>
        /// <remarks>
        /// This is a marker method that is intercepted by a source generator.
        /// The actual implementation is generated at compile-time.
        /// To view the generated code, use "Go to Implementation" (Ctrl+F12) or check SelectExprNavigationHelper.g.cs.
        /// </remarks>
        public static IEnumerable<TResult> SelectExpr<T, TResult>(
            this IEnumerable<T> query,
            Func<T, TResult> selector
        )
            where T : class
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Create select expression method, with generate instance DTOs.
        /// Works with IEnumerable where nullable operators are supported natively.
        /// </summary>
        /// <remarks>
        /// This is a marker method that is intercepted by a source generator.
        /// The actual implementation is generated at compile-time.
        /// To view the generated code, use "Go to Implementation" (Ctrl+F12) or check SelectExprNavigationHelper.g.cs.
        /// </remarks>
        [global::System.Runtime.CompilerServices.OverloadResolutionPriority(-1)]
        public static IEnumerable<TResult> SelectExpr<T, TResult>(
            this IEnumerable<T> query,
            Func<T, object> selector
        )
            where T : class
        {
            throw new NotImplementedException();
        }
    }
}

#pragma warning disable
namespace System.Runtime.CompilerServices
{
    file sealed class OverloadResolutionPriorityAttribute(int priority) : global::System.Attribute
    {
        public int Priority { get; } = priority;
    }
}
