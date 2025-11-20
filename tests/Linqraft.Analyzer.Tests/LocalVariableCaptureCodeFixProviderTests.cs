using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixVerifier<
    Linqraft.Analyzer.LocalVariableCaptureAnalyzer,
    Linqraft.Analyzer.LocalVariableCaptureCodeFixProvider,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier
>;

namespace Linqraft.Analyzer.Tests;

public class LocalVariableCaptureCodeFixProviderTests
{
    [Fact]
    public async Task SingleLocalVariable_AddsCapture()
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

    public static IQueryable<TResult> SelectExpr<TSource, TResult>(
        this IQueryable<TSource> source,
        System.Func<TSource, TResult> selector,
        object capture)
        => source.Select(x => selector(x));
}";

        var fixedCode =
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
        System.Func<TSource, TResult> selector)
        => source.Select(x => selector(x));

    public static IQueryable<TResult> SelectExpr<TSource, TResult>(
        this IQueryable<TSource> source,
        System.Func<TSource, TResult> selector,
        object capture)
        => source.Select(x => selector(x));
}";

        var expected = VerifyCS
            .Diagnostic(LocalVariableCaptureAnalyzer.DiagnosticId)
            .WithLocation(0)
            .WithArguments("localVar");

        await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task MultipleLocalVariables_AddsCapture()
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

    public static IQueryable<TResult> SelectExpr<TSource, TResult>(
        this IQueryable<TSource> source,
        System.Func<TSource, TResult> selector,
        object capture)
        => source.Select(x => selector(x));
}";

        var fixedCode =
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
            Value1 = s + localVar1,
            Value2 = s + localVar2
        }, capture: new { localVar1, localVar2 });
    }
}

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
}";

        var expected1 = VerifyCS
            .Diagnostic(LocalVariableCaptureAnalyzer.DiagnosticId)
            .WithLocation(0)
            .WithArguments("localVar1");

        var expected2 = VerifyCS
            .Diagnostic(LocalVariableCaptureAnalyzer.DiagnosticId)
            .WithLocation(1)
            .WithArguments("localVar2");

        await VerifyCS.VerifyCodeFixAsync(test, new[] { expected1, expected2 }, fixedCode);
    }

    [Fact]
    public async Task ParameterFromOuterScope_AddsCapture()
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

    public static IQueryable<TResult> SelectExpr<TSource, TResult>(
        this IQueryable<TSource> source,
        System.Func<TSource, TResult> selector,
        object capture)
        => source.Select(x => selector(x));
}";

        var fixedCode =
            @"
using System.Linq;
using System.Collections.Generic;

