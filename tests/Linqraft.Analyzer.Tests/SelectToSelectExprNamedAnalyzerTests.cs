using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    Linqraft.Analyzer.SelectToSelectExprNamedAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier
>;

namespace Linqraft.Analyzer.Tests;

public class SelectToSelectExprNamedAnalyzerTests
{
    [Fact]
    public async Task IQueryableSelect_WithNamedType_ReportsDiagnostic()
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

        var expected = VerifyCS
            .Diagnostic(SelectToSelectExprNamedAnalyzer.AnalyzerId)
            .WithLocation(0)
            .WithSeverity(DiagnosticSeverity.Info);

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task IQueryableSelect_WithNamedType_InVariableAssignment_ReportsDiagnostic()
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
        var users = list.AsQueryable().{|#0:Select|}(x => new SampleDto { Id = x.Id });
    }
}";

        var expected = VerifyCS
            .Diagnostic(SelectToSelectExprNamedAnalyzer.AnalyzerId)
            .WithLocation(0)
            .WithSeverity(DiagnosticSeverity.Info);

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task IEnumerableSelect_WithNamedType_NoDiagnostic()
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
        var result = list.Select(x => new SampleDto { Id = x.Id });
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task IQueryableSelect_WithAnonymousType_NoDiagnostic()
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
        var result = list.AsQueryable().Select(x => new { x.Id });
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task IQueryableSelect_WithSimpleProjection_NoDiagnostic()
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
    public async Task IQueryableSelect_WithComplexNamedType_ReportsDiagnostic()
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
            ChildNames = x.Children.Select(c => c.Name).ToList()
        });
    }
}";

        var expected = VerifyCS
            .Diagnostic(SelectToSelectExprNamedAnalyzer.AnalyzerId)
            .WithLocation(0)
            .WithSeverity(DiagnosticSeverity.Info);

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }
}
