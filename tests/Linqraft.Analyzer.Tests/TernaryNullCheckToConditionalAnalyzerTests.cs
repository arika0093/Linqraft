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
            $@"
using System.Linq;
using System.Collections.Generic;

class Sample
{{
    public Nest? Nest {{ get; set; }}
}}

class Nest
{{
    public int Id {{ get; set; }}
    public string Name {{ get; set; }}
}}

class Test
{{
    void Method()
    {{
        var samples = new List<Sample>();
        var result = samples.AsQueryable().SelectExpr(s => {{|#0:s.Nest != null
            ? new {{
                Id = s.Nest.Id,
                Name = s.Nest.Name
            }}
            : null|}});
    }}
}}

{TestSourceCodes.SelectExprWithFunc}";

        var expected = new DiagnosticResult(
            TernaryNullCheckToConditionalAnalyzer.AnalyzerId,
            DiagnosticSeverity.Info
        ).WithLocation(0);

        await RunAnalyzerTestAsync(test, expected);
    }

    [Fact]
    public async Task Analyzer_DetectsTernaryNullCheckWithAnonymousObjectCreation()
    {
        var test =
            $@"
using System.Linq;
using System.Collections.Generic;

class Sample
{{
    public Nest? Nest {{ get; set; }}
}}

class Nest
{{
    public int Id {{ get; set; }}
}}

class Test
{{
    void Method()
    {{
        var samples = new List<Sample>();
        var result = samples.AsQueryable().SelectExpr(s => {{|#0:s.Nest != null ? new {{ s.Nest.Id }} : null|}});
    }}
}}

{TestSourceCodes.SelectExprWithFunc}";

        var expected = new DiagnosticResult(
            TernaryNullCheckToConditionalAnalyzer.AnalyzerId,
            DiagnosticSeverity.Info
        ).WithLocation(0);

        await RunAnalyzerTestAsync(test, expected);
    }

    [Fact]
    public async Task Analyzer_DoesNotDetectTernaryWithoutObjectCreation()
    {
        var test =
            $@"
using System.Linq;
using System.Collections.Generic;

class Sample
{{
    public Nest? Nest {{ get; set; }}
}}

class Nest
{{
    public int Id {{ get; set; }}
}}

class Test
{{
    void Method()
    {{
        var samples = new List<Sample>();
        var result = samples.AsQueryable().SelectExpr(s => s.Nest != null ? s.Nest.Id : 0);
    }}
}}

{TestSourceCodes.SelectExprWithFunc}";

        await RunAnalyzerTestAsync(test);
    }

    [Fact]
    public async Task Analyzer_DoesNotDetectTernaryWithoutNullCheck()
    {
        var test =
            $@"
using System.Linq;
using System.Collections.Generic;

class Sample
{{
    public int Value {{ get; set; }}
}}

class Test
{{
    void Method()
    {{
        var samples = new List<Sample>();
        var result = samples.AsQueryable().SelectExpr(s => s.Value > 0
            ? new {{ s.Value }}
            : null);
    }}
}}

{TestSourceCodes.SelectExprWithFunc}";

        await RunAnalyzerTestAsync(test);
    }

    [Fact]
    public async Task Analyzer_DetectsTernaryNullCheckWithInvertedCondition()
    {
        // Test inverted condition: x == null ? null : new{}
        var test =
            $@"
using System.Linq;
using System.Collections.Generic;

class Parent
{{
    public Child? Child {{ get; set; }}
}}

class Child
{{
    public string Name {{ get; set; }}
}}

class ChildDto
{{
    public string Name {{ get; set; }}
}}

class Test
{{
    void Method()
    {{
        var parents = new List<Parent>();
        var result = parents.AsQueryable().SelectExpr(p => {{|#0:p.Child == null ? null : new ChildDto {{ Name = p.Child.Name }}|}});
    }}
}}

{TestSourceCodes.SelectExprWithFunc}";

        var expected = new DiagnosticResult(
            TernaryNullCheckToConditionalAnalyzer.AnalyzerId,
            DiagnosticSeverity.Info
        ).WithLocation(0);

        await RunAnalyzerTestAsync(test, expected);
    }

    [Fact]
    public async Task Analyzer_DoesNotDetectTernaryOutsideSelectExpr()
    {
        // Verify that the analyzer does not trigger outside of SelectExpr context
        var test =
            @"
using System.Linq;
using System.Collections.Generic;

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
        // This should NOT trigger LQRS004 because it's not inside SelectExpr
        var result = p.Child == null ? null : new ChildDto { Name = p.Child.Name };
    }
}";

        // No diagnostic expected
        await RunAnalyzerTestAsync(test);
    }

    [Fact]
    public async Task Analyzer_DoesNotDetectTernaryInRegularSelect()
    {
        // Verify that the analyzer does not trigger in regular Select (not SelectExpr)
        var test =
            @"
using System.Linq;
using System.Collections.Generic;

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
        var parents = new List<Parent>();
        // This should NOT trigger LQRS004 because it's in regular Select, not SelectExpr
        var result = parents.AsQueryable().Select(p => p.Child == null ? null : new ChildDto { Name = p.Child.Name });
    }
}";

        // No diagnostic expected
        await RunAnalyzerTestAsync(test);
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
