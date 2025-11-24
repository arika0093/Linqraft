using System.Threading.Tasks;
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

" + TestSourceCodes.SelectExprWithFuncAndBothOverloads;

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

" + TestSourceCodes.SelectExprWithFuncAndBothOverloads;

        var expected = VerifyCS
            .Diagnostic(LocalVariableCaptureAnalyzer.AnalyzerId)
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

" + TestSourceCodes.SelectExprWithFuncAndBothOverloads;

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

" + TestSourceCodes.SelectExprWithFuncAndBothOverloads;

        var expected1 = VerifyCS
            .Diagnostic(LocalVariableCaptureAnalyzer.AnalyzerId)
            .WithLocation(0)
            .WithArguments("localVar1");

        var expected2 = VerifyCS
            .Diagnostic(LocalVariableCaptureAnalyzer.AnalyzerId)
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

" + TestSourceCodes.SelectExprWithFuncAndBothOverloads;

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

" + TestSourceCodes.SelectExprWithFuncAndBothOverloads;

        var expected = VerifyCS
            .Diagnostic(LocalVariableCaptureAnalyzer.AnalyzerId)
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

" + TestSourceCodes.SelectExprWithFuncAndBothOverloads;

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

" + TestSourceCodes.SelectExprWithFuncAndBothOverloads;

        var expected1 = VerifyCS
            .Diagnostic(LocalVariableCaptureAnalyzer.AnalyzerId)
            .WithLocation(0)
            .WithArguments("multiplier");

        var expected2 = VerifyCS
            .Diagnostic(LocalVariableCaptureAnalyzer.AnalyzerId)
            .WithLocation(1)
            .WithArguments("offset");

        await VerifyCS.VerifyCodeFixAsync(test, new[] { expected1, expected2 }, fixedCode);
    }

    [Fact]
    public async Task LocalVariable_InComplexExpression_AddsCapture_Format2()
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
        var result = list.AsQueryable().SelectExpr(
            s => new { 
                Result = (s.Value * {|#0:multiplier|}) + {|#1:offset|}
            }
        ).ToList();
    }
}

" + TestSourceCodes.SelectExprWithFuncAndBothOverloads;

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
        var result = list.AsQueryable().SelectExpr(
            s => new { 
                Result = (s.Value * multiplier) + offset
            }, capture: new { multiplier, offset }
        ).ToList();
    }
}

" + TestSourceCodes.SelectExprWithFuncAndBothOverloads;

        var expected1 = VerifyCS
            .Diagnostic(LocalVariableCaptureAnalyzer.AnalyzerId)
            .WithLocation(0)
            .WithArguments("multiplier");

        var expected2 = VerifyCS
            .Diagnostic(LocalVariableCaptureAnalyzer.AnalyzerId)
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

" + TestSourceCodes.SelectExprWithFuncObjectAndBothOverloads;

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

" + TestSourceCodes.SelectExprWithFuncObjectAndBothOverloads;

        var expected = VerifyCS
            .Diagnostic(LocalVariableCaptureAnalyzer.AnalyzerId)
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

" + TestSourceCodes.SelectExprWithFuncAndBothOverloads;

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

" + TestSourceCodes.SelectExprWithFuncAndBothOverloads;

        var expected1 = VerifyCS
            .Diagnostic(LocalVariableCaptureAnalyzer.AnalyzerId)
            .WithLocation(0)
            .WithArguments("multiplier");

        var expected2 = VerifyCS
            .Diagnostic(LocalVariableCaptureAnalyzer.AnalyzerId)
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

" + TestSourceCodes.SelectExprWithFuncAndBothOverloads;

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

" + TestSourceCodes.SelectExprWithFuncAndBothOverloads;

        var expected = VerifyCS
            .Diagnostic(LocalVariableCaptureAnalyzer.AnalyzerId)
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

" + TestSourceCodes.SelectExprWithFuncAndBothOverloads;

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

" + TestSourceCodes.SelectExprWithFuncAndBothOverloads;

        var expected = VerifyCS
            .Diagnostic(LocalVariableCaptureAnalyzer.AnalyzerId)
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
            ConstantPi = Pi
        });
    }
}

" + TestSourceCodes.SelectExprWithFuncAndBothOverloads;

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
        }, capture: new { SampleText, SampleValue });
    }
}

" + TestSourceCodes.SelectExprWithFuncAndBothOverloads;

        var expected1 = VerifyCS
            .Diagnostic(LocalVariableCaptureAnalyzer.AnalyzerId)
            .WithLocation(0)
            .WithArguments("SampleValue");

        var expected2 = VerifyCS
            .Diagnostic(LocalVariableCaptureAnalyzer.AnalyzerId)
            .WithLocation(1)
            .WithArguments("SampleText");

        await VerifyCS.VerifyCodeFixAsync(test, new[] { expected1, expected2 }, fixedCode);
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

" + TestSourceCodes.SelectExprWithFuncAndCaptureOnly;

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

" + TestSourceCodes.SelectExprWithFuncAndCaptureOnly;

        var expected = VerifyCS
            .Diagnostic(LocalVariableCaptureAnalyzer.AnalyzerId)
            .WithLocation(0)
            .WithArguments("local2");

        await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task ThisMemberAccess_CreatesLocalVariableAndCaptures()
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
    private int Status = 10;

    void Method()
    {
        var list = new List<Entity>();
        var result = list.AsQueryable().SelectExpr(x => new
        {
            x.Value,
            OrderStatus = {|#0:this.Status|}
        });
    }
}

