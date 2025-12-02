using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    Linqraft.Analyzer.GroupByAnonymousKeyAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier
>;

namespace Linqraft.Analyzer.Tests;

public class GroupByAnonymousKeyAnalyzerTests
{
    [Fact]
    public async Task GroupByAnonymousType_WithSelectExpr_ReportsDiagnostic()
    {
        var test =
            @"
using System.Linq;
using System.Collections.Generic;
using Linqraft;

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
        var result = list.AsQueryable()
            .{|#0:GroupBy|}(x => new { x.Id })
            .SelectExpr(g => new { g.Key });
    }
}
" + TestSourceCodes.LinqraftPackageSourceCode;

        var expected = VerifyCS
            .Diagnostic(GroupByAnonymousKeyAnalyzer.AnalyzerId)
            .WithLocation(0)
            .WithSeverity(DiagnosticSeverity.Error);

        await RunAnalyzerTestAsync(test, expected);
    }

    [Fact]
    public async Task GroupByAnonymousType_WithMultipleProperties_ReportsDiagnostic()
    {
        var test =
            @"
using System.Linq;
using System.Collections.Generic;
using Linqraft;

class Entity
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
}

class Test
{
    void Method()
    {
        var list = new List<Entity>();
        var result = list.AsQueryable()
            .{|#0:GroupBy|}(e => new { e.Id, e.Name })
            .SelectExpr(g => new 
            { 
                g.Key,
                Count = g.Count()
            });
    }
}
" + TestSourceCodes.LinqraftPackageSourceCode;

        var expected = VerifyCS
            .Diagnostic(GroupByAnonymousKeyAnalyzer.AnalyzerId)
            .WithLocation(0)
            .WithSeverity(DiagnosticSeverity.Error);

        await RunAnalyzerTestAsync(test, expected);
    }

    [Fact]
    public async Task GroupBySingleProperty_WithSelectExpr_NoDiagnostic()
    {
        var test =
            @"
using System.Linq;
using System.Collections.Generic;
using Linqraft;

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
        var result = list.AsQueryable()
            .GroupBy(x => x.Id)
            .SelectExpr(g => new { g.Key });
    }
}
" + TestSourceCodes.LinqraftPackageSourceCode;

        await RunAnalyzerTestAsync(test);
    }

    [Fact]
    public async Task GroupByAnonymousType_WithoutSelectExpr_NoDiagnostic()
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
        var result = list.AsQueryable()
            .GroupBy(x => new { x.Id })
            .Select(g => new { g.Key });
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task GroupByAnonymousType_WithSelectExpr_InChain_ReportsDiagnostic()
    {
        var test =
            @"
using System.Linq;
using System.Collections.Generic;
using Linqraft;

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
        var result = list.AsQueryable()
            .Where(x => x.Id > 0)
            .{|#0:GroupBy|}(x => new { x.Id })
            .SelectExpr(g => new { g.Key })
            .ToList();
    }
}
" + TestSourceCodes.LinqraftPackageSourceCode;

        var expected = VerifyCS
            .Diagnostic(GroupByAnonymousKeyAnalyzer.AnalyzerId)
            .WithLocation(0)
            .WithSeverity(DiagnosticSeverity.Error);

        await RunAnalyzerTestAsync(test, expected);
    }

    private static async Task RunAnalyzerTestAsync(string source, params DiagnosticResult[] expected)
    {
        var test = new CSharpAnalyzerTest<GroupByAnonymousKeyAnalyzer, DefaultVerifier>
        {
            TestCode = source,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
            // Ignore compiler errors for SelectExpr (it's an extension method with test stub)
            CompilerDiagnostics = CompilerDiagnostics.None,
        };

        test.ExpectedDiagnostics.AddRange(expected);
        await test.RunAsync();
    }
}
