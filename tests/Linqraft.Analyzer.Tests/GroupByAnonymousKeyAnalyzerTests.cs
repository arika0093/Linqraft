using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    Linqraft.Analyzer.GroupByAnonymousKeyAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier
>;

namespace Linqraft.Analyzer.Tests;

public class GroupByAnonymousKeyAnalyzerTests
{
    [Fact]
    public async Task GroupByWithAnonymousKeyFollowedBySelectExpr_ReportsDiagnostic()
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
                CategoryType = g.Key.CategoryType,
                Count = g.Count(),
            }})
            .ToList();
    }}
}}

{TestSourceCodes.SelectExprWithExpression}";

        var expected = VerifyCS
            .Diagnostic(GroupByAnonymousKeyAnalyzer.AnalyzerId)
            .WithLocation(0)
            .WithSeverity(DiagnosticSeverity.Error);

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task GroupByWithAnonymousKeyWithIntermediateMethods_ReportsDiagnostic()
    {
        var test =
            $@"
using System.Linq;
using System.Collections.Generic;

class Entity
{{
    public int CategoryId {{ get; set; }}
    public string CategoryType {{ get; set; }}
    public bool IsActive {{ get; set; }}
}}

class Test
{{
    void Method()
    {{
        var entities = new List<Entity>().AsQueryable();
        var result = entities
            .GroupBy(e => {{|#0:new {{ e.CategoryId, e.CategoryType }}|}})
            .Where(g => g.Count() > 0)
            .SelectExpr(g => new
            {{
                CategoryId = g.Key.CategoryId,
                Count = g.Count(),
            }})
            .ToList();
    }}
}}

{TestSourceCodes.SelectExprWithExpression}";

        var expected = VerifyCS
            .Diagnostic(GroupByAnonymousKeyAnalyzer.AnalyzerId)
            .WithLocation(0)
            .WithSeverity(DiagnosticSeverity.Error);

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task GroupByWithAnonymousKeyWithoutSelectExpr_NoDiagnostic()
    {
        var test =
            @"
using System.Linq;
using System.Collections.Generic;

class Entity
{
    public int CategoryId { get; set; }
    public string CategoryType { get; set; }
}

class Test
{
    void Method()
    {
        var entities = new List<Entity>().AsQueryable();
        var result = entities
            .GroupBy(e => new { e.CategoryId, e.CategoryType })
            .Select(g => new
            {
                CategoryId = g.Key.CategoryId,
                CategoryType = g.Key.CategoryType,
                Count = g.Count(),
            })
            .ToList();
    }
}";

        // No diagnostic because Select is used instead of SelectExpr
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task GroupByWithNamedKeyFollowedBySelectExpr_NoDiagnostic()
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

class EntitiesGroupKey
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

{TestSourceCodes.SelectExprWithExpression}";

        // No diagnostic because a named type is used as the key
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task GroupByWithSinglePropertyKey_NoDiagnostic()
    {
        var test =
            $@"
using System.Linq;
using System.Collections.Generic;

class Entity
{{
    public int CategoryId {{ get; set; }}
    public string Name {{ get; set; }}
}}

class Test
{{
    void Method()
    {{
        var entities = new List<Entity>().AsQueryable();
        var result = entities
            .GroupBy(e => e.CategoryId)
            .SelectExpr(g => new
            {{
                CategoryId = g.Key,
                Count = g.Count(),
            }})
            .ToList();
    }}
}}

{TestSourceCodes.SelectExprWithExpression}";

        // No diagnostic because a single property (not anonymous type) is used as the key
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task SelectExprWithoutGroupBy_NoDiagnostic()
    {
        var test =
            $@"
using System.Linq;
using System.Collections.Generic;

class Entity
{{
    public int Id {{ get; set; }}
    public string Name {{ get; set; }}
}}

class Test
{{
    void Method()
    {{
        var entities = new List<Entity>().AsQueryable();
        var result = entities
            .Where(e => e.Id > 0)
            .SelectExpr(e => new
            {{
                e.Id,
                e.Name,
            }})
            .ToList();
    }}
}}

{TestSourceCodes.SelectExprWithExpression}";

        // No diagnostic because there's no GroupBy
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task GroupByWithAnonymousKeyFollowedBySelectExprWithTypeArgs_ReportsDiagnostic()
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

class GroupResultDto
{{
    public int CategoryId {{ get; set; }}
    public int Count {{ get; set; }}
}}

class Test
{{
    void Method()
    {{
        var entities = new List<Entity>().AsQueryable();
        var result = entities
            .GroupBy(e => {{|#0:new {{ e.CategoryId, e.CategoryType }}|}})
            .SelectExpr<System.Linq.IGrouping<object, Entity>, GroupResultDto>(g => new
            {{
                CategoryId = g.Key.GetHashCode(), // workaround to access something from Key
                Count = g.Count(),
            }})
            .ToList();
    }}
}}

{TestSourceCodes.SelectExprWithExpressionObject}";

        var expected = VerifyCS
            .Diagnostic(GroupByAnonymousKeyAnalyzer.AnalyzerId)
            .WithLocation(0)
            .WithSeverity(DiagnosticSeverity.Error);

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task GroupByWithAnonymousKeyFollowedByPredefinedSelectExpr_ReportsDiagnostic()
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

class GroupResultDto
{{
    public int CategoryId {{ get; set; }}
    public string CategoryType {{ get; set; }}
    public int Count {{ get; set; }}
}}

class Test
{{
    void Method()
    {{
        var entities = new List<Entity>().AsQueryable();
        var result = entities
            .GroupBy(e => {{|#0:new {{ e.CategoryId, e.CategoryType }}|}})
            .SelectExpr(g => new GroupResultDto
            {{
                CategoryId = g.Key.CategoryId,
                CategoryType = g.Key.CategoryType,
                Count = g.Count(),
            }})
            .ToList();
    }}
}}

{TestSourceCodes.SelectExprWithExpression}";

        var expected = VerifyCS
            .Diagnostic(GroupByAnonymousKeyAnalyzer.AnalyzerId)
            .WithLocation(0)
            .WithSeverity(DiagnosticSeverity.Error);

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task GroupByWithNamedKeyFollowedByPredefinedSelectExpr_NoDiagnostic()
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

class EntitiesGroupKey
{{
    public int CategoryId {{ get; set; }}
    public string CategoryType {{ get; set; }}
}}

class GroupResultDto
{{
    public int CategoryId {{ get; set; }}
    public string CategoryType {{ get; set; }}
    public int Count {{ get; set; }}
}}

class Test
{{
    void Method()
    {{
        var entities = new List<Entity>().AsQueryable();
        var result = entities
            .GroupBy(e => new EntitiesGroupKey {{ CategoryId = e.CategoryId, CategoryType = e.CategoryType }})
            .SelectExpr(g => new GroupResultDto
            {{
                CategoryId = g.Key.CategoryId,
                CategoryType = g.Key.CategoryType,
                Count = g.Count(),
            }})
            .ToList();
    }}
}}

{TestSourceCodes.SelectExprWithExpression}";

        // No diagnostic because a named type is used as the key
        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
