namespace Linqraft.Analyzer.Tests;

/// <summary>
/// Common source code snippets for testing Linqraft analyzers
/// </summary>
internal static class TestSourceCodes
{
    /// <summary>
    /// SelectExpr extension method that accepts Func with TResult and uses Select with lambda wrapper
    /// </summary>
    public const string SelectExprWithFunc = """
        static class Extensions
        {
            public static IQueryable<TResult> SelectExpr<TSource, TResult>(
                this IQueryable<TSource> source,
                System.Func<TSource, TResult> selector)
                => source.Select(x => selector(x));
        }
        """;

    /// <summary>
    /// SelectExpr extension method in the Linqraft namespace (for tests that need to detect SelectExpr calls)
    /// </summary>
    public const string SelectExprWithFuncInLinqraftNamespace = """
        namespace Linqraft
        {
            static class Extensions
            {
                public static IQueryable<TResult> SelectExpr<TSource, TResult>(
                    this IQueryable<TSource> source,
                    System.Func<TSource, TResult> selector)
                    => source.Select(x => selector(x));
            }
        }
        """;

    /// <summary>
    /// SelectExpr extension method that accepts Func with TResult and capture parameter
    /// </summary>
    public const string SelectExprWithFuncAndCapture = """
        static class Extensions
        {
            public static IQueryable<TResult> SelectExpr<TSource, TResult>(
                this IQueryable<TSource> source,
                System.Func<TSource, TResult> selector,
                object capture)
                => source.Select(x => selector(x));
        }
        """;

    /// <summary>
    /// SelectExpr extension method that accepts Func with object return type and throws NotImplementedException
    /// </summary>
    public const string SelectExprWithFuncObject = """
        static class Extensions
        {
            public static IQueryable<TResult> SelectExpr<TSource, TResult>(
                this IQueryable<TSource> source,
                System.Func<TSource, object> selector)
                => throw new System.NotImplementedException();
        }
        """;

    /// <summary>
    /// SelectExpr extension method that accepts Expression with TResult and uses Select directly
    /// </summary>
    public const string SelectExprWithExpression = """
        static class Extensions
        {
            public static IQueryable<TResult> SelectExpr<TSource, TResult>(
                this IQueryable<TSource> source,
                System.Linq.Expressions.Expression<System.Func<TSource, TResult>> selector)
                => source.Select(selector);
        }
        """;

    /// <summary>
    /// SelectExpr extension method that accepts Expression with TResult and throws NotImplementedException
    /// </summary>
    public const string SelectExprWithExpressionNotImplemented = """
        static class Extensions
        {
            public static IQueryable<TResult> SelectExpr<TSource, TResult>(
                this IQueryable<TSource> source,
                System.Linq.Expressions.Expression<System.Func<TSource, TResult>> selector)
                => throw new System.NotImplementedException();
        }
        """;

    /// <summary>
    /// SelectExpr extension method that accepts Expression with object return type and throws NotImplementedException
    /// </summary>
    public const string SelectExprWithExpressionObject = """
        static class Extensions
        {
            public static IQueryable<TResult> SelectExpr<TSource, TResult>(
                this IQueryable<TSource> source,
                System.Linq.Expressions.Expression<System.Func<TSource, object>> selector)
                => throw new System.NotImplementedException();
        }
        """;

    /// <summary>
    /// SelectExpr extension method for IEnumerable that accepts Func with TResult
    /// </summary>
    public const string SelectExprEnumerableWithFunc = """
        static class Extensions
        {
            public static IEnumerable<TResult> SelectExpr<TSource, TResult>(
                this IEnumerable<TSource> source,
                System.Func<TSource, TResult> selector)
                => source.Select(x => selector(x));
        }
        """;

    /// <summary>
    /// SelectExpr extension method with both regular and capture overloads for code fix tests
    /// </summary>
    public const string SelectExprWithFuncAndBothOverloads = """
        static class Extensions
        {
            public static IQueryable<TResult> SelectExpr<TSource, TResult>(
                this IQueryable<TSource> source,
                System.Func<TSource, TResult> selector)
                => source.Select(x => selector(x));

            public static IQueryable<TResult> SelectExpr<TSource, TResult>(
                this IQueryable<TSource> source,
                System.Func<TSource, TResult> selector,
                object capture)
                => source.Select(x => selector(x));
        }
        """;

    /// <summary>
    /// SelectExpr extension method with Func<TSource, object> and both overloads for code fix tests
    /// </summary>
    public const string SelectExprWithFuncObjectAndBothOverloads = """
        static class Extensions
        {
            public static IQueryable<TResult> SelectExpr<TSource, TResult>(
                this IQueryable<TSource> source,
                System.Func<TSource, object> selector)
                => throw new System.NotImplementedException();

            public static IQueryable<TResult> SelectExpr<TSource, TResult>(
                this IQueryable<TSource> source,
                System.Func<TSource, object> selector,
                object capture)
                => throw new System.NotImplementedException();
        }
        """;

    /// <summary>
    /// SelectExpr extension method with only the capture overload (for certain code fix tests)
    /// </summary>
    public const string SelectExprWithFuncAndCaptureOnly = """
        static class Extensions
        {
            public static IQueryable<TResult> SelectExpr<TSource, TResult>(
                this IQueryable<TSource> source,
                System.Func<TSource, TResult> selector,
                object capture)
                => source.Select(x => selector(x));
        }
        """;
}
