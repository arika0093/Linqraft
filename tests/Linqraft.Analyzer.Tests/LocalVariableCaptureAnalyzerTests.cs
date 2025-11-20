using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    Linqraft.Analyzer.LocalVariableCaptureAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier
>;

namespace Linqraft.Analyzer.Tests;

public class LocalVariableCaptureAnalyzerTests
{
    [Fact]
    public async Task LocalVariable_InSelectExpr_ReportsDiagnostic()
    {
        var test =
            @"
using System.Linq;
using System.Collections.Generic;

class Test
{
    void Method()
    {
        var localVar = 10;
        var list = new List<int> { 1, 2, 3 };
        var result = list.AsQueryable().SelectExpr(s => new { Value = s + {|#0:localVar|} });
    }
}

static class Extensions
{
    public static IQueryable<TResult> SelectExpr<TSource, TResult>(
        this IQueryable<TSource> source,
        System.Func<TSource, TResult> selector)
        => source.Select(x => selector(x));
}";

        var expected = VerifyCS
            .Diagnostic(LocalVariableCaptureAnalyzer.DiagnosticId)
            .WithLocation(0)
            .WithArguments("localVar")
            .WithSeverity(DiagnosticSeverity.Error);

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task MultipleLocalVariables_InSelectExpr_ReportsMultipleDiagnostics()
    {
        var test =
            @"
using System.Linq;
using System.Collections.Generic;

class Test
{
    void Method()
    {
        var localVar1 = 10;
        var localVar2 = 20;
        var list = new List<int> { 1, 2, 3 };
        var result = list.AsQueryable().SelectExpr(s => new { 
            Value1 = s + {|#0:localVar1|},
            Value2 = s + {|#1:localVar2|}
        });
    }
}

static class Extensions
{
    public static IQueryable<TResult> SelectExpr<TSource, TResult>(
        this IQueryable<TSource> source,
        System.Func<TSource, TResult> selector)
        => source.Select(x => selector(x));
}";

        var expected1 = VerifyCS
            .Diagnostic(LocalVariableCaptureAnalyzer.DiagnosticId)
            .WithLocation(0)
            .WithArguments("localVar1")
            .WithSeverity(DiagnosticSeverity.Error);

        var expected2 = VerifyCS
            .Diagnostic(LocalVariableCaptureAnalyzer.DiagnosticId)
            .WithLocation(1)
            .WithArguments("localVar2")
            .WithSeverity(DiagnosticSeverity.Error);

        await VerifyCS.VerifyAnalyzerAsync(test, expected1, expected2);
    }

    [Fact]
    public async Task LocalVariable_WithCaptureParameter_NoDiagnostic()
    {
        var test =
            @"
using System.Linq;
using System.Collections.Generic;

class Test
{
    void Method()
    {
        var localVar = 10;
        var list = new List<int> { 1, 2, 3 };
        var result = list.AsQueryable().SelectExpr(s => new { Value = s + localVar }, capture: new { localVar });
    }
}

static class Extensions
{
    public static IQueryable<TResult> SelectExpr<TSource, TResult>(
        this IQueryable<TSource> source,
        System.Func<TSource, TResult> selector,
        object capture)
        => source.Select(x => selector(x));
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ParameterFromOuterScope_InSelectExpr_ReportsDiagnostic()
    {
        var test =
            @"
using System.Linq;
using System.Collections.Generic;

class Test
{
    void Method(int outerParam)
    {
        var list = new List<int> { 1, 2, 3 };
        var result = list.AsQueryable().SelectExpr(s => new { Value = s + {|#0:outerParam|} });
    }
}

static class Extensions
{
    public static IQueryable<TResult> SelectExpr<TSource, TResult>(
        this IQueryable<TSource> source,
        System.Func<TSource, TResult> selector)
        => source.Select(x => selector(x));
}";

        var expected = VerifyCS
            .Diagnostic(LocalVariableCaptureAnalyzer.DiagnosticId)
            .WithLocation(0)
            .WithArguments("outerParam")
            .WithSeverity(DiagnosticSeverity.Error);

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task LambdaParameter_NoDiagnostic()
    {
        var test =
            @"
using System.Linq;
using System.Collections.Generic;

class Entity
{
    public int Id { get; set; }
    public string Name { get; set; }
}

class Test
{
    void Method()
    {
        var list = new List<Entity>();
        var result = list.AsQueryable().SelectExpr(s => new { s.Id, s.Name });
    }
}

static class Extensions
{
    public static IQueryable<TResult> SelectExpr<TSource, TResult>(
        this IQueryable<TSource> source,
        System.Func<TSource, TResult> selector)
        => source.Select(x => selector(x));
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task MemberAccess_NoDiagnostic()
    {
        var test =
            @"
using System.Linq;
using System.Collections.Generic;

class Entity
{
    public int Value { get; set; }
}

class Test
{
    void Method()
    {
        var list = new List<Entity>();
        var result = list.AsQueryable().SelectExpr(s => new { Value = s.Value * 2 });
    }
}

static class Extensions
{
    public static IQueryable<TResult> SelectExpr<TSource, TResult>(
        this IQueryable<TSource> source,
        System.Func<TSource, TResult> selector)
        => source.Select(x => selector(x));
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task LocalVariable_InComplexExpression_ReportsDiagnostic()
    {
        var test =
            @"
using System.Linq;
using System.Collections.Generic;

class Entity
{
    public int Value { get; set; }
}

class Test
{
    void Method()
    {
        var multiplier = 10;
        var offset = 5;
        var list = new List<Entity>();
        var result = list.AsQueryable().SelectExpr(s => new { 
            Result = (s.Value * {|#0:multiplier|}) + {|#1:offset|}
        });
    }
}

static class Extensions
{
    public static IQueryable<TResult> SelectExpr<TSource, TResult>(
        this IQueryable<TSource> source,
        System.Func<TSource, TResult> selector)
        => source.Select(x => selector(x));
}";

        var expected1 = VerifyCS
            .Diagnostic(LocalVariableCaptureAnalyzer.DiagnosticId)
            .WithLocation(0)
            .WithArguments("multiplier")
            .WithSeverity(DiagnosticSeverity.Error);

        var expected2 = VerifyCS
            .Diagnostic(LocalVariableCaptureAnalyzer.DiagnosticId)
            .WithLocation(1)
            .WithArguments("offset")
            .WithSeverity(DiagnosticSeverity.Error);

        await VerifyCS.VerifyAnalyzerAsync(test, expected1, expected2);
    }

    [Fact]
    public async Task LocalVariable_WithTypedSelectExpr_ReportsDiagnostic()
    {
        var test =
            @"
using System.Linq;
using System.Collections.Generic;

class Entity
{
    public int Value { get; set; }
}

class ResultDto
{
    public int Result { get; set; }
}

class Test
{
    void Method()
    {
        var multiplier = 10;
        var list = new List<Entity>();
        var result = list.AsQueryable().SelectExpr<Entity, ResultDto>(s => new { 
            Result = s.Value * {|#0:multiplier|}
        });
    }
}

static class Extensions
{
    public static IQueryable<TResult> SelectExpr<TSource, TResult>(
        this IQueryable<TSource> source,
        System.Func<TSource, object> selector)
        => throw new System.NotImplementedException();
}";

        var expected = VerifyCS
            .Diagnostic(LocalVariableCaptureAnalyzer.DiagnosticId)
            .WithLocation(0)
            .WithArguments("multiplier")
            .WithSeverity(DiagnosticSeverity.Error);

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task LocalVariable_WithIEnumerable_ReportsDiagnostic()
    {
        var test =
            @"
using System.Linq;
using System.Collections.Generic;

class Entity
{
    public int Value { get; set; }
}

class Test
{
    void Method()
    {
        var multiplier = 10;
        var list = new List<Entity>();
        var result = list.SelectExpr(s => new { Result = s.Value * {|#0:multiplier|} });
    }
}

static class Extensions
{
    public static IEnumerable<TResult> SelectExpr<TSource, TResult>(
        this IEnumerable<TSource> source,
        System.Func<TSource, TResult> selector)
        => source.Select(x => selector(x));
}";

        var expected = VerifyCS
            .Diagnostic(LocalVariableCaptureAnalyzer.DiagnosticId)
            .WithLocation(0)
            .WithArguments("multiplier")
            .WithSeverity(DiagnosticSeverity.Error);

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task LocalVariable_ReusedInMultiplePlaces_ReportsMultipleDiagnostics()
    {
        var test =
            @"
using System.Linq;
using System.Collections.Generic;

class Entity
{
    public int Value1 { get; set; }
    public int Value2 { get; set; }
}

class Test
{
    void Method()
    {
        var multiplier = 10;
        var list = new List<Entity>();
        var result = list.AsQueryable().SelectExpr(s => new { 
            Result1 = s.Value1 * {|#0:multiplier|},
            Result2 = s.Value2 * {|#1:multiplier|}
        });
    }
}

static class Extensions
{
    public static IQueryable<TResult> SelectExpr<TSource, TResult>(
        this IQueryable<TSource> source,
        System.Func<TSource, TResult> selector)
        => source.Select(x => selector(x));
}";

        var expected1 = VerifyCS
            .Diagnostic(LocalVariableCaptureAnalyzer.DiagnosticId)
            .WithLocation(0)
            .WithArguments("multiplier")
            .WithSeverity(DiagnosticSeverity.Error);

        var expected2 = VerifyCS
            .Diagnostic(LocalVariableCaptureAnalyzer.DiagnosticId)
            .WithLocation(1)
            .WithArguments("multiplier")
            .WithSeverity(DiagnosticSeverity.Error);

        await VerifyCS.VerifyAnalyzerAsync(test, expected1, expected2);
    }

    [Fact]
    public async Task LocalVariable_InNestedLambda_ReportsDiagnostic()
    {
        var test =
            @"
using System.Linq;
using System.Collections.Generic;

class Entity
{
    public List<int> Values { get; set; }
}

class Test
{
    void Method()
    {
        var multiplier = 10;
        var list = new List<Entity>();
        var result = list.AsQueryable().SelectExpr(s => new { 
            Values = s.Values.Select(v => v * {|#0:multiplier|}).ToList()
        });
    }
}

static class Extensions
{
    public static IQueryable<TResult> SelectExpr<TSource, TResult>(
        this IQueryable<TSource> source,
        System.Func<TSource, TResult> selector)
        => source.Select(x => selector(x));
}";

        var expected = VerifyCS
            .Diagnostic(LocalVariableCaptureAnalyzer.DiagnosticId)
            .WithLocation(0)
            .WithArguments("multiplier")
            .WithSeverity(DiagnosticSeverity.Error);

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task NoLocalVariables_NoDiagnostic()
    {
        var test =
            @"
using System.Linq;
using System.Collections.Generic;

class Entity
{
    public int Value { get; set; }
}

class Test
{
    void Method()
    {
        var list = new List<Entity>();
        var result = list.AsQueryable().SelectExpr(s => new { Result = s.Value * 10 });
    }
}

static class Extensions
{
    public static IQueryable<TResult> SelectExpr<TSource, TResult>(
        this IQueryable<TSource> source,
        System.Func<TSource, TResult> selector)
        => source.Select(x => selector(x));
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ConstantValue_NoDiagnostic()
    {
        var test =
            @"
using System.Linq;
using System.Collections.Generic;

class Entity
{
    public int Value { get; set; }
}

class Test
{
    void Method()
    {
        const int multiplier = 10;
        var list = new List<Entity>();
        var result = list.AsQueryable().SelectExpr(s => new { Result = s.Value * multiplier });
    }
}

static class Extensions
{
    public static IQueryable<TResult> SelectExpr<TSource, TResult>(
        this IQueryable<TSource> source,
        System.Func<TSource, TResult> selector)
        => source.Select(x => selector(x));
}";

        // Note: Constants might still be flagged depending on how Roslyn treats them
        // This test documents the current behavior
        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
