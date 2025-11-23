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

        var fixedCode =
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
        var result = samples.AsQueryable().SelectExpr(s => new
        {{
            Id = s.Nest?.Id,
            Name = s.Nest?.Name
        }});
    }}
}}

{TestSourceCodes.SelectExprWithFunc}";

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
    public Child? Child {{ get; set; }}
}}

class Child
{{
    public string Name {{ get; set; }}
}}

class Test
{{
    void Method()
    {{
        var samples = new List<Sample>();
        var result = samples.AsQueryable().SelectExpr(s => {{|#0:s.Nest != null && s.Nest.Child != null
            ? new {{
                Id = s.Nest.Id,
                ChildName = s.Nest.Child.Name
            }}
            : null|}});
    }}
}}

{TestSourceCodes.SelectExprWithFunc}";

        var fixedCode =
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
    public Child? Child {{ get; set; }}
}}

class Child
{{
    public string Name {{ get; set; }}
}}

class Test
{{
    void Method()
    {{
        var samples = new List<Sample>();
        var result = samples.AsQueryable().SelectExpr(s => new
        {{
            Id = s.Nest?.Id,
            ChildName = s.Nest?.Child?.Name
        }});
    }}
}}

{TestSourceCodes.SelectExprWithFunc}";

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
        var result = samples.AsQueryable().SelectExpr(s => {{|#0:s.Nest != null
            ? (object?)new {{
                Id = s.Nest.Id
            }}
            : null|}});
    }}
}}

{TestSourceCodes.SelectExprWithFunc}";

        var fixedCode =
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
        var result = samples.AsQueryable().SelectExpr(s => new
        {{
            Id = s.Nest?.Id
        }});
    }}
}}

{TestSourceCodes.SelectExprWithFunc}";

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

        var fixedCode =
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
        var result = samples.AsQueryable().SelectExpr(s => new
        {{
            Id = s.Nest?.Id,
            Name = s.Nest?.Name
        }});
    }}
}}

{TestSourceCodes.SelectExprWithFunc}";

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
        var result = parents.AsQueryable().SelectExpr(p => {{|#0:p.Child == null 
            ? null 
            : new ChildDto {{
                Name = p.Child.Name
            }}|}});
    }}
}}

{TestSourceCodes.SelectExprWithFunc}";

        var fixedCode =
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
        var result = parents.AsQueryable().SelectExpr(p => new ChildDto
        {{
            Name = p.Child?.Name
        }});
    }}
}}

{TestSourceCodes.SelectExprWithFunc}";

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
