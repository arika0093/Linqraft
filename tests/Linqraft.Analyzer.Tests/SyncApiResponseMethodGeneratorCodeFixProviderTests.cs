using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;

namespace Linqraft.Analyzer.Tests;

public class SyncApiResponseMethodGeneratorCodeFixProviderTests
{
    [Fact]
    public async Task CodeFix_VoidMethod_ConvertsToSyncApiResponseMethod()
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

class Item
{
    public int Id { get; set; }
    public string Name { get; set; }
}

class Test
{
    List<ItemsDto_T27C3JAA> GetItems()
    {
        var list = new List<Item>();
        return list.AsQueryable()
            .Where(i => i.Id > 0)
            .SelectExpr<Item, ItemsDto_T27C3JAA>(i => new { i.Id, i.Name }).ToList();
    }
}";

        var expected = new DiagnosticResult(
            SyncApiResponseMethodGeneratorAnalyzer.AnalyzerId,
            DiagnosticSeverity.Info
        ).WithLocation(0);

        await RunCodeFixTestAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task CodeFix_VoidMethod_WithSingleProperty_ConvertsToSyncApiResponseMethod()
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
    void {|#0:GetItems|}()
    {
        var list = new List<Item>();
        list.AsQueryable().Select(i => new { i.Id });
    }
}";

        var fixedCode =
            @"
using System.Linq;
using System.Collections.Generic;

class Item
{
    public int Id { get; set; }
}

class Test
{
    List<ItemsDto_REIXTLBA> GetItems()
    {
        var list = new List<Item>();
        return list.AsQueryable().SelectExpr<Item, ItemsDto_REIXTLBA>(i => new { i.Id }).ToList();
    }
}";

        var expected = new DiagnosticResult(
            SyncApiResponseMethodGeneratorAnalyzer.AnalyzerId,
            DiagnosticSeverity.Info
        ).WithLocation(0);

        await RunCodeFixTestAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task CodeFix_VoidMethod_WithChainedOperations_ConvertsToSyncApiResponseMethod()
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
    void {|#0:GetActiveItems|}()
    {
        var items = new List<Item>();
        items.AsQueryable()
            .Where(i => i.Id > 0)
            .OrderBy(i => i.Name)
            .Select(i => new { i.Id, i.Name });
    }
}";

        var fixedCode =
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
    List<ActiveItemsDto_T27C3JAA> GetActiveItems()
    {
        var items = new List<Item>();
        return items.AsQueryable()
            .Where(i => i.Id > 0)
            .OrderBy(i => i.Name)
            .SelectExpr<Item, ActiveItemsDto_T27C3JAA>(i => new { i.Id, i.Name }).ToList();
    }
}";

        var expected = new DiagnosticResult(
            SyncApiResponseMethodGeneratorAnalyzer.AnalyzerId,
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
            SyncApiResponseMethodGeneratorAnalyzer,
            SyncApiResponseMethodGeneratorCodeFixProvider,
            DefaultVerifier
        >
        {
            TestCode = source,
            FixedCode = fixedSource,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
            // Allow compiler errors for undefined SelectExpr and ToList (they will be available at runtime)
            CompilerDiagnostics = CompilerDiagnostics.None,
        };

        test.ExpectedDiagnostics.Add(expected);

        await test.RunAsync();
    }
}
