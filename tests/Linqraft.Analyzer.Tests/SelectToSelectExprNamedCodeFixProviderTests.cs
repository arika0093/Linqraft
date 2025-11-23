using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;

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

    [Fact]
    public async Task CodeFix_AddsCapture_WhenLocalVariableIsUsed()
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
    public int Test { get; set; }
}

class Test
{
    void Method()
    {
        var list = new List<Sample>();
        var test = 10;
        var result = list.AsQueryable().{|#0:Select|}(x => new SampleDto { Id = x.Id, Test = test });
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
    public int Test { get; set; }
}

class Test
{
    void Method()
    {
        var list = new List<Sample>();
        var test = 10;
        var result = list.AsQueryable().SelectExpr(x => new SampleDto { Id = x.Id, Test = test }, capture: new { test });
    }
}";

        var expected = new DiagnosticResult(
            SelectToSelectExprNamedAnalyzer.AnalyzerId,
            DiagnosticSeverity.Info
        ).WithLocation(0);

        // Index 2 = use predefined classes
        await RunCodeFixTestAsync(test, expected, fixedCode, 2);
    }

    [Fact]
    public async Task CodeFix_Case1_AllExplicitConvert_PreservesFormatting()
    {
        var test =
            @"
using System.Linq;
using System.Collections.Generic;

class SampleClass
{
    public int Id { get; set; }
    public string Foo { get; set; }
    public string Bar { get; set; }
    public List<SampleChild> Childs { get; set; }
    public SampleChild2 Child2 { get; set; }
    public SampleChild3 Child3 { get; set; }
}

class SampleChild
{
    public int Id { get; set; }
    public string Baz { get; set; }
    public SampleChildChild Child { get; set; }
}

class SampleChildChild
{
    public int? Id { get; set; }
    public string Qux { get; set; }
}

class SampleChild2
{
    public int? Id { get; set; }
    public string Quux { get; set; }
}

class SampleChild3
{
    public int Id { get; set; }
    public string Corge { get; set; }
    public SampleChild3Child Child { get; set; }
}

class SampleChild3Child
{
    public int? Id { get; set; }
    public string Grault { get; set; }
}

class ManualSampleClassDto
{
    public int Id { get; set; }
    public string Foo { get; set; }
    public string Bar { get; set; }
    public object Childs { get; set; }
    public int? Child2Id { get; set; }
    public string Child2Quux { get; set; }
    public int Child3Id { get; set; }
    public string Child3Corge { get; set; }
    public int? Child3ChildId { get; set; }
    public string Child3ChildGrault { get; set; }
}

class ManualSampleChildDto
{
    public int Id { get; set; }
    public string Baz { get; set; }
    public int? ChildId { get; set; }
    public string ChildQux { get; set; }
}

class DbContext
{
    public List<SampleClass> SampleClasses { get; set; }
}

class Test
{
    void Method()
    {
        var _dbContext = new DbContext();
        var results = _dbContext
            .SampleClasses.AsQueryable().{|#0:Select|}(s => new ManualSampleClassDto
            {
                Id = s.Id,
                Foo = s.Foo,
                Bar = s.Bar,
                Childs = s.Childs.Select(c => new ManualSampleChildDto
                {
                    Id = c.Id,
                    Baz = c.Baz,
                    ChildId = c.Child != null ? c.Child.Id : null,
                    ChildQux = c.Child != null ? c.Child.Qux : null,
                }),
                Child2Id = s.Child2 != null ? s.Child2.Id : null,
                Child2Quux = s.Child2 != null ? s.Child2.Quux : null,
                Child3Id = s.Child3.Id,
                Child3Corge = s.Child3.Corge,
                Child3ChildId =
                    s.Child3 != null && s.Child3.Child != null ? s.Child3.Child.Id : null,
                Child3ChildGrault =
                    s.Child3 != null && s.Child3.Child != null ? s.Child3.Child.Grault : null,
            });
    }
}";

        var fixedCode =
            @"
using System.Linq;
using System.Collections.Generic;

class SampleClass
{
    public int Id { get; set; }
    public string Foo { get; set; }
    public string Bar { get; set; }
    public List<SampleChild> Childs { get; set; }
    public SampleChild2 Child2 { get; set; }
    public SampleChild3 Child3 { get; set; }
}

class SampleChild
{
    public int Id { get; set; }
    public string Baz { get; set; }
    public SampleChildChild Child { get; set; }
}

class SampleChildChild
{
    public int? Id { get; set; }
    public string Qux { get; set; }
}

class SampleChild2
{
    public int? Id { get; set; }
    public string Quux { get; set; }
}

class SampleChild3
{
    public int Id { get; set; }
    public string Corge { get; set; }
    public SampleChild3Child Child { get; set; }
}

class SampleChild3Child
{
    public int? Id { get; set; }
    public string Grault { get; set; }
}

class ManualSampleClassDto
{
    public int Id { get; set; }
    public string Foo { get; set; }
    public string Bar { get; set; }
    public object Childs { get; set; }
    public int? Child2Id { get; set; }
    public string Child2Quux { get; set; }
    public int Child3Id { get; set; }
    public string Child3Corge { get; set; }
    public int? Child3ChildId { get; set; }
    public string Child3ChildGrault { get; set; }
}

class ManualSampleChildDto
{
    public int Id { get; set; }
    public string Baz { get; set; }
    public int? ChildId { get; set; }
    public string ChildQux { get; set; }
}

class DbContext
{
    public List<SampleClass> SampleClasses { get; set; }
}

class Test
{
    void Method()
    {
        var _dbContext = new DbContext();
        var results = _dbContext
            .SampleClasses.AsQueryable().SelectExpr<SampleClass, ResultsDto_T4NIPRBA>(s => new
            {
                Id = s.Id,
                Foo = s.Foo,
                Bar = s.Bar,
                Childs = s.Childs.Select(c => new
                {
                    Id = c.Id,
                    Baz = c.Baz,
                    ChildId = c.Child?.Id,
                    ChildQux = c.Child?.Qux,
                }),
                Child2Id = s.Child2?.Id,
                Child2Quux = s.Child2?.Quux,
                Child3Id = s.Child3.Id,
                Child3Corge = s.Child3.Corge,
                Child3ChildId = s.Child3?.Child?.Id,
                Child3ChildGrault = s.Child3?.Child?.Grault
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

    [Fact]
    public async Task CodeFix_Case2_PredefinedFormatting_PreservesContinuationIndentation()
    {
        var test =
            @"
using System.Linq;
using System.Collections.Generic;

class SampleClass
{
    public int Id { get; set; }
    public string Foo { get; set; }
    public string Bar { get; set; }
    public List<SampleChild> Childs { get; set; }
    public SampleChild2 Child2 { get; set; }
    public SampleChild3 Child3 { get; set; }
}

class SampleChild
{
    public int Id { get; set; }
    public string Baz { get; set; }
    public SampleChildChild Child { get; set; }
}

class SampleChildChild
{
    public int? Id { get; set; }
    public string Qux { get; set; }
}

class SampleChild2
{
    public int? Id { get; set; }
    public string Quux { get; set; }
}

class SampleChild3
{
    public int Id { get; set; }
    public string Corge { get; set; }
    public SampleChild3Child Child { get; set; }
}

class SampleChild3Child
{
    public int? Id { get; set; }
    public string Grault { get; set; }
}

class ManualSampleClassDto
{
    public int Id { get; set; }
    public string Foo { get; set; }
    public string Bar { get; set; }
    public object Childs { get; set; }
    public int? Child2Id { get; set; }
    public string Child2Quux { get; set; }
    public int Child3Id { get; set; }
    public string Child3Corge { get; set; }
    public int? Child3ChildId { get; set; }
    public string Child3ChildGrault { get; set; }
}

class ManualSampleChildDto
{
    public int Id { get; set; }
    public string Baz { get; set; }
    public int? ChildId { get; set; }
    public string ChildQux { get; set; }
}

class DbContext
{
    public List<SampleClass> SampleClasses { get; set; }
}

class Test
{
    void Method()
    {
        var _dbContext = new DbContext();
        var results = _dbContext
            .SampleClasses.AsQueryable().{|#0:Select|}(s => new ManualSampleClassDto
            {
                Id = s.Id,
                Foo = s.Foo,
                Bar = s.Bar,
                Childs = s.Childs.Select(c => new ManualSampleChildDto
                {
                    Id = c.Id,
                    Baz = c.Baz,
                    ChildId = c.Child != null ? c.Child.Id : null,
                    ChildQux = c.Child != null ? c.Child.Qux : null,
                }),
                Child2Id = s.Child2 != null ? s.Child2.Id : null,
                Child2Quux = s.Child2 != null ? s.Child2.Quux : null,
                Child3Id = s.Child3.Id,
                Child3Corge = s.Child3.Corge,
                Child3ChildId =
                    s.Child3 != null && s.Child3.Child != null ? s.Child3.Child.Id : null,
                Child3ChildGrault =
                    s.Child3 != null && s.Child3.Child != null ? s.Child3.Child.Grault : null,
            });
    }
}";

        var fixedCode =
            @"
using System.Linq;
using System.Collections.Generic;

class SampleClass
{
    public int Id { get; set; }
    public string Foo { get; set; }
    public string Bar { get; set; }
    public List<SampleChild> Childs { get; set; }
    public SampleChild2 Child2 { get; set; }
    public SampleChild3 Child3 { get; set; }
}

class SampleChild
{
    public int Id { get; set; }
    public string Baz { get; set; }
    public SampleChildChild Child { get; set; }
}

class SampleChildChild
{
    public int? Id { get; set; }
    public string Qux { get; set; }
}

class SampleChild2
{
    public int? Id { get; set; }
    public string Quux { get; set; }
}

class SampleChild3
{
    public int Id { get; set; }
    public string Corge { get; set; }
    public SampleChild3Child Child { get; set; }
}

class SampleChild3Child
{
    public int? Id { get; set; }
    public string Grault { get; set; }
}

class ManualSampleClassDto
{
    public int Id { get; set; }
    public string Foo { get; set; }
    public string Bar { get; set; }
    public object Childs { get; set; }
    public int? Child2Id { get; set; }
    public string Child2Quux { get; set; }
    public int Child3Id { get; set; }
    public string Child3Corge { get; set; }
    public int? Child3ChildId { get; set; }
    public string Child3ChildGrault { get; set; }
}

class ManualSampleChildDto
{
    public int Id { get; set; }
    public string Baz { get; set; }
    public int? ChildId { get; set; }
    public string ChildQux { get; set; }
}

class DbContext
{
    public List<SampleClass> SampleClasses { get; set; }
}

class Test
{
    void Method()
    {
        var _dbContext = new DbContext();
        var results = _dbContext
            .SampleClasses.AsQueryable().SelectExpr(s => new ManualSampleClassDto
            {
                Id = s.Id,
                Foo = s.Foo,
                Bar = s.Bar,
                Childs = s.Childs.Select(c => new ManualSampleChildDto
                {
                    Id = c.Id,
                    Baz = c.Baz,
                    ChildId = c.Child?.Id,
                    ChildQux = c.Child?.Qux,
                }),
                Child2Id = s.Child2?.Id,
                Child2Quux = s.Child2?.Quux,
                Child3Id = s.Child3.Id,
                Child3Corge = s.Child3.Corge,
                Child3ChildId =
                    s.Child3?.Child?.Id,
                Child3ChildGrault =
                    s.Child3?.Child?.Grault,
            });
    }
}";

        var expected = new DiagnosticResult(
            SelectToSelectExprNamedAnalyzer.AnalyzerId,
            DiagnosticSeverity.Info
        ).WithLocation(0);

        // Index 2 = use predefined classes
        await RunCodeFixTestAsync(test, expected, fixedCode, 2);
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
