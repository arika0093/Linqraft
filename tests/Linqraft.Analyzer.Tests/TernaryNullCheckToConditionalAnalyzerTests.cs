using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace Linqraft.Analyzer.Tests;

public class TernaryNullCheckToConditionalAnalyzerTests
{
    [Fact]
    public async Task Analyzer_DetectsTernaryNullCheckWithObjectCreation()
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

        var expected = new DiagnosticResult(
            TernaryNullCheckToConditionalAnalyzer.DiagnosticId,
            DiagnosticSeverity.Info
        ).WithLocation(0);

        await RunAnalyzerTestAsync(test, expected);
    }

    [Fact]
    public async Task Analyzer_DetectsTernaryNullCheckWithAnonymousObjectCreation()
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
        var result = {|#0:s.Nest != null ? new { s.Nest.Id } : null|};
    }
}";

        var expected = new DiagnosticResult(
            TernaryNullCheckToConditionalAnalyzer.DiagnosticId,
            DiagnosticSeverity.Info
        ).WithLocation(0);

        await RunAnalyzerTestAsync(test, expected);
    }

    [Fact]
    public async Task Analyzer_DoesNotDetectTernaryWithoutObjectCreation()
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
        var result = s.Nest != null ? s.Nest.Id : 0;
    }
}";

        await RunAnalyzerTestAsync(test);
    }

    [Fact]
    public async Task Analyzer_DoesNotDetectTernaryWithoutNullCheck()
    {
        var test =
            @"
class Sample
{
    public int Value { get; set; }
}

class Test
{
    void Method()
    {
        var s = new Sample();
        var result = s.Value > 0
            ? new { s.Value }
            : null;
    }
}";

        await RunAnalyzerTestAsync(test);
    }

    [Fact]
    public async Task Analyzer_DetectsTernaryNullCheckWithInvertedCondition()
    {
        // Test inverted condition: x == null ? null : new{}
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
        var result = {|#0:p.Child == null ? null : new ChildDto { Name = p.Child.Name }|};
    }
}";

        var expected = new DiagnosticResult(
            TernaryNullCheckToConditionalAnalyzer.DiagnosticId,
            DiagnosticSeverity.Info
        ).WithLocation(0);

        await RunAnalyzerTestAsync(test, expected);
    }

    private static async Task RunAnalyzerTestAsync(
        string source,
        params DiagnosticResult[] expected
    )
    {
        var test = new CSharpAnalyzerTest<TernaryNullCheckToConditionalAnalyzer, DefaultVerifier>
        {
            TestCode = source,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };

        test.ExpectedDiagnostics.AddRange(expected);

        await test.RunAsync();
    }
}
