using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;

namespace Linqraft.Analyzer.Tests;

public class ApiResponseMethodGeneratorCodeFixProviderTests
{
    [Fact]
    public async Task CodeFix_VoidMethod_ConvertsToAsyncApiResponseMethod()
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

        var fixedCode =
            @"
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

class Item
{
    public int Id { get; set; }
    public string Name { get; set; }
}

class Test
{
    async Task<List<ItemsDto_T27C3JAA>> GetItemsAsync()
    {
        var list = new List<Item>();
        return await list.AsQueryable()
            .Where(i => i.Id > 0)
            .SelectExpr<Item, ItemsDto_T27C3JAA>(i => new { i.Id, i.Name }).ToListAsync();
    }
}";

        var expected = new DiagnosticResult(
            ApiResponseMethodGeneratorAnalyzer.AnalyzerId,
            DiagnosticSeverity.Info
        ).WithLocation(0);

        await RunCodeFixTestAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task CodeFix_TaskMethod_ConvertsToAsyncApiResponseMethod()
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
    async Task {|#0:GetItems|}()
    {
        var list = new List<Item>();
        await list.AsQueryable()
            .Select(i => new { i.Id, i.Name });
    }
}";

        var fixedCode =
            @"
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

class Item
{
    public int Id { get; set; }
    public string Name { get; set; }
}

class Test
{
    async Task<List<ItemsDto_T27C3JAA>> GetItemsAsync()
    {
        var list = new List<Item>();
        return await list.AsQueryable()
            .SelectExpr<Item, ItemsDto_T27C3JAA>(i => new { i.Id, i.Name }).ToListAsync();
    }
}";

        var expected = new DiagnosticResult(
            ApiResponseMethodGeneratorAnalyzer.AnalyzerId,
            DiagnosticSeverity.Info
        ).WithLocation(0);

        await RunCodeFixTestAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task CodeFix_MethodAlreadyEndsWithAsync_DoesNotDuplicateSuffix()
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

class Test
{
    async Task {|#0:GetItemsAsync|}()
    {
        var list = new List<Item>();
        await list.AsQueryable().Select(i => new { i.Id });
    }
}";

        var fixedCode =
            @"
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

class Item
{
    public int Id { get; set; }
}

class Test
{
    async Task<List<ItemsAsyncDto_REIXTLBA>> GetItemsAsync()
    {
        var list = new List<Item>();
        return await list.AsQueryable().SelectExpr<Item, ItemsAsyncDto_REIXTLBA>(i => new { i.Id }).ToListAsync();
    }
}";

        var expected = new DiagnosticResult(
            ApiResponseMethodGeneratorAnalyzer.AnalyzerId,
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
            ApiResponseMethodGeneratorAnalyzer,
            ApiResponseMethodGeneratorCodeFixProvider,
            DefaultVerifier
        >
        {
            TestCode = source,
            FixedCode = fixedSource,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
            // Allow compiler errors for undefined SelectExpr and ToListAsync (they will be available at runtime)
            CompilerDiagnostics = CompilerDiagnostics.None,
        };

        test.ExpectedDiagnostics.Add(expected);

        await test.RunAsync();
    }
}
