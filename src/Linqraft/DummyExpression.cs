using System;
using System.Linq;

namespace Linqraft
{
    /// <summary>
    /// Dummy expression methods for Linqraft to compile correctly.
    /// </summary>
    public static class DummyExpression
    {
        /// <summary>
        /// Dummy Select expression method for Linqraft to compile correctly.
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
        /// Dummy Select expression method for Linqraft to compile correctly.
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
