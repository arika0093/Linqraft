using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    Linqraft.Analyzer.UnnecessaryCaptureAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier
>;

namespace Linqraft.Analyzer.Tests;

public class UnnecessaryCaptureAnalyzerTests
{
    [Fact]
    public async Task UnnecessaryCaptureVariable_ReportsDiagnostic()
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

" + TestSourceCodes.SelectExprWithFuncAndCapture;

        var expected = VerifyCS
            .Diagnostic(UnnecessaryCaptureAnalyzer.AnalyzerId)
            .WithLocation(0)
            .WithArguments("local")
            .WithSeverity(DiagnosticSeverity.Warning);

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task MultipleUnnecessaryCaptureVariables_ReportsMultipleDiagnostics()
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

" + TestSourceCodes.SelectExprWithFuncAndCapture;

        var expected1 = VerifyCS
            .Diagnostic(UnnecessaryCaptureAnalyzer.AnalyzerId)
            .WithLocation(0)
            .WithArguments("local1")
            .WithSeverity(DiagnosticSeverity.Warning);

        var expected2 = VerifyCS
            .Diagnostic(UnnecessaryCaptureAnalyzer.AnalyzerId)
            .WithLocation(1)
            .WithArguments("local2")
            .WithSeverity(DiagnosticSeverity.Warning);

        await VerifyCS.VerifyAnalyzerAsync(test, expected1, expected2);
    }

    [Fact]
    public async Task PartiallyUnnecessaryCaptureVariables_ReportsOnlyUnnecessary()
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

" + TestSourceCodes.SelectExprWithFuncAndCapture;

        var expected = VerifyCS
            .Diagnostic(UnnecessaryCaptureAnalyzer.AnalyzerId)
            .WithLocation(0)
            .WithArguments("unused")
            .WithSeverity(DiagnosticSeverity.Warning);

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task AllCaptureVariablesUsed_NoDiagnostic()
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
        var list = new List<int> { 1, 2, 3 };
        var result = list.AsQueryable().SelectExpr(s => new { 
            Value1 = s + local1,
            Value2 = s + local2
        }, capture: new { local1, local2 });
    }
}

" + TestSourceCodes.SelectExprWithFuncAndCapture;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task NoCaptureParameter_NoDiagnostic()
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
        var result = list.AsQueryable().SelectExpr(s => new { Value = s });
    }
}

" + TestSourceCodes.SelectExprWithFunc;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task CapturedVariableUsedInMemberAccess_NoDiagnostic()
    {
        var test =
            @"
using System.Linq;
using System.Collections.Generic;

class Test
{
    void Method()
    {
        var obj = new Entity { Name = ""Test"" };
        var list = new List<int> { 1, 2, 3 };
        var result = list.AsQueryable().SelectExpr(s => new { Value = obj.Name }, capture: new { obj });
    }
}

class Entity
{
    public string Name { get; set; }
}

" + TestSourceCodes.SelectExprWithFuncAndCapture;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task CapturedFieldUsed_NoDiagnostic()
    {
        var test =
            @"
using System.Linq;
using System.Collections.Generic;

class Test
{
    private int _field = 10;

    void Method()
    {
        var list = new List<int> { 1, 2, 3 };
        var result = list.AsQueryable().SelectExpr(s => new { Value = s + _field }, capture: new { _field });
    }
}

" + TestSourceCodes.SelectExprWithFuncAndCapture;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task PositionalCaptureArgument_ReportsDiagnostic()
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

" + TestSourceCodes.SelectExprWithFuncAndCapture;

        var expected = VerifyCS
            .Diagnostic(UnnecessaryCaptureAnalyzer.AnalyzerId)
            .WithLocation(0)
            .WithArguments("local")
            .WithSeverity(DiagnosticSeverity.Warning);

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task CaptureWithNameEquals_ReportsDiagnostic()
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

" + TestSourceCodes.SelectExprWithFuncAndCapture;

        var expected = VerifyCS
            .Diagnostic(UnnecessaryCaptureAnalyzer.AnalyzerId)
            .WithLocation(0)
            .WithArguments("capturedLocal")
            .WithSeverity(DiagnosticSeverity.Warning);

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }
}
