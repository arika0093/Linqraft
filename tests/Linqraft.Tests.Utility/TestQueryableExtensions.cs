using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace Linqraft.Tests.Utility;

public static class TestQueryableExtensions
{
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = "Test-only LINQ-to-Objects query sources are used to exercise generated IQueryable projections."
    )]
    [UnconditionalSuppressMessage(
        "AOT",
        "IL3050",
        Justification = "Test-only LINQ-to-Objects query sources are used to exercise generated IQueryable projections."
    )]
    public static IQueryable<T> AsTestQueryable<T>(this IEnumerable<T> source) =>
        source.AsQueryable();
}
