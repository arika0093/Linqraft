using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;

namespace Linqraft.Analyzer.Tests;

public class GroupByAnonymousKeyCodeFixProviderTests
{
    [Fact]
    public async Task CodeFix_ConvertAnonymousKeyToNamedClass()
    {
        var test =
            $@"
using System.Linq;
using System.Collections.Generic;

namespace TestNamespace
{{
    class Entity
    {{
        public int CategoryId {{ get; set; }}
        public string CategoryType {{ get; set; }}
    }}

    class Test
    {{
        void Method()
        {{
            var entities = new List<Entity>().AsQueryable();
            var result = entities
                .GroupBy(e => {{|#0:new {{ e.CategoryId, e.CategoryType }}|}})
                .SelectExpr(g => new
                {{
                    CategoryId = g.Key.CategoryId,
                    CategoryType = g.Key.CategoryType,
                    Count = g.Count(),
                }})
                .ToList();
        }}
    }}
}}

{TestSourceCodes.SelectExprWithExpression}";

        var fixedCode =
            $@"using System.Linq;
using System.Collections.Generic;

namespace TestNamespace
{{
    class Entity
    {{
        public int CategoryId {{ get; set; }}
        public string CategoryType {{ get; set; }}
    }}

    class Test
    {{
        void Method()
        {{
            var entities = new List<Entity>().AsQueryable();
            var result = entities
                .GroupBy(e => new EntitiesGroupKey {{ CategoryId = e.CategoryId, CategoryType = e.CategoryType }})
                .SelectExpr(g => new
                {{
                    CategoryId = g.Key.CategoryId,
                    CategoryType = g.Key.CategoryType,
                    Count = g.Count(),
                }})
                .ToList();
        }}
    }}

    public partial class EntitiesGroupKey
    {{
        public required int CategoryId {{ get; set; }}
        public required string CategoryType {{ get; set; }}
    }}
}}

{TestSourceCodes.SelectExprWithExpression}";

        var expected = new DiagnosticResult(
            GroupByAnonymousKeyAnalyzer.AnalyzerId,
            DiagnosticSeverity.Error
        ).WithLocation(0);

        await RunCodeFixTestAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task CodeFix_ConvertAnonymousKeyWithExplicitPropertyNames()
    {
        var test =
            $@"
using System.Linq;
using System.Collections.Generic;

namespace TestNamespace
{{
    class Entity
    {{
        public int Id {{ get; set; }}
        public string Type {{ get; set; }}
    }}

    class Test
    {{
        void Method()
        {{
            var entities = new List<Entity>().AsQueryable();
            var result = entities
                .GroupBy(e => {{|#0:new {{ CategoryId = e.Id, CategoryType = e.Type }}|}})
                .SelectExpr(g => new
                {{
                    g.Key.CategoryId,
                    g.Key.CategoryType,
                    Count = g.Count(),
                }})
                .ToList();
        }}
    }}
}}

{TestSourceCodes.SelectExprWithExpression}";

        // The class name is derived from the source variable name "entities" -> "Entity"
        var fixedCode =
            $@"using System.Linq;
using System.Collections.Generic;

namespace TestNamespace
{{
    class Entity
    {{
        public int Id {{ get; set; }}
        public string Type {{ get; set; }}
    }}

    class Test
    {{
        void Method()
        {{
            var entities = new List<Entity>().AsQueryable();
            var result = entities
                .GroupBy(e => new EntitiesGroupKey {{ CategoryId = e.Id, CategoryType = e.Type }})
                .SelectExpr(g => new
                {{
                    g.Key.CategoryId,
                    g.Key.CategoryType,
                    Count = g.Count(),
                }})
                .ToList();
        }}
    }}

    public partial class EntitiesGroupKey
    {{
        public required int CategoryId {{ get; set; }}
        public required string CategoryType {{ get; set; }}
    }}
}}

{TestSourceCodes.SelectExprWithExpression}";

        var expected = new DiagnosticResult(
            GroupByAnonymousKeyAnalyzer.AnalyzerId,
            DiagnosticSeverity.Error
        ).WithLocation(0);

        await RunCodeFixTestAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task CodeFix_ConvertAnonymousKeyInGlobalNamespace()
    {
        var test =
            $@"
using System.Linq;
using System.Collections.Generic;

class Entity
{{
    public int CategoryId {{ get; set; }}
    public string CategoryType {{ get; set; }}
}}

class Test
{{
    void Method()
    {{
        var entities = new List<Entity>().AsQueryable();
        var result = entities
            .GroupBy(e => {{|#0:new {{ e.CategoryId, e.CategoryType }}|}})
            .SelectExpr(g => new
            {{
                CategoryId = g.Key.CategoryId,
                Count = g.Count(),
            }})
            .ToList();
    }}
}}

{TestSourceCodes.SelectExprWithExpression}";

        var fixedCode =
            $@"using System.Linq;
using System.Collections.Generic;

class Entity
{{
    public int CategoryId {{ get; set; }}
    public string CategoryType {{ get; set; }}
}}

class Test
{{
    void Method()
    {{
        var entities = new List<Entity>().AsQueryable();
        var result = entities
            .GroupBy(e => new EntitiesGroupKey {{ CategoryId = e.CategoryId, CategoryType = e.CategoryType }})
            .SelectExpr(g => new
            {{
                CategoryId = g.Key.CategoryId,
                Count = g.Count(),
            }})
            .ToList();
    }}
}}

{TestSourceCodes.SelectExprWithExpression}

public partial class EntitiesGroupKey
{{
    public required int CategoryId {{ get; set; }}
    public required string CategoryType {{ get; set; }}
}}";

        var expected = new DiagnosticResult(
            GroupByAnonymousKeyAnalyzer.AnalyzerId,
            DiagnosticSeverity.Error
        ).WithLocation(0);

        await RunCodeFixTestAsync(test, expected, fixedCode);
    }

    private static async Task RunCodeFixTestAsync(
        string source,
        DiagnosticResult[] expected,
        string fixedSource
    )
    {
        // Normalize line endings to LF to avoid CRLF/LF mismatch issues
        static string NormalizeLineEndings(string text) => text.Replace("\r\n", "\n");

        var test = new CSharpCodeFixTest<
            GroupByAnonymousKeyAnalyzer,
            GroupByAnonymousKeyCodeFixProvider,
            DefaultVerifier
        >
        {
            TestCode = NormalizeLineEndings(source),
            FixedCode = NormalizeLineEndings(fixedSource),
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };

        test.ExpectedDiagnostics.AddRange(expected);

        await test.RunAsync();
    }

    private static async Task RunCodeFixTestAsync(
        string source,
        DiagnosticResult expected,
        string fixedSource
    ) => await RunCodeFixTestAsync(source, [expected], fixedSource);
}
