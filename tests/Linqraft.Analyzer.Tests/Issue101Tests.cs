using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace Linqraft.Analyzer.Tests;

public class Issue101Tests
{
    [Fact]
    public async Task Issue101_UsesFullyQualifiedNames_ForNestedObjects()
    {
        var test =
            @"
using System.Linq;
using System.Collections.Generic;

namespace MyApp
{
    class Parent
    {
        public int Id { get; set; }
        public Child Child { get; set; }
    }

    class Child
    {
        public string Name { get; set; }
    }

    class ParentDto
    {
        public int Id { get; set; }
        public ChildDto Child { get; set; }
    }

    class ChildDto
    {
        public string Name { get; set; }
    }
}

class Test
{
    void Method()
    {
        var list = new List<MyApp.Parent>();
        var result = list.AsQueryable().{|#0:Select|}(x => new MyApp.ParentDto 
        { 
            Id = x.Id,
            Child = new MyApp.ChildDto { Name = x.Child.Name }
        });
    }
}";

        var fixedCode =
            @"
using System.Linq;
using System.Collections.Generic;
using MyApp;

namespace MyApp
{
    class Parent
    {
        public int Id { get; set; }
        public Child Child { get; set; }
    }

    class Child
    {
        public string Name { get; set; }
    }

    class ParentDto
    {
        public int Id { get; set; }
        public ChildDto Child { get; set; }
    }

    class ChildDto
    {
        public string Name { get; set; }
    }
}

class Test
{
    void Method()
    {
        var list = new List<MyApp.Parent>();
        var result = list.AsQueryable().SelectExpr<Parent, ResultDto_4QSMNXAA>(x => new
        {
            Id = x.Id,
            Child = new global::MyApp.ChildDto{ Name = x.Child.Name }
        });
    }
}";

        var expected = new DiagnosticResult(
            SelectToSelectExprNamedAnalyzer.DiagnosticId,
            DiagnosticSeverity.Info
        ).WithLocation(0);

        await RunCodeFixTestAsync(test, expected, fixedCode, 1); // Index 1 = root only conversion
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
