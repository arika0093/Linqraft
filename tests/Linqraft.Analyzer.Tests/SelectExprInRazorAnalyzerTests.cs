using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    Linqraft.Analyzer.SelectExprInRazorAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier
>;

namespace Linqraft.Analyzer.Tests;

public class SelectExprInRazorAnalyzerTests
{
    [Fact]
    public async Task SelectExpr_InRegularCsFile_NoDiagnostic()
    {
        var test =
            @"
using System.Linq;
using System.Collections.Generic;

class Test
{
    void Method()
    {
        var list = new List<int> { 1, 2, 3 };
        var result = list.AsQueryable().SelectExpr(x => new { Value = x });
    }
}

static class Extensions
{
    public static IQueryable<TResult> SelectExpr<TSource, TResult>(
        this IQueryable<TSource> source,
        System.Linq.Expressions.Expression<System.Func<TSource, TResult>> selector)
        => source.Select(selector);
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task SelectExpr_WithTypeArguments_InRegularCsFile_NoDiagnostic()
    {
        var test =
            @"
using System.Linq;
using System.Collections.Generic;

class Sample
{
    public int Id { get; set; }
}

class SampleDto
{
    public int Id { get; set; }
}

class Test
{
    void Method()
    {
        var list = new List<Sample>();
        var result = list.AsQueryable().SelectExpr<Sample, SampleDto>(x => new { x.Id });
    }
}

static class Extensions
{
    public static IQueryable<TResult> SelectExpr<TSource, TResult>(
        this IQueryable<TSource> source,
        System.Linq.Expressions.Expression<System.Func<TSource, object>> selector)
        => throw new System.NotImplementedException();
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task SelectExpr_MultipleOccurrences_InRegularCsFile_NoDiagnostic()
    {
        var test =
            @"
using System.Linq;
using System.Collections.Generic;

class Test
{
    void Method()
    {
        var list = new List<int> { 1, 2, 3 };
        var result1 = list.AsQueryable().SelectExpr(x => new { Value = x });
        var result2 = list.AsQueryable().SelectExpr(x => new { DoubleValue = x * 2 });
    }
}

static class Extensions
{
    public static IQueryable<TResult> SelectExpr<TSource, TResult>(
        this IQueryable<TSource> source,
        System.Linq.Expressions.Expression<System.Func<TSource, TResult>> selector)
        => source.Select(selector);
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task OtherMethod_InRegularCsFile_NoDiagnostic()
    {
        var test =
            @"
using System.Linq;
using System.Collections.Generic;

class Test
{
    void Method()
    {
        var list = new List<int> { 1, 2, 3 };
        var result = list.Select(x => new { Value = x });
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Theory]
    [InlineData("test.razor", true)]
    [InlineData("test.cshtml", true)]
    [InlineData("Test.Razor", true)]
    [InlineData("Test.CSHTML", true)]
    [InlineData("test.cs", false)]
    [InlineData("test.txt", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsInRazorFile_VariousExtensions_ReturnsCorrectResult(
        string filePath,
        bool expectedResult
    )
    {
        // Use reflection to call the private static method
        var method = typeof(SelectExprInRazorAnalyzer).GetMethod(
            "IsInRazorFile",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static
        );

        Assert.NotNull(method);

        // Create a mock SyntaxTree with the specified file path
        var code = "class Test { }";
        var tree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(
            code,
            path: filePath
        );

        var result = (bool)method!.Invoke(null, new object[] { tree })!;

        Assert.Equal(expectedResult, result);
    }
}

