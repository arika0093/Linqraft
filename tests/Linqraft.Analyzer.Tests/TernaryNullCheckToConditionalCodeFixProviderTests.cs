using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace Linqraft.Analyzer.Tests;

public class TernaryNullCheckToConditionalCodeFixProviderTests
{
    [Fact]
    public async Task CodeFix_ConvertsTernaryToNullConditional_SimpleCase()
    {
        var test =
            @"
class Sample
{
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
        var s = new Sample();
        var result = {|#0:s.Nest != null
            ? new {
                Id = s.Nest.Id,
                Name = s.Nest.Name
            }
            : null|};
    }
}";

        var fixedCode =
            @"
class Sample
{
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
        var s = new Sample();
        var result = new {
                Id = s.Nest?.Id,
                Name = s.Nest?.Name
        };
    }
}";

        var expected = new DiagnosticResult(
            TernaryNullCheckToConditionalAnalyzer.AnalyzerId,
            DiagnosticSeverity.Info
        ).WithLocation(0);

        await RunCodeFixTestAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task CodeFix_ConvertsTernaryToNullConditional_NestedNullChecks()
    {
        var test =
            @"
class Sample
{
    public Nest? Nest { get; set; }
}

class Nest
{
    public int Id { get; set; }
    public Child? Child { get; set; }
}

class Child
{
    public string Name { get; set; }
}

class Test
{
    void Method()
    {
        var s = new Sample();
        var result = {|#0:s.Nest != null && s.Nest.Child != null
            ? new {
                Id = s.Nest.Id,
                ChildName = s.Nest.Child.Name
            }
            : null|};
    }
}";

        var fixedCode =
            @"
class Sample
{
    public Nest? Nest { get; set; }
}

class Nest
{
    public int Id { get; set; }
    public Child? Child { get; set; }
}

class Child
{
    public string Name { get; set; }
}

class Test
{
    void Method()
    {
        var s = new Sample();
        var result = new {
                Id = s.Nest?.Id,
                ChildName = s.Nest?.Child?.Name
        };
    }
}";

        var expected = new DiagnosticResult(
            TernaryNullCheckToConditionalAnalyzer.AnalyzerId,
            DiagnosticSeverity.Info
        ).WithLocation(0);

        await RunCodeFixTestAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task CodeFix_ConvertsTernaryToNullConditional_WithNullableCast()
    {
        var test =
            @"
class Sample
{
    public Nest? Nest { get; set; }
}

class Nest
{
    public int Id { get; set; }
}

class Test
{
    void Method()
    {
        var s = new Sample();
        var result = {|#0:s.Nest != null
            ? (object?)new {
                Id = s.Nest.Id
            }
            : null|};
    }
}";

        var fixedCode =
            @"
class Sample
{
    public Nest? Nest { get; set; }
}

class Nest
{
    public int Id { get; set; }
}

class Test
{
    void Method()
    {
        var s = new Sample();
        var result = new {
                Id = s.Nest?.Id
        };
    }
}";

        var expected = new DiagnosticResult(
            TernaryNullCheckToConditionalAnalyzer.AnalyzerId,
            DiagnosticSeverity.Info
        ).WithLocation(0);

        await RunCodeFixTestAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task CodeFix_ConvertsTernaryToNullConditional_IssueScenario()
    {
        // This is the exact scenario from the GitHub issue
        var test =
            @"
class Sample
{
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
        var s = new Sample();
        var result = {|#0:s.Nest != null
            ? new {
                Id = s.Nest.Id,
                Name = s.Nest.Name
            }
            : null|};
    }
}";

        var fixedCode =
            @"
class Sample
{
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
        var s = new Sample();
        var result = new {
                Id = s.Nest?.Id,
                Name = s.Nest?.Name
        };
    }
}";

        var expected = new DiagnosticResult(
            TernaryNullCheckToConditionalAnalyzer.AnalyzerId,
            DiagnosticSeverity.Info
        ).WithLocation(0);

        await RunCodeFixTestAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task CodeFix_ConvertsTernaryToNullConditional_InvertedCondition()
    {
        // Test the inverted case: condition ? null : new{}
        var test =
            @"
class Parent
{
    public Child? Child { get; set; }
}

class Child
{
    public string Name { get; set; }
}

class ChildDto
{
    public string Name { get; set; }
}

class Test
{
    void Method()
    {
        var p = new Parent();
        var result = {|#0:p.Child == null 
            ? null 
            : new ChildDto {
                Name = p.Child.Name
            }|};
    }
}";

        var fixedCode =
            @"
class Parent
{
    public Child? Child { get; set; }
}

class Child
{
    public string Name { get; set; }
}

class ChildDto
{
    public string Name { get; set; }
}

class Test
{
    void Method()
    {
        var p = new Parent();
        var result = new ChildDto {
                Name = p.Child?.Name
        };
    }
}";

        var expected = new DiagnosticResult(
            TernaryNullCheckToConditionalAnalyzer.AnalyzerId,
            DiagnosticSeverity.Info
        ).WithLocation(0);

        await RunCodeFixTestAsync(test, expected, fixedCode);
    }

    private static async Task RunCodeFixTestAsync(
        string source,
        DiagnosticResult expected,
        string fixedSource
    )
    {
        var test = new CSharpCodeFixTest<
            TernaryNullCheckToConditionalAnalyzer,
            TernaryNullCheckToConditionalCodeFixProvider,
            DefaultVerifier
        >
        {
            TestCode = source,
            FixedCode = fixedSource,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };

        test.ExpectedDiagnostics.Add(expected);

        await test.RunAsync();
    }
}
