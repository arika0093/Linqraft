using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    Linqraft.Analyzer.SelectExprToTypedAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier
>;

namespace Linqraft.Analyzer.Tests;

public class SelectExprToTypedAnalyzerTests
{
    [Fact]
    public async Task SelectExpr_WithoutTypeArgs_ReportsDiagnostic()
    {
        var test =
            @"
using System.Linq;
using System.Collections.Generic;

class Sample
{
    public int Id { get; set; }
    public string Name { get; set; }
}

class Test
{
    void Method()
    {
        var list = new List<Sample>();
        var result = list.AsQueryable().{|#0:SelectExpr|}(x => new { x.Id, x.Name });
    }
}

static class Extensions
{
    public static IQueryable<TResult> SelectExpr<TSource, TResult>(
        this IQueryable<TSource> source,
        System.Linq.Expressions.Expression<System.Func<TSource, TResult>> selector)
        => source.Select(selector);
}";

        var expected = VerifyCS
            .Diagnostic(SelectExprToTypedAnalyzer.DiagnosticId)
            .WithLocation(0)
            .WithSeverity(DiagnosticSeverity.Info)
            .WithArguments("Sample", "ResultDto_T27C3JAA");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task SelectExpr_WithTypeArgs_NoDiagnostic()
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
    public async Task SelectExpr_WithoutAnonymousType_NoDiagnostic()
    {
        var test =
            @"
using System.Linq;
using System.Collections.Generic;

class Sample
{
    public int Id { get; set; }
}

class Test
{
    void Method()
    {
        var list = new List<Sample>();
        var result = list.AsQueryable().SelectExpr(x => x.Id);
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
    public async Task SelectExpr_InVariableDeclaration_UsesVariableName()
    {
        var test =
            @"
using System.Linq;
using System.Collections.Generic;

class Sample
{
    public int Id { get; set; }
}

class Test
{
    void Method()
    {
        var list = new List<Sample>();
        var users = list.AsQueryable().{|#0:SelectExpr|}(x => new { x.Id });
    }
}

static class Extensions
{
    public static IQueryable<TResult> SelectExpr<TSource, TResult>(
        this IQueryable<TSource> source,
        System.Linq.Expressions.Expression<System.Func<TSource, TResult>> selector)
        => source.Select(selector);
}";

        var expected = VerifyCS
            .Diagnostic(SelectExprToTypedAnalyzer.DiagnosticId)
            .WithLocation(0)
            .WithSeverity(DiagnosticSeverity.Info)
            .WithArguments("Sample", "UsersDto_REIXTLBA");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task SelectExpr_InMethodWithGetPrefix_UsesMethodName()
    {
        var test =
            @"
using System.Linq;
using System.Collections.Generic;

class Sample
{
    public int Id { get; set; }
}

class Test
{
    object GetUsers()
    {
        var list = new List<Sample>();
        return list.AsQueryable().{|#0:SelectExpr|}(x => new { x.Id });
    }
}

static class Extensions
{
    public static IQueryable<TResult> SelectExpr<TSource, TResult>(
        this IQueryable<TSource> source,
        System.Linq.Expressions.Expression<System.Func<TSource, TResult>> selector)
        => source.Select(selector);
}";

        var expected = VerifyCS
            .Diagnostic(SelectExprToTypedAnalyzer.DiagnosticId)
            .WithLocation(0)
            .WithSeverity(DiagnosticSeverity.Info)
            .WithArguments("Sample", "UsersDto_REIXTLBA");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }
}

public class SelectExprToTypedCodeFixProviderTests
{
    [Fact]
    public async Task CodeFix_SelectExprWithoutTypeArgs_AddsTypeArgs()
    {
        var test =
            @"
using System.Linq;
using System.Collections.Generic;

class Sample
{
    public int Id { get; set; }
    public string Name { get; set; }
}

class Test
{
    void Method()
    {
        var list = new List<Sample>();
        var result = list.AsQueryable().{|#0:SelectExpr|}(x => new { x.Id, x.Name });
    }
}

static class Extensions
{
    public static IQueryable<TResult> SelectExpr<TSource, TResult>(
        this IQueryable<TSource> source,
        System.Linq.Expressions.Expression<System.Func<TSource, TResult>> selector)
        => source.Select(selector);
}";

        var fixedCode =
            @"
using System.Linq;
using System.Collections.Generic;

class Sample
{
    public int Id { get; set; }
    public string Name { get; set; }
}

class Test
{
    void Method()
    {
        var list = new List<Sample>();
        var result = list.AsQueryable().SelectExpr<Sample, ResultDto_T27C3JAA>(x => new { x.Id, x.Name });
    }
}

static class Extensions
{
    public static IQueryable<TResult> SelectExpr<TSource, TResult>(
        this IQueryable<TSource> source,
        System.Linq.Expressions.Expression<System.Func<TSource, TResult>> selector)
        => source.Select(selector);
}";

        var expected = new DiagnosticResult(
            SelectExprToTypedAnalyzer.DiagnosticId,
            DiagnosticSeverity.Info
        )
            .WithLocation(0)
            .WithArguments("Sample", "ResultDto_T27C3JAA");

        await RunCodeFixTestAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task CodeFix_SelectExprInVariableDeclaration_UsesVariableName()
    {
        var test =
            @"
using System.Linq;
using System.Collections.Generic;

class Sample
{
    public int Id { get; set; }
}

class Test
{
    void Method()
    {
        var list = new List<Sample>();
        var users = list.AsQueryable().{|#0:SelectExpr|}(x => new { x.Id });
    }
}

static class Extensions
{
    public static IQueryable<TResult> SelectExpr<TSource, TResult>(
        this IQueryable<TSource> source,
        System.Linq.Expressions.Expression<System.Func<TSource, TResult>> selector)
        => source.Select(selector);
}";

        var fixedCode =
            @"
using System.Linq;
using System.Collections.Generic;

class Sample
{
    public int Id { get; set; }
}

class Test
{
    void Method()
    {
        var list = new List<Sample>();
        var users = list.AsQueryable().SelectExpr<Sample, UsersDto_REIXTLBA>(x => new { x.Id });
    }
}

static class Extensions
{
    public static IQueryable<TResult> SelectExpr<TSource, TResult>(
        this IQueryable<TSource> source,
        System.Linq.Expressions.Expression<System.Func<TSource, TResult>> selector)
        => source.Select(selector);
}";

        var expected = new DiagnosticResult(
            SelectExprToTypedAnalyzer.DiagnosticId,
            DiagnosticSeverity.Info
        )
            .WithLocation(0)
            .WithArguments("Sample", "UsersDto_REIXTLBA");

        await RunCodeFixTestAsync(test, expected, fixedCode);
    }

    private static async Task RunCodeFixTestAsync(
        string source,
        DiagnosticResult expected,
        string fixedSource
    )
    {
        var test = new CSharpCodeFixTest<
            SelectExprToTypedAnalyzer,
            SelectExprToTypedCodeFixProvider,
            DefaultVerifier
        >
        {
            TestCode = source,
            FixedCode = fixedSource,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
            // Allow compiler errors for undefined DTO types (they will be generated later)
            CompilerDiagnostics = CompilerDiagnostics.None,
        };

        test.ExpectedDiagnostics.Add(expected);

        await test.RunAsync();
    }
}
