using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace Linqraft.Analyzer.Tests;

public class SelectToSelectExprAnalyzerTests
{
    [Fact]
    public async Task NoDiagnostic_WhenNotUsingSelect()
    {
        var test = @"
using System.Linq;

class TestClass
{
    void TestMethod()
    {
        var data = new[] { 1, 2, 3 };
        var result = data.Where(x => x > 1);
    }
}";

        await new CSharpAnalyzerTest<SelectToSelectExprAnalyzer, DefaultVerifier>
        {
            TestCode = test,
        }.RunAsync();
    }

    [Fact]
    public async Task ProducesDiagnostic_WhenUsingSelect()
    {
        var test = @"
using System.Linq;

class TestClass
{
    void TestMethod()
    {
        var data = new[] { 1, 2, 3 };
        var result = data.{|#0:Select|}(x => x * 2);
    }
}";

        var expected = DiagnosticResult.CompilerWarning(DiagnosticDescriptors.SelectToSelectExpr.Id)
            .WithLocation(0);

        await new CSharpAnalyzerTest<SelectToSelectExprAnalyzer, DefaultVerifier>
        {
            TestCode = test,
            ExpectedDiagnostics = { expected },
        }.RunAsync();
    }

    [Fact]
    public async Task CodeFix_ConvertsSelectToSelectExpr()
    {
        var test = @"
using System.Linq;

class TestClass
{
    void TestMethod()
    {
        var data = new[] { 1, 2, 3 };
        var result = data.{|#0:Select|}(x => x * 2);
    }
}";

        var fixedCode = @"
using System.Linq;

class TestClass
{
    void TestMethod()
    {
        var data = new[] { 1, 2, 3 };
        var result = data.SelectExpr(x => x * 2);
    }
}";

        var expected = DiagnosticResult.CompilerWarning(DiagnosticDescriptors.SelectToSelectExpr.Id)
            .WithLocation(0);

        await new CSharpCodeFixTest<SelectToSelectExprAnalyzer, SelectToSelectExprCodeFixProvider, DefaultVerifier>
        {
            TestCode = test,
            FixedCode = fixedCode,
            ExpectedDiagnostics = { expected },
        }.RunAsync();
    }
}
