using System.Threading.Tasks;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixVerifier<
    Linqraft.Analyzer.UnnecessaryCaptureAnalyzer,
    Linqraft.Analyzer.UnnecessaryCaptureCodeFixProvider,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier
>;

namespace Linqraft.Analyzer.Tests;

public class UnnecessaryCaptureCodeFixProviderTests
{
    [Fact]
    public async Task SingleUnnecessaryCaptureVariable_RemovesCaptureArgument()
    {
        var test =
            @"
using System.Linq;
using System.Collections.Generic;

class Test
{
    void Method()
    {
        var local = 10;
        var list = new List<Entity> { new Entity { Id = 1 } };
        var result = list.AsQueryable().SelectExpr(s => new { s.Id }, capture: new { {|#0:local|} });
    }
}

class Entity
{
    public int Id { get; set; }
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
        var local = 10;
        var list = new List<Entity> { new Entity { Id = 1 } };
        var result = list.AsQueryable().SelectExpr(s => new { s.Id });
    }
}

class Entity
{
    public int Id { get; set; }
}

" + TestSourceCodes.SelectExprWithFuncAndBothOverloads;

        var expected = VerifyCS
            .Diagnostic(UnnecessaryCaptureAnalyzer.AnalyzerId)
            .WithLocation(0)
            .WithArguments("local");

        await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task MultipleUnnecessaryCaptureVariables_RemovesCaptureArgument()
    {
        var test =
            @"
using System.Linq;
using System.Collections.Generic;

class Test
{
    void Method()
    {
        var local1 = 10;
        var local2 = 20;
        var list = new List<Entity> { new Entity { Id = 1 } };
        var result = list.AsQueryable().SelectExpr(s => new { s.Id }, capture: new { {|#0:local1|}, {|#1:local2|} });
    }
}

class Entity
{
    public int Id { get; set; }
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
        var local1 = 10;
        var local2 = 20;
        var list = new List<Entity> { new Entity { Id = 1 } };
        var result = list.AsQueryable().SelectExpr(s => new { s.Id });
    }
}

class Entity
{
    public int Id { get; set; }
}

" + TestSourceCodes.SelectExprWithFuncAndBothOverloads;

        var expected1 = VerifyCS
            .Diagnostic(UnnecessaryCaptureAnalyzer.AnalyzerId)
            .WithLocation(0)
            .WithArguments("local1");

        var expected2 = VerifyCS
            .Diagnostic(UnnecessaryCaptureAnalyzer.AnalyzerId)
            .WithLocation(1)
            .WithArguments("local2");

        await VerifyCS.VerifyCodeFixAsync(test, new[] { expected1, expected2 }, fixedCode);
    }

    [Fact]
    public async Task PartiallyUnnecessaryCaptureVariables_RemovesOnlyUnnecessary()
    {
        var test =
            @"
using System.Linq;
using System.Collections.Generic;

class Test
{
    void Method()
    {
        var used = 10;
        var unused = 20;
        var list = new List<int> { 1, 2, 3 };
        var result = list.AsQueryable().SelectExpr(s => new { Value = s + used }, capture: new { used, {|#0:unused|} });
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
        var used = 10;
        var unused = 20;
        var list = new List<int> { 1, 2, 3 };
        var result = list.AsQueryable().SelectExpr(s => new { Value = s + used }, capture: new { used });
    }
}

" + TestSourceCodes.SelectExprWithFuncAndBothOverloads;

        var expected = VerifyCS
            .Diagnostic(UnnecessaryCaptureAnalyzer.AnalyzerId)
            .WithLocation(0)
            .WithArguments("unused");

        await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task PartiallyUnnecessaryMultipleVariables_RemovesOnlyUnnecessary()
    {
        var test =
            @"
using System.Linq;
using System.Collections.Generic;

class Test
{
    void Method()
    {
        var used1 = 10;
        var unused = 20;
        var used2 = 30;
        var list = new List<int> { 1, 2, 3 };
        var result = list.AsQueryable().SelectExpr(s => new { 
            Value1 = s + used1,
            Value2 = s + used2
        }, capture: new { used1, {|#0:unused|}, used2 });
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
        var used1 = 10;
        var unused = 20;
        var used2 = 30;
        var list = new List<int> { 1, 2, 3 };
        var result = list.AsQueryable().SelectExpr(s => new { 
            Value1 = s + used1,
            Value2 = s + used2
        }, capture: new { used1, used2 });
    }
}

" + TestSourceCodes.SelectExprWithFuncAndBothOverloads;

        var expected = VerifyCS
            .Diagnostic(UnnecessaryCaptureAnalyzer.AnalyzerId)
            .WithLocation(0)
            .WithArguments("unused");

        await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task PositionalCaptureArgument_RemovesCaptureArgument()
    {
        var test =
            @"
using System.Linq;
using System.Collections.Generic;

class Test
{
    void Method()
    {
        var local = 10;
        var list = new List<Entity> { new Entity { Id = 1 } };
        var result = list.AsQueryable().SelectExpr(s => new { s.Id }, new { {|#0:local|} });
    }
}

class Entity
{
    public int Id { get; set; }
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
        var local = 10;
        var list = new List<Entity> { new Entity { Id = 1 } };
        var result = list.AsQueryable().SelectExpr(s => new { s.Id });
    }
}

class Entity
{
    public int Id { get; set; }
}

" + TestSourceCodes.SelectExprWithFuncAndBothOverloads;

        var expected = VerifyCS
            .Diagnostic(UnnecessaryCaptureAnalyzer.AnalyzerId)
            .WithLocation(0)
            .WithArguments("local");

        await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task CaptureWithNameEquals_RemovesCaptureArgument()
    {
        var test =
            @"
using System.Linq;
using System.Collections.Generic;

class Test
{
    void Method()
    {
        var local = 10;
        var list = new List<Entity> { new Entity { Id = 1 } };
        var result = list.AsQueryable().SelectExpr(s => new { s.Id }, capture: new { {|#0:capturedLocal|} = local });
    }
}

class Entity
{
    public int Id { get; set; }
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
        var local = 10;
        var list = new List<Entity> { new Entity { Id = 1 } };
        var result = list.AsQueryable().SelectExpr(s => new { s.Id });
    }
}

class Entity
{
    public int Id { get; set; }
}

" + TestSourceCodes.SelectExprWithFuncAndBothOverloads;

        var expected = VerifyCS
            .Diagnostic(UnnecessaryCaptureAnalyzer.AnalyzerId)
            .WithLocation(0)
            .WithArguments("capturedLocal");

        await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task CaptureWithNameEquals_RemovesCaptureArgument_Multiline()
    {
        var test =
            @"
using System.Linq;
using System.Collections.Generic;

class Test
{
    void Method()
    {
        var local = 10;
        var list = new List<Entity> { new Entity { Id = 1 } };
        var result = list.AsQueryable().SelectExpr(
            s => new { s.Id },
            capture: new { {|#0:capturedLocal|} = local }
        );
    }
}

class Entity
{
    public int Id { get; set; }
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
        var local = 10;
        var list = new List<Entity> { new Entity { Id = 1 } };
        var result = list.AsQueryable().SelectExpr(
            s => new { s.Id }
        );
    }
}

class Entity
{
    public int Id { get; set; }
}

" + TestSourceCodes.SelectExprWithFuncAndBothOverloads;

        var expected = VerifyCS
            .Diagnostic(UnnecessaryCaptureAnalyzer.AnalyzerId)
            .WithLocation(0)
            .WithArguments("capturedLocal");

        await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task PartiallyUnnecessaryWithNameEquals_RemovesOnlyUnnecessary()
    {
        var test =
            @"
using System.Linq;
using System.Collections.Generic;

class Test
{
    void Method()
    {
        var used = 10;
        var unused = 20;
        var list = new List<int> { 1, 2, 3 };
        var result = list.AsQueryable().SelectExpr(s => new { Value = s + used }, capture: new { used, {|#0:unusedVar|} = unused });
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
        var used = 10;
        var unused = 20;
        var list = new List<int> { 1, 2, 3 };
        var result = list.AsQueryable().SelectExpr(s => new { Value = s + used }, capture: new { used });
    }
}

" + TestSourceCodes.SelectExprWithFuncAndBothOverloads;

        var expected = VerifyCS
            .Diagnostic(UnnecessaryCaptureAnalyzer.AnalyzerId)
            .WithLocation(0)
            .WithArguments("unusedVar");

        await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
    }
}
