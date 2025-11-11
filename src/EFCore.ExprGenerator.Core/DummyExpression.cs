using System;
using System.Linq;

/// <summary>
/// Dummy expression methods for EFCore.ExprGenerator to compile correctly.
/// </summary>
public static class DummyExpression
{
    /// <summary>
    /// Dummy Select expression method for EFCore.ExprGenerator to compile correctly.
    /// </summary>
    public static IQueryable<TResult> SelectExpr<T, TResult>(
        this IQueryable<T> query,
        Func<T, TResult> selector
    )
        where T : class
    {
        throw new NotImplementedException();
    }
}