class Test
{
    void Method(int outerParam)
    {
        var list = new List<int> { 1, 2, 3 };
        var result = list.AsQueryable().SelectExpr(s => new { Value = s + outerParam }, capture: new { outerParam });
    }
}

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
}";

        var expected = VerifyCS
            .Diagnostic(LocalVariableCaptureAnalyzer.DiagnosticId)
            .WithLocation(0)
            .WithArguments("outerParam");

        await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task LocalVariable_InComplexExpression_AddsCapture()
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

    public static IQueryable<TResult> SelectExpr<TSource, TResult>(
        this IQueryable<TSource> source,
        System.Func<TSource, TResult> selector,
        object capture)
        => source.Select(x => selector(x));
}";

        var fixedCode =
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
            Result = (s.Value * multiplier) + offset
        }, capture: new { multiplier, offset });
    }
}

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
}";

        var expected1 = VerifyCS
            .Diagnostic(LocalVariableCaptureAnalyzer.DiagnosticId)
            .WithLocation(0)
            .WithArguments("multiplier");

        var expected2 = VerifyCS
            .Diagnostic(LocalVariableCaptureAnalyzer.DiagnosticId)
            .WithLocation(1)
            .WithArguments("offset");

        await VerifyCS.VerifyCodeFixAsync(test, new[] { expected1, expected2 }, fixedCode);
    }

    [Fact]
    public async Task LocalVariable_WithTypedSelectExpr_AddsCapture()
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

    public static IQueryable<TResult> SelectExpr<TSource, TResult>(
        this IQueryable<TSource> source,
        System.Func<TSource, object> selector,
        object capture)
        => throw new System.NotImplementedException();
}";

        var fixedCode =
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
            Result = s.Value * multiplier
        }, capture: new { multiplier });
    }
}

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
}";

        var expected = VerifyCS
            .Diagnostic(LocalVariableCaptureAnalyzer.DiagnosticId)
            .WithLocation(0)
            .WithArguments("multiplier");

        await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task LocalVariable_ReusedInMultiplePlaces_AddsCapture()
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

    public static IQueryable<TResult> SelectExpr<TSource, TResult>(
        this IQueryable<TSource> source,
        System.Func<TSource, TResult> selector,
        object capture)
        => source.Select(x => selector(x));
}";

        var fixedCode =
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
            Result1 = s.Value1 * multiplier,
            Result2 = s.Value2 * multiplier
        }, capture: new { multiplier });
    }
}

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
}";

        var expected1 = VerifyCS
            .Diagnostic(LocalVariableCaptureAnalyzer.DiagnosticId)
            .WithLocation(0)
            .WithArguments("multiplier");

        var expected2 = VerifyCS
            .Diagnostic(LocalVariableCaptureAnalyzer.DiagnosticId)
            .WithLocation(1)
            .WithArguments("multiplier");

        await VerifyCS.VerifyCodeFixAsync(test, new[] { expected1, expected2 }, fixedCode);
    }

    [Fact]
    public async Task LocalVariable_InNestedLambda_AddsCapture()
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

    public static IQueryable<TResult> SelectExpr<TSource, TResult>(
        this IQueryable<TSource> source,
        System.Func<TSource, TResult> selector,
        object capture)
        => source.Select(x => selector(x));
}";

        var fixedCode =
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
            Values = s.Values.Select(v => v * multiplier).ToList()
        }, capture: new { multiplier });
    }
}

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
}";

        var expected = VerifyCS
            .Diagnostic(LocalVariableCaptureAnalyzer.DiagnosticId)
            .WithLocation(0)
            .WithArguments("multiplier");

        await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task InstanceField_AddsCapture()
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
    private int SampleValue = 10;

    void Method()
    {
        var list = new List<Entity>();
        var result = list.AsQueryable().SelectExpr(s => new { Value = {|#0:SampleValue|} });
    }
}

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
}";

        var fixedCode =
            @"
using System.Linq;
using System.Collections.Generic;

class Entity
{
    public int Value { get; set; }
}

class Test
{
    private int SampleValue = 10;

    void Method()
    {
        var list = new List<Entity>();
        var result = list.AsQueryable().SelectExpr(s => new { Value = SampleValue }, capture: new { SampleValue });
    }
}

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
}";

        var expected = VerifyCS
            .Diagnostic(LocalVariableCaptureAnalyzer.DiagnosticId)
            .WithLocation(0)
            .WithArguments("SampleValue");

        await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task MultipleInstanceMembers_AddsCapture()
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
    private int SampleValue { get; set; } = 10;
    protected string SampleText = ""Hello"";
    public const double Pi = 3.14;

    void Method()
    {
        var list = new List<Entity>();
        var result = list.AsQueryable().SelectExpr(x => new
        {
            Value = {|#0:SampleValue|},
            Text = {|#1:SampleText|},
            ConstantPi = {|#2:Pi|}
        });
    }
}

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
}";

        var fixedCode =
            @"
using System.Linq;
using System.Collections.Generic;

class Entity
{
    public int Value { get; set; }
}

class Test
{
    private int SampleValue { get; set; } = 10;
    protected string SampleText = ""Hello"";
    public const double Pi = 3.14;

    void Method()
    {
        var list = new List<Entity>();
        var result = list.AsQueryable().SelectExpr(x => new
        {
            Value = SampleValue,
            Text = SampleText,
            ConstantPi = Pi
        }, capture: new { Pi, SampleText, SampleValue });
    }
}

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
}";

        var expected1 = VerifyCS
            .Diagnostic(LocalVariableCaptureAnalyzer.DiagnosticId)
            .WithLocation(0)
            .WithArguments("SampleValue");

        var expected2 = VerifyCS
            .Diagnostic(LocalVariableCaptureAnalyzer.DiagnosticId)
            .WithLocation(1)
            .WithArguments("SampleText");

        var expected3 = VerifyCS
            .Diagnostic(LocalVariableCaptureAnalyzer.DiagnosticId)
            .WithLocation(2)
            .WithArguments("Pi");

        await VerifyCS.VerifyCodeFixAsync(
            test,
            new[] { expected1, expected2, expected3 },
            fixedCode
        );
    }

    [Fact]
    public async Task IncompleteCaptureParameter_UpdatesCapture()
    {
        var test =
            @"
using System.Linq;
using System.Collections.Generic;

class Entity
{
    public int Value { get; set; }
    public string Name { get; set; }
}

class Test
{
    void Method()
    {
        var local1 = 10;
        var local2 = ""World"";
        var list = new List<Entity>();
        var result = list.AsQueryable().SelectExpr(x => new
        {
            Foo = x.Value + local1,
            Bar = x.Name + {|#0:local2|}
        },
        capture: new { local1 });
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

        var fixedCode =
            @"
using System.Linq;
using System.Collections.Generic;

class Entity
{
    public int Value { get; set; }
    public string Name { get; set; }
}

class Test
{
    void Method()
    {
        var local1 = 10;
        var local2 = ""World"";
        var list = new List<Entity>();
        var result = list.AsQueryable().SelectExpr(x => new
        {
            Foo = x.Value + local1,
            Bar = x.Name + local2
        }, capture: new { local1, local2 });
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

        var expected = VerifyCS
            .Diagnostic(LocalVariableCaptureAnalyzer.DiagnosticId)
            .WithLocation(0)
            .WithArguments("local2");

        await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
    }
}
