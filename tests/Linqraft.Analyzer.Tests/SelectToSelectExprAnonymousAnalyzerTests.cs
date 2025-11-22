using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    Linqraft.Analyzer.SelectToSelectExprAnonymousAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier
>;

namespace Linqraft.Analyzer.Tests;

public class SelectToSelectExprAnonymousAnalyzerTests
{
    [Fact]
    public async Task IQueryableSelect_WithAnonymousType_ReportsDiagnostic()
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

class Test
{
    void Method()
    {
        var list = new List<Sample>();
        var result = list.AsQueryable().{|#0:Select|}(x => new { x.Id, x.Name });
    }
}";

        var expected = VerifyCS
            .Diagnostic(SelectToSelectExprAnonymousAnalyzer.AnalyzerId)
            .WithLocation(0)
            .WithSeverity(DiagnosticSeverity.Info);

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task IQueryableSelect_WithAnonymousType_InVariableAssignment_ReportsDiagnostic()
    {
        var test =
            @"
using System.Linq;
using System.Collections.Generic;

class Sample
{
    public int Id { get; set; }
}

class Test
{
    void Method()
    {
        var list = new List<Sample>();
        var users = list.AsQueryable().{|#0:Select|}(x => new { x.Id });
    }
}";

        var expected = VerifyCS
            .Diagnostic(SelectToSelectExprAnonymousAnalyzer.AnalyzerId)
            .WithLocation(0)
            .WithSeverity(DiagnosticSeverity.Info);

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task IEnumerableSelect_WithAnonymousType_NoDiagnostic()
    {
        var test =
            @"
using System.Linq;
using System.Collections.Generic;

class Sample
{
    public int Id { get; set; }
}

class Test
{
    void Method()
    {
        var list = new List<Sample>();
        var result = list.Select(x => new { x.Id });
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task IQueryableSelect_WithoutAnonymousType_NoDiagnostic()
    {
        var test =
            @"
using System.Linq;
using System.Collections.Generic;

class Sample
{
    public int Id { get; set; }
}

class Test
{
    void Method()
    {
        var list = new List<Sample>();
        var result = list.AsQueryable().Select(x => x.Id);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task IQueryableSelect_WithNamedType_NoDiagnostic()
    {
        var test =
            @"
using System.Linq;
using System.Collections.Generic;

class Sample
{
    public int Id { get; set; }
}

class SampleDto
{
    public int Id { get; set; }
}

class Test
{
    void Method()
    {
        var list = new List<Sample>();
        var result = list.AsQueryable().Select(x => new SampleDto { Id = x.Id });
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task IQueryableSelect_WithComplexAnonymousType_ReportsDiagnostic()
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

class Test
{
    void Method()
    {
        var list = new List<Parent>();
        var result = list.AsQueryable().{|#0:Select|}(x => new 
        { 
            x.Id,
            ChildNames = x.Children.Select(c => c.Name).ToList()
        });
    }
}";

        var expected = VerifyCS
            .Diagnostic(SelectToSelectExprAnonymousAnalyzer.AnalyzerId)
            .WithLocation(0)
            .WithSeverity(DiagnosticSeverity.Info);

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }
}
