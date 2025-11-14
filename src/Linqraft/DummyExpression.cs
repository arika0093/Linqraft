using System;
using System.Collections.Generic;
using System.Linq;

namespace Linqraft
{
    /// <summary>
    /// Dummy expression methods for Linqraft to compile correctly.
    /// </summary>
    public static class DummyExpression
    {
        /// <summary>
        /// Create select expression method, usable nullable operators, and generate instance DTOs.
        /// </summary>
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
