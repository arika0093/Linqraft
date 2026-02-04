using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    Linqraft.Analyzer.ApiResponseMethodGeneratorAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier
>;

namespace Linqraft.Analyzer.Tests;

public class ApiResponseMethodGeneratorAnalyzerTests
{
    [Fact]
    public async Task VoidMethod_WithUnassignedSelectAnonymousType_ReportsDiagnostic()
    {
        var test =
            @"
using System.Linq;
using System.Collections.Generic;

class Item
{
    public int Id { get; set; }
    public string Name { get; set; }
}

class Test
{
    void {|#0:GetItems|}()
    {
        var list = new List<Item>();
        list.AsQueryable()
            .Where(i => i.Id > 0)
            .Select(i => new { i.Id, i.Name });
    }
}";

        var expected = VerifyCS
            .Diagnostic(ApiResponseMethodGeneratorAnalyzer.AnalyzerId)
            .WithLocation(0)
            .WithSeverity(DiagnosticSeverity.Info)
            .WithArguments("GetItems");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task TaskMethod_WithUnassignedSelectAnonymousType_ReportsDiagnostic()
    {
        var test =
            @"
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;

class Item
{
    public int Id { get; set; }
    public string Name { get; set; }
}

class Test
{
    Task {|#0:GetItems|}()
    {
        var list = new List<Item>();
        list.AsQueryable()
            .Where(i => i.Id > 0)
            .Select(i => new { i.Id, i.Name });
        return Task.CompletedTask;
    }
}";

        var expected = VerifyCS
            .Diagnostic(ApiResponseMethodGeneratorAnalyzer.AnalyzerId)
            .WithLocation(0)
            .WithSeverity(DiagnosticSeverity.Info)
            .WithArguments("GetItems");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task VoidMethod_WithAssignedSelect_NoDiagnostic()
    {
        var test =
            @"
using System.Linq;
using System.Collections.Generic;

class Item
{
    public int Id { get; set; }
}

class Test
{
    void GetItems()
    {
        var list = new List<Item>();
        var result = list.AsQueryable().Select(i => new { i.Id });
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TaskOfTMethod_WithSelect_NoDiagnostic()
    {
        var test =
            @"
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;

class Item
{
    public int Id { get; set; }
}

class ItemDto
{
    public int Id { get; set; }
}

class Test
{
    async Task<List<ItemDto>> GetItems()
    {
        var list = new List<Item>();
        return await Task.FromResult(list.Select(i => new ItemDto { Id = i.Id }).ToList());
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task VoidMethod_WithSelectNoAnonymousType_NoDiagnostic()
    {
        var test =
            @"
using System.Linq;
using System.Collections.Generic;

class Item
{
    public int Id { get; set; }
}

class Test
{
    void GetItems()
    {
        var list = new List<Item>();
        list.AsQueryable().Select(i => i.Id);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task VoidMethod_WithIEnumerableSelect_NoDiagnostic()
    {
        var test =
            @"
using System.Linq;
using System.Collections.Generic;

class Item
{
    public int Id { get; set; }
}

class Test
{
    void GetItems()
    {
        var list = new List<Item>();
        list.Select(i => new { i.Id });
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task VoidMethod_WithNamedTypeSelect_NoDiagnostic()
    {
        var test =
            @"
using System.Linq;
using System.Collections.Generic;

class Item
{
    public int Id { get; set; }
}

class ItemDto
{
    public int Id { get; set; }
}

class Test
{
    void GetItems()
    {
        var list = new List<Item>();
        list.AsQueryable().Select(i => new ItemDto { Id = i.Id });
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task VoidMethod_MultipleSelects_OnlyUnassignedReportsDiagnostic()
    {
        var test =
            @"
using System.Linq;
using System.Collections.Generic;

class Item
{
    public int Id { get; set; }
    public string Name { get; set; }
}

class Test
{
    void {|#0:GetItems|}()
    {
        var list = new List<Item>();
        
        // This is assigned, should not trigger
        var assigned = list.AsQueryable().Select(i => new { i.Id });
        
        // This is unassigned, should trigger
        list.AsQueryable().Select(i => new { i.Id, i.Name });
    }
}";

        var expected = VerifyCS
            .Diagnostic(ApiResponseMethodGeneratorAnalyzer.AnalyzerId)
            .WithLocation(0)
            .WithSeverity(DiagnosticSeverity.Info)
            .WithArguments("GetItems");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }
}
