using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;

namespace Linqraft.Analyzer.Tests;

public class SelectToSelectExprAnonymousCodeFixProviderTests
{
    [Fact]
    public async Task CodeFix_SelectToSelectExpr_AnonymousPattern()
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

        var fixedCode =
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
        var result = list.AsQueryable().SelectExpr(x => new { x.Id, x.Name });
    }
}";

        var expected = new DiagnosticResult(
            SelectToSelectExprAnonymousAnalyzer.AnalyzerId,
            DiagnosticSeverity.Info
        ).WithLocation(0);

        await RunCodeFixTestAsync(test, expected, fixedCode, 0);
    }

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

class Test
{
    void Method()
    {
        var list = new List<Sample>();
        var result = list.AsQueryable().{|#0:Select|}(x => new { x.Id, x.Name });
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

class Test
{
    void Method()
    {
        var list = new List<Sample>();
        var result = list.AsQueryable().SelectExpr<Sample, ResultDto_T27C3JAA>(x => new { x.Id, x.Name });
    }
}";

        var expected = new DiagnosticResult(
            SelectToSelectExprAnonymousAnalyzer.AnalyzerId,
            DiagnosticSeverity.Info
        ).WithLocation(0);

        await RunCodeFixTestAsync(test, expected, fixedCode, 1);
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

class Test
{
    void Method()
    {
        var list = new List<Sample>();
        var users = list.AsQueryable().{|#0:Select|}(x => new { x.Id });
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

class Test
{
    void Method()
    {
        var list = new List<Sample>();
        var users = list.AsQueryable().SelectExpr<Sample, UsersDto_REIXTLBA>(x => new { x.Id });
    }
}";

        var expected = new DiagnosticResult(
            SelectToSelectExprAnonymousAnalyzer.AnalyzerId,
            DiagnosticSeverity.Info
        ).WithLocation(0);

        await RunCodeFixTestAsync(test, expected, fixedCode, 1);
    }

    [Fact]
    public async Task CodeFix_SimplifiesTernaryNullCheck_Simple()
    {
        var test =
            @"
using System.Linq;
using System.Collections.Generic;

class Sample
{
    public int Id { get; set; }
    public Child? Foo { get; set; }
}

class Child
{
    public int Id { get; set; }
}

class Test
{
    void Method()
    {
        var list = new List<Sample>();
        var result = list.AsQueryable().{|#0:Select|}(x => new { Value = x.Foo != null ? x.Foo.Id : (int?)null });
    }
}";

        var fixedCode =
            @"
using System.Linq;
using System.Collections.Generic;

class Sample
{
    public int Id { get; set; }
    public Child? Foo { get; set; }
}

class Child
{
    public int Id { get; set; }
}

class Test
{
    void Method()
    {
        var list = new List<Sample>();
        var result = list.AsQueryable().SelectExpr(x => new { Value = x.Foo?.Id });
    }
}";

        var expected = new DiagnosticResult(
            SelectToSelectExprAnonymousAnalyzer.AnalyzerId,
            DiagnosticSeverity.Info
        ).WithLocation(0);

        await RunCodeFixTestAsync(test, expected, fixedCode, 0);
    }

    [Fact]
    public async Task CodeFix_SimplifiesTernaryNullCheck_WithTypeCast()
    {
        var test =
            @"
using System.Linq;
using System.Collections.Generic;

class Sample
{
    public int Id { get; set; }
    public Child? Child { get; set; }
}

class Child
{
    public int Id { get; set; }
}

class Test
{
    void Method()
    {
        var list = new List<Sample>();
        var result = list.AsQueryable().{|#0:Select|}(x => new { Value = x.Child != null ? (int?)x.Child.Id : (int?)null });
    }
}";

        var fixedCode =
            @"
using System.Linq;
using System.Collections.Generic;

class Sample
{
    public int Id { get; set; }
    public Child? Child { get; set; }
}

class Child
{
    public int Id { get; set; }
}

class Test
{
    void Method()
    {
        var list = new List<Sample>();
        var result = list.AsQueryable().SelectExpr(x => new { Value = x.Child?.Id });
    }
}";

        var expected = new DiagnosticResult(
            SelectToSelectExprAnonymousAnalyzer.AnalyzerId,
            DiagnosticSeverity.Info
        ).WithLocation(0);

        await RunCodeFixTestAsync(test, expected, fixedCode, 0);
    }

    [Fact]
    public async Task CodeFix_SimplifiesTernaryNullCheck_WithNullCast()
    {
        var test =
            @"
using System.Linq;
using System.Collections.Generic;

class Sample
{
    public int Id { get; set; }
    public Child? Child { get; set; }
}

class Child
{
    public int Id { get; set; }
}

class Test
{
    void Method()
    {
        var list = new List<Sample>();
        var result = list.AsQueryable().{|#0:Select|}(x => new { Value = x.Child != null ? x.Child.Id : (int?)null });
    }
}";

        var fixedCode =
            @"
using System.Linq;
using System.Collections.Generic;

class Sample
{
    public int Id { get; set; }
    public Child? Child { get; set; }
}

class Child
{
    public int Id { get; set; }
}

class Test
{
    void Method()
    {
        var list = new List<Sample>();
        var result = list.AsQueryable().SelectExpr(x => new { Value = x.Child?.Id });
    }
}";

        var expected = new DiagnosticResult(
            SelectToSelectExprAnonymousAnalyzer.AnalyzerId,
            DiagnosticSeverity.Info
        ).WithLocation(0);

        await RunCodeFixTestAsync(test, expected, fixedCode, 0);
    }

    [Fact]
    public async Task CodeFix_SimplifiesTernaryNullCheck_Nested()
    {
        var test =
            @"
using System.Linq;
using System.Collections.Generic;

class Sample
{
    public int Id { get; set; }
    public Child? Child3 { get; set; }
}

class Child
{
    public int Id { get; set; }
    public Child? Child { get; set; }
}

class Test
{
    void Method()
    {
        var list = new List<Sample>();
        var result = list.AsQueryable().{|#0:Select|}(x => new { Value = x.Child3 != null && x.Child3.Child != null ? x.Child3.Child.Id : (int?)null });
    }
}";

        var fixedCode =
            @"
using System.Linq;
using System.Collections.Generic;

class Sample
{
    public int Id { get; set; }
    public Child? Child3 { get; set; }
}

class Child
{
    public int Id { get; set; }
    public Child? Child { get; set; }
}

class Test
{
    void Method()
    {
        var list = new List<Sample>();
        var result = list.AsQueryable().SelectExpr(x => new { Value = x.Child3?.Child?.Id });
    }
}";

        var expected = new DiagnosticResult(
            SelectToSelectExprAnonymousAnalyzer.AnalyzerId,
            DiagnosticSeverity.Info
        ).WithLocation(0);

        await RunCodeFixTestAsync(test, expected, fixedCode, 0);
    }

    [Fact]
    public async Task CodeFix_SimplifiesTernaryNullCheck_DeepNesting()
    {
        var test =
            @"
using System.Linq;
using System.Collections.Generic;

class Sample
{
    public int Id { get; set; }
    public Child? Child { get; set; }
}

class Child
{
    public int Id { get; set; }
    public Child? Child { get; set; }
}

class Test
{
    void Method()
    {
        var list = new List<Sample>();
        var result = list.AsQueryable().{|#0:Select|}(x => new { Value = x.Child != null && x.Child.Child != null && x.Child.Child.Child != null ? x.Child.Child.Child.Id : (int?)null });
    }
}";

        var fixedCode =
            @"
using System.Linq;
using System.Collections.Generic;

class Sample
{
    public int Id { get; set; }
    public Child? Child { get; set; }
}

class Child
{
    public int Id { get; set; }
    public Child? Child { get; set; }
}

class Test
{
    void Method()
    {
        var list = new List<Sample>();
        var result = list.AsQueryable().SelectExpr(x => new { Value = x.Child?.Child?.Child?.Id });
    }
}";

        var expected = new DiagnosticResult(
            SelectToSelectExprAnonymousAnalyzer.AnalyzerId,
            DiagnosticSeverity.Info
        ).WithLocation(0);

        await RunCodeFixTestAsync(test, expected, fixedCode, 0);
    }

    [Fact]
    public async Task CodeFix_SimplifiesTernaryNullCheck_WhenReturningObject()
    {
        var test =
            @"
using System.Linq;
using System.Collections.Generic;

class Sample
{
    public int Id { get; set; }
    public Nest? Nest { get; set; }
}

class Nest
{
    public int Id { get; set; }
    public string Name { get; set; }
}

class Test
{
    void Method()
    {
        var list = new List<Sample>();
        var result = list.AsQueryable().{|#0:Select|}(s => new {
            NestField = s.Nest != null
                ? new {
                    Id = s.Nest.Id,
                    Name = s.Nest.Name
                }
                : null,
        });
    }
}";

        var fixedCode =
            @"
using System.Linq;
using System.Collections.Generic;

class Sample
{
    public int Id { get; set; }
    public Nest? Nest { get; set; }
}

class Nest
{
    public int Id { get; set; }
    public string Name { get; set; }
}

class Test
{
    void Method()
    {
        var list = new List<Sample>();
        var result = list.AsQueryable().SelectExpr(s => new
        {
            NestField = new
            {
                Id = s.Nest?.Id,
                Name = s.Nest?.Name
            },
        });
    }
}";

        var expected = new DiagnosticResult(
            SelectToSelectExprAnonymousAnalyzer.AnalyzerId,
            DiagnosticSeverity.Info
        ).WithLocation(0);

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

class Test
{
    void Method()
    {
        var list = new List<Sample>();
        var test = 10;
        var result = list.AsQueryable().{|#0:Select|}(x => new { x.Id, test });
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

class Test
{
    void Method()
    {
        var list = new List<Sample>();
        var test = 10;
        var result = list.AsQueryable().SelectExpr(x => new { x.Id, test }, capture: new { test });
    }
}";

        var expected = new DiagnosticResult(
            SelectToSelectExprAnonymousAnalyzer.AnalyzerId,
            DiagnosticSeverity.Info
        ).WithLocation(0);

        await RunCodeFixTestAsync(test, expected, fixedCode, 0);
    }

    [Fact]
    public async Task CodeFix_AddsCapture_WhenMultipleLocalVariablesAreUsed()
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
        var test1 = 10;
        var test2 = ""hello"";
        var result = list.AsQueryable().{|#0:Select|}(x => new { x.Id, test1, test2 });
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

class Test
{
    void Method()
    {
        var list = new List<Sample>();
        var test1 = 10;
        var test2 = ""hello"";
        var result = list.AsQueryable().SelectExpr(x => new { x.Id, test1, test2 }, capture: new { test1, test2 });
    }
}";

        var expected = new DiagnosticResult(
            SelectToSelectExprAnonymousAnalyzer.AnalyzerId,
            DiagnosticSeverity.Info
        ).WithLocation(0);

        await RunCodeFixTestAsync(test, expected, fixedCode, 0);
    }

    [Fact]
    public async Task CodeFix_AddsCapture_WhenLocalVariableIsUsed_WithChainedMethod()
    {
        // This test verifies that when converting .Select(...).FirstOrDefault() to SelectExpr,
        // the capture parameter is correctly added to SelectExpr and the formatting is preserved.
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
    private List<Sample> Datas = new List<Sample>();

    void Query()
    {
        var localValue = ""test"";
        var _ = Datas
            .AsQueryable()
            .{|#0:Select|}(d => new
            {
                d.Id,
                d.Name,
                LocalValue = localValue,
            })
            .FirstOrDefault();
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

class Test
{
    private List<Sample> Datas = new List<Sample>();

    void Query()
    {
        var localValue = ""test"";
        var _ = Datas
            .AsQueryable()
            .SelectExpr(d => new
            {
                d.Id,
                d.Name,
                LocalValue = localValue,
            }, capture: new { localValue })
            .FirstOrDefault();
    }
}";

        var expected = new DiagnosticResult(
            SelectToSelectExprAnonymousAnalyzer.AnalyzerId,
            DiagnosticSeverity.Info
        ).WithLocation(0);

        // Index 0 = anonymous pattern
        await RunCodeFixTestAsync(test, expected, fixedCode, 0);
    }

    /// <summary>
    /// Test for issue #160: When converting complex nested objects with ternary null checks,
    /// the null-conditional operator should be inserted correctly.
    /// </summary>
    [Fact]
    public async Task CodeFix_Issue160_TernaryNullCheckWithLinqChain_ShouldInsertNullConditional()
    {
        var test =
            @"
using System.Linq;
using System.Collections.Generic;

class TestData
{
    public required ChildData InnerData { get; set; }
}

class ChildData
{
    public required Child2? ChildMaybeNull { get; set; }
}

class Child2
{
    public required List<Child3> AnotherChilds { get; set; }
}

class Child3
{
    public required int Id { get; set; }
}

class Test
{
    private List<TestData> _datas = [];

    void Query()
    {
        var result = _datas
            .AsQueryable()
            .{|#0:Select|}(d => new
            {
                TestData = d.InnerData.ChildMaybeNull != null
                    ? d
                        .InnerData.ChildMaybeNull.AnotherChilds.Where(ac => ac.Id > 0)
                        .Select(ac => ac.Id)
                        .ToList()
                    : null,
            })
            .ToList();
    }
}";

        // The key fix is that the null-conditional operator (?.) is inserted after ChildMaybeNull
        // The formatting may differ slightly but the semantic behavior is correct
        var fixedCode =
            @"
using System.Linq;
using System.Collections.Generic;

class TestData
{
    public required ChildData InnerData { get; set; }
}

class ChildData
{
    public required Child2? ChildMaybeNull { get; set; }
}

class Child2
{
    public required List<Child3> AnotherChilds { get; set; }
}

class Child3
{
    public required int Id { get; set; }
}

class Test
{
    private List<TestData> _datas = [];

    void Query()
    {
        var result = _datas
            .AsQueryable()
            .SelectExpr(d => new
            {
                TestData = d.InnerData.ChildMaybeNull?.AnotherChilds.Where(ac => ac.Id > 0)
                    .Select(ac => ac.Id)
                    .ToList(),
            })
            .ToList();
    }
}";

        var expected = new DiagnosticResult(
            SelectToSelectExprAnonymousAnalyzer.AnalyzerId,
            DiagnosticSeverity.Info
        ).WithLocation(0);

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
            SelectToSelectExprAnonymousAnalyzer,
            SelectToSelectExprAnonymousCodeFixProvider,
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
