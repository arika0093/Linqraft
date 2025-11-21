using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace Linqraft.Analyzer.Tests;

public class Issue97TestsV2
{
    [Fact]
    public async Task Issue97_PreservesWhitespace_InConversionAll()
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

class SampleDto
{
    public int Id { get; set; }
    public string Name { get; set; }
}

class Test
{
    void Method()
    {
        var list = new List<Sample>();
        var result = list.AsQueryable().{|#0:Select|}(x => new SampleDto { Id = x.Id, Name = x.Name });
    }
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

class SampleDto
{
    public int Id { get; set; }
    public string Name { get; set; }
}

class Test
{
    void Method()
    {
        var list = new List<Sample>();
        var result = list.AsQueryable().SelectExpr<Sample, ResultDto_T27C3JAA>(x => new { Id = x.Id, Name = x.Name });
    }
}";

        var expected = new DiagnosticResult(
            SelectToSelectExprNamedAnalyzer.DiagnosticId,
            DiagnosticSeverity.Info
        ).WithLocation(0);

        await RunCodeFixTestAsync(test, expected, fixedCode, 0); // Index 0 = convert all
    }

    private static async Task RunCodeFixTestAsync(
        string source,
        DiagnosticResult expected,
        string fixedSource,
        int codeActionIndex
    )
    {
        var test = new CSharpCodeFixTest<
            SelectToSelectExprNamedAnalyzer,
            SelectToSelectExprNamedCodeFixProvider,
            DefaultVerifier
        >
        {
            TestCode = source,
            FixedCode = fixedSource,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
            CodeActionIndex = codeActionIndex,
            CompilerDiagnostics = CompilerDiagnostics.None,
        };

        test.ExpectedDiagnostics.Add(expected);

        await test.RunAsync();
    }
}
