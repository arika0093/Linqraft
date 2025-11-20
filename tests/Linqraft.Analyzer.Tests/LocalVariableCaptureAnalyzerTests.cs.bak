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

    [Fact]
    public async Task InstanceField_InSelectExpr_ReportsDiagnostic()
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
}";

        var expected = VerifyCS
            .Diagnostic(LocalVariableCaptureAnalyzer.DiagnosticId)
            .WithLocation(0)
            .WithArguments("SampleValue")
            .WithSeverity(DiagnosticSeverity.Error);

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task InstanceProperty_InSelectExpr_ReportsDiagnostic()
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
    private string SampleText { get; set; } = ""Hello"";

    void Method()
    {
        var list = new List<Entity>();
        var result = list.AsQueryable().SelectExpr(s => new { Text = {|#0:SampleText|} });
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
            .WithArguments("SampleText")
            .WithSeverity(DiagnosticSeverity.Error);

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task MultipleInstanceMembers_InSelectExpr_ReportsMultipleDiagnostics()
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
}";

        var expected1 = VerifyCS
            .Diagnostic(LocalVariableCaptureAnalyzer.DiagnosticId)
            .WithLocation(0)
            .WithArguments("SampleValue")
            .WithSeverity(DiagnosticSeverity.Error);

        var expected2 = VerifyCS
            .Diagnostic(LocalVariableCaptureAnalyzer.DiagnosticId)
            .WithLocation(1)
            .WithArguments("SampleText")
            .WithSeverity(DiagnosticSeverity.Error);

        var expected3 = VerifyCS
            .Diagnostic(LocalVariableCaptureAnalyzer.DiagnosticId)
            .WithLocation(2)
            .WithArguments("Pi")
            .WithSeverity(DiagnosticSeverity.Error);

        await VerifyCS.VerifyAnalyzerAsync(test, expected1, expected2, expected3);
    }

    [Fact]
    public async Task IncompleteCaptureParameter_ReportsDiagnostic()
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

        var expected = VerifyCS
            .Diagnostic(LocalVariableCaptureAnalyzer.DiagnosticId)
            .WithLocation(0)
            .WithArguments("local2")
            .WithSeverity(DiagnosticSeverity.Error);

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task StaticField_ReportsDiagnostic()
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
    private static int StaticValue = 10;

    void Method()
    {
        var list = new List<Entity>();
        var result = list.AsQueryable().SelectExpr(s => new { Value = {|#0:StaticValue|} });
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
            .WithArguments("StaticValue")
            .WithSeverity(DiagnosticSeverity.Error);

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task ConstField_ReportsDiagnostic()
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
    private const double Pi = 3.14;

    void Method()
    {
        var list = new List<Entity>();
        var result = list.AsQueryable().SelectExpr(s => new { Constant = {|#0:Pi|} });
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
            .WithArguments("Pi")
            .WithSeverity(DiagnosticSeverity.Error);

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task ThisMemberAccess_ReportsDiagnostic()
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
    private string TestProperty { get; set; } = ""test"";

    void Method()
    {
        var list = new List<Entity>();
        var result = list.AsQueryable().SelectExpr(s => new { Prop = {|#0:this.TestProperty|} });
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
            .WithArguments("TestProperty")
            .WithSeverity(DiagnosticSeverity.Error);

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task StaticMemberAccess_ReportsDiagnostic()
    {
        var test =
            @"
using System.Linq;
using System.Collections.Generic;

class Entity
{
    public int Value { get; set; }
}

class AnotherClass
{
    public static string StaticValue = ""static"";
}

class Test
{
    void Method()
    {
        var list = new List<Entity>();
        var result = list.AsQueryable().SelectExpr(s => new { Value = {|#0:AnotherClass.StaticValue|} });
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
            .WithArguments("StaticValue")
            .WithSeverity(DiagnosticSeverity.Error);

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }
}
