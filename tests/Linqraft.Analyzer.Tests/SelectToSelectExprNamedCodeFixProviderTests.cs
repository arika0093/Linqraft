using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace Linqraft.Analyzer.Tests;

public class SelectToSelectExprNamedCodeFixProviderTests
{
    [Fact]
    public async Task CodeFix_SelectToSelectExpr_ExplicitDtoPattern()
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
            SelectToSelectExprNamedAnalyzer.AnalyzerId,
            DiagnosticSeverity.Info
        ).WithLocation(0);

        // Index 1 = root only conversion
        await RunCodeFixTestAsync(test, expected, fixedCode, 1);
    }

    [Fact]
    public async Task CodeFix_SelectToSelectExpr_PredefinedDtoPattern()
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
        var result = list.AsQueryable().SelectExpr(x => new SampleDto { Id = x.Id, Name = x.Name });
    }
}";

        var expected = new DiagnosticResult(
            SelectToSelectExprNamedAnalyzer.AnalyzerId,
            DiagnosticSeverity.Info
        ).WithLocation(0);

        // Index 2 = predefined DTO pattern
        await RunCodeFixTestAsync(test, expected, fixedCode, 2);
    }

    [Fact]
    public async Task CodeFix_SelectToSelectExpr_UsesVariableName()
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

        var fixedCode =
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
        var users = list.AsQueryable().SelectExpr<Sample, UsersDto_REIXTLBA>(x => new { Id = x.Id });
    }
}";

        var expected = new DiagnosticResult(
            SelectToSelectExprNamedAnalyzer.AnalyzerId,
            DiagnosticSeverity.Info
        ).WithLocation(0);

        // Index 1 = root only conversion
        await RunCodeFixTestAsync(test, expected, fixedCode, 1);
    }

    [Fact]
    public async Task CodeFix_SelectToSelectExpr_ExplicitDtoPattern_WithNestedSelect()
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
    public List<ChildDto> Children { get; set; }
}

class ChildDto
{
    public string Name { get; set; }
}

class Test
{
    void Method()
    {
        var list = new List<Parent>();
        var result = list.AsQueryable().{|#0:Select|}(x => new ParentDto 
        { 
            Id = x.Id,
            Children = x.Children.Select(c => new ChildDto { Name = c.Name }).ToList()
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
    public List<ChildDto> Children { get; set; }
}

class ChildDto
{
    public string Name { get; set; }
}

class Test
{
    void Method()
    {
        var list = new List<Parent>();
        var result = list.AsQueryable().SelectExpr<Parent, ResultDto_TTX2UNAA>(x => new
        {
            Id = x.Id,
            Children = x.Children.Select(c => new { Name = c.Name }).ToList()
        });
    }
}";

        var expected = new DiagnosticResult(
            SelectToSelectExprNamedAnalyzer.AnalyzerId,
            DiagnosticSeverity.Info
        ).WithLocation(0);

        // Index 0 = convert all (including nested)
        await RunCodeFixTestAsync(test, expected, fixedCode, 0);
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
            // Allow compiler errors for undefined SelectExpr (it's an extension method that will be available at runtime)
            CompilerDiagnostics = CompilerDiagnostics.None,
        };

        test.ExpectedDiagnostics.Add(expected);

        await test.RunAsync();
    }
}
