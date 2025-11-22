using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace Linqraft.Analyzer.Tests;

public class Issue102Tests
{
    [Fact]
    public async Task Issue102_DoesNotConvert_EmptyListInitializers()
    {
        var test =
            @"
using System.Linq;
using System.Collections.Generic;

class Parent
{
    public int Id { get; set; }
    public List<Child> Children { get; set; }
}

class Child
{
    public string Name { get; set; }
}

class ParentDto
{
    public int Id { get; set; }
    public List<string> ChildNames { get; set; }
}

class Test
{
    void Method()
    {
        var list = new List<Parent>();
        var result = list.AsQueryable().{|#0:Select|}(x => new ParentDto 
        { 
            Id = x.Id,
            ChildNames = new List<string>()
        });
    }
}";

        var fixedCode =
            @"
using System.Linq;
using System.Collections.Generic;

class Parent
{
    public int Id { get; set; }
    public List<Child> Children { get; set; }
}

class Child
{
    public string Name { get; set; }
}

class ParentDto
{
    public int Id { get; set; }
    public List<string> ChildNames { get; set; }
}

class Test
{
    void Method()
    {
        var list = new List<Parent>();
        var result = list.AsQueryable().SelectExpr<Parent, ResultDto_UD6NMUAA>(x => new
        {
            Id = x.Id,
            ChildNames = new List<string>()
        });
    }
}";

        var expected = new DiagnosticResult(
            SelectToSelectExprNamedAnalyzer.AnalyzerId,
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
