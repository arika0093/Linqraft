using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;

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
            SelectToSelectExprAnonymousAnalyzer.AnalyzerId,
            DiagnosticSeverity.Info
        ).WithLocation(0);

        await RunCodeFixTestAsync(test, expected, fixedCode, 1); // Index 1 = explicit DTO pattern
    }

    [Fact]
    public async Task Issue98_AddsUsingDirective_ForSourceType_LQRS003()
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

    class SampleDto
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
        var result = list.AsQueryable().{|#0:Select|}(x => new MyNamespace.SampleDto { Id = x.Id, Name = x.Name });
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

    class SampleDto
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
        var result = list.AsQueryable().SelectExpr<Sample, ResultDto_T27C3JAA>(x => new { Id = x.Id, Name = x.Name });
    }
}";

        var expected = new DiagnosticResult(
            SelectToSelectExprNamedAnalyzer.AnalyzerId,
            DiagnosticSeverity.Info
        ).WithLocation(0);

        await RunCodeFixTestForNamedAsync(test, expected, fixedCode, 1); // Index 1 = root only conversion
    }

    [Fact]
    public async Task Issue98_AddsUsingDirective_ForSourceType_LQRS001()
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
        var result = list.AsQueryable().{|#0:SelectExpr|}(x => new { x.Id, x.Name });
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
            SelectExprToTypedAnalyzer.AnalyzerId,
            DiagnosticSeverity.Hidden
        ).WithLocation(0);

        await RunCodeFixTestForTypedAsync(test, expected, fixedCode, 0);
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

    private static async Task RunCodeFixTestForNamedAsync(
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

    private static async Task RunCodeFixTestForTypedAsync(
        string source,
        DiagnosticResult expected,
        string fixedSource,
        int codeActionIndex
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
            CodeActionIndex = codeActionIndex,
            CompilerDiagnostics = CompilerDiagnostics.None,
        };

        test.ExpectedDiagnostics.Add(expected);

        await test.RunAsync();
    }
}
