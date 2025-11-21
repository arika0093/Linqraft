using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace Linqraft.Analyzer.Tests;

public class Issue98Tests
{
    [Fact]
    public async Task Issue98_AddsUsingDirective_ForSourceType()
    {
        var test =
            @"
using System.Linq;
using System.Collections.Generic;

namespace MyNamespace
{
    class Sample
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }
}

class Test
{
    void Method()
    {
        var list = new List<MyNamespace.Sample>();
        var result = list.AsQueryable().{|#0:Select|}(x => new { x.Id, x.Name });
    }
}";

        var fixedCode =
            @"
using System.Linq;
using System.Collections.Generic;
using MyNamespace;

namespace MyNamespace
{
    class Sample
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }
}

class Test
{
    void Method()
    {
        var list = new List<MyNamespace.Sample>();
        var result = list.AsQueryable().SelectExpr<Sample, ResultDto_T27C3JAA>(x => new { x.Id, x.Name });
    }
}";

        var expected = new DiagnosticResult(
            SelectToSelectExprAnonymousAnalyzer.DiagnosticId,
            DiagnosticSeverity.Info
        ).WithLocation(0);

        await RunCodeFixTestAsync(test, expected, fixedCode, 1); // Index 1 = explicit DTO pattern
    }

    private static async Task RunCodeFixTestAsync(
        string source,
        DiagnosticResult expected,
        string fixedSource,
        int codeActionIndex
    )
    {
        var test = new CSharpCodeFixTest<
            SelectToSelectExprAnonymousAnalyzer,
            SelectToSelectExprAnonymousCodeFixProvider,
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