" + TestSourceCodes.SelectExprWithFuncAndBothOverloads;

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
    private int Status = 10;

    void Method()
    {
        var list = new List<Entity>();
        var captured_Status = this.Status;
        var result = list.AsQueryable().SelectExpr(x => new
        {
            x.Value,
            OrderStatus = captured_Status
        }, capture: new { captured_Status });
    }
}

" + TestSourceCodes.SelectExprWithFuncAndBothOverloads;

        var expected = VerifyCS
            .Diagnostic(LocalVariableCaptureAnalyzer.AnalyzerId)
            .WithLocation(0)
            .WithArguments("Status");

        await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task StaticMemberAccess_CreatesLocalVariableAndCaptures()
    {
        var test =
            @"
using System.Linq;
using System.Collections.Generic;

class Entity
{
    public int Value { get; set; }
}

static class Settings
{
    public static int DefaultValue = 42;
}

class Test
{
    void Method()
    {
        var list = new List<Entity>();
        var result = list.AsQueryable().SelectExpr(x => new
        {
            x.Value,
            Default = {|#0:Settings.DefaultValue|}
        });
    }
}

" + TestSourceCodes.SelectExprWithFuncAndBothOverloads;

        var fixedCode =
            @"
using System.Linq;
using System.Collections.Generic;

class Entity
{
    public int Value { get; set; }
}

static class Settings
{
    public static int DefaultValue = 42;
}

class Test
{
    void Method()
    {
        var list = new List<Entity>();
        var captured_DefaultValue = Settings.DefaultValue;
        var result = list.AsQueryable().SelectExpr(x => new
        {
            x.Value,
            Default = captured_DefaultValue
        }, capture: new { captured_DefaultValue });
    }
}

" + TestSourceCodes.SelectExprWithFuncAndBothOverloads;

        var expected = VerifyCS
            .Diagnostic(LocalVariableCaptureAnalyzer.AnalyzerId)
            .WithLocation(0)
            .WithArguments("DefaultValue");

        await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task InstanceVariableMemberAccess_CreatesLocalVariableAndCaptures()
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
    public string Property { get; set; }
}

class Test
{
    void Method()
    {
        var anotherClass = new AnotherClass { Property = ""test"" };
        var list = new List<Entity>();
        var result = list.AsQueryable().SelectExpr(x => new
        {
            x.Value,
            Other = {|#0:anotherClass.Property|}
        });
    }
}

" + TestSourceCodes.SelectExprWithFuncAndBothOverloads;

        var fixedCode =
            @"
using System.Linq;
using System.Collections.Generic;

class Entity
{
    public int Value { get; set; }
}

class AnotherClass
{
    public string Property { get; set; }
}

class Test
{
    void Method()
    {
        var anotherClass = new AnotherClass { Property = ""test"" };
        var list = new List<Entity>();
        var captured_Property = anotherClass.Property;
        var result = list.AsQueryable().SelectExpr(x => new
        {
            x.Value,
            Other = captured_Property
        }, capture: new { captured_Property });
    }
}

" + TestSourceCodes.SelectExprWithFuncAndBothOverloads;

        var expected = VerifyCS
            .Diagnostic(LocalVariableCaptureAnalyzer.AnalyzerId)
            .WithLocation(0)
            .WithArguments("Property");

        await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task MixedReferences_CreatesLocalVariablesAndCaptures()
    {
        var test =
            @"
using System.Linq;
using System.Collections.Generic;

class Entity
{
    public int Value { get; set; }
}

static class Settings
{
    public static int DefaultValue = 42;
}

class AnotherClass
{
    public string Property { get; set; }
}

class Test
{
    private int Status = 10;

    void Method()
    {
        var anotherClass = new AnotherClass { Property = ""test"" };
        var list = new List<Entity>();
        var result = list.AsQueryable().SelectExpr(x => new
        {
            x.Value,
            OrderStatus = {|#0:this.Status|},
            Default = {|#1:Settings.DefaultValue|},
            Other = {|#2:anotherClass.Property|}
        });
    }
}

" + TestSourceCodes.SelectExprWithFuncAndBothOverloads;

        var fixedCode =
            @"
using System.Linq;
using System.Collections.Generic;

class Entity
{
    public int Value { get; set; }
}

static class Settings
{
    public static int DefaultValue = 42;
}

class AnotherClass
{
    public string Property { get; set; }
}

class Test
{
    private int Status = 10;

    void Method()
    {
        var anotherClass = new AnotherClass { Property = ""test"" };
        var list = new List<Entity>();
        var captured_Status = this.Status;
        var captured_DefaultValue = Settings.DefaultValue;
        var captured_Property = anotherClass.Property;
        var result = list.AsQueryable().SelectExpr(x => new
        {
            x.Value,
            OrderStatus = captured_Status,
            Default = captured_DefaultValue,
            Other = captured_Property
        }, capture: new { captured_DefaultValue, captured_Property, captured_Status });
    }
}

" + TestSourceCodes.SelectExprWithFuncAndBothOverloads;

        var expected1 = VerifyCS
            .Diagnostic(LocalVariableCaptureAnalyzer.AnalyzerId)
            .WithLocation(0)
            .WithArguments("Status");

        var expected2 = VerifyCS
            .Diagnostic(LocalVariableCaptureAnalyzer.AnalyzerId)
            .WithLocation(1)
            .WithArguments("DefaultValue");

        var expected3 = VerifyCS
            .Diagnostic(LocalVariableCaptureAnalyzer.AnalyzerId)
            .WithLocation(2)
            .WithArguments("Property");

        await VerifyCS.VerifyCodeFixAsync(
            test,
            new[] { expected1, expected2, expected3 },
            fixedCode
        );
    }
}
