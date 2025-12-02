using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixVerifier<
    Linqraft.Analyzer.GroupByAnonymousKeyAnalyzer,
    Linqraft.Analyzer.GroupByAnonymousKeyCodeFixProvider,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier
>;

namespace Linqraft.Analyzer.Tests;

public class GroupByAnonymousKeyCodeFixProviderTests
{
    [Fact]
    public async Task CodeFix_ConvertsSinglePropertyAnonymousTypeToExpression()
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

        var fixedCode =
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

        var expected = VerifyCS
            .Diagnostic(GroupByAnonymousKeyAnalyzer.AnalyzerId)
            .WithLocation(0)
            .WithSeverity(DiagnosticSeverity.Error);

        await RunCodeFixTestAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task CodeFix_ConvertsMultiplePropertyAnonymousTypeToTuple()
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
            .{|#0:GroupBy|}(x => new { x.Id, x.Name })
            .SelectExpr(g => new { g.Key });
    }
}
" + TestSourceCodes.LinqraftPackageSourceCode;

        var fixedCode =
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
            .GroupBy(x => (x.Id, x.Name))
            .SelectExpr(g => new { g.Key });
    }
}
" + TestSourceCodes.LinqraftPackageSourceCode;

        var expected = VerifyCS
            .Diagnostic(GroupByAnonymousKeyAnalyzer.AnalyzerId)
            .WithLocation(0)
            .WithSeverity(DiagnosticSeverity.Error);

        await RunCodeFixTestAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task CodeFix_ConvertsAnonymousTypeWithNamedProperties()
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
            .{|#0:GroupBy|}(x => new { Key1 = x.Id, Key2 = x.Name })
            .SelectExpr(g => new { g.Key });
    }
}
" + TestSourceCodes.LinqraftPackageSourceCode;

        var fixedCode =
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
            .GroupBy(x => (Key1: x.Id, Key2: x.Name))
            .SelectExpr(g => new { g.Key });
    }
}
" + TestSourceCodes.LinqraftPackageSourceCode;

        var expected = VerifyCS
            .Diagnostic(GroupByAnonymousKeyAnalyzer.AnalyzerId)
            .WithLocation(0)
            .WithSeverity(DiagnosticSeverity.Error);

        await RunCodeFixTestAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task CodeFix_ConvertsAnonymousTypeWithMixedProperties()
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
            .{|#0:GroupBy|}(x => new { x.Id, Key2 = x.Name })
            .SelectExpr(g => new { g.Key });
    }
}
" + TestSourceCodes.LinqraftPackageSourceCode;

        var fixedCode =
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
            .GroupBy(x => (x.Id, Key2: x.Name))
            .SelectExpr(g => new { g.Key });
    }
}
" + TestSourceCodes.LinqraftPackageSourceCode;

        var expected = VerifyCS
            .Diagnostic(GroupByAnonymousKeyAnalyzer.AnalyzerId)
            .WithLocation(0)
            .WithSeverity(DiagnosticSeverity.Error);

        await RunCodeFixTestAsync(test, expected, fixedCode);
    }

    private static async Task RunCodeFixTestAsync(
        string source,
        DiagnosticResult expected,
        string fixedSource
    )
    {
        var test = new CSharpCodeFixTest<
            GroupByAnonymousKeyAnalyzer,
            GroupByAnonymousKeyCodeFixProvider,
            DefaultVerifier
        >
        {
            TestCode = source,
            FixedCode = fixedSource,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
            // Allow compiler errors for SelectExpr (it's an extension method that will be available at runtime)
            CompilerDiagnostics = CompilerDiagnostics.None,
        };

        test.ExpectedDiagnostics.Add(expected);

        await test.RunAsync();
    }
}
