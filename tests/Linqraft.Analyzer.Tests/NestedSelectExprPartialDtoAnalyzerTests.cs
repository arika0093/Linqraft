using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    Linqraft.Analyzer.NestedSelectExprPartialDtoAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier
>;

namespace Linqraft.Analyzer.Tests;

public class NestedSelectExprPartialDtoAnalyzerTests
{
    [Fact]
    public async Task NestedSelectExpr_WithoutPartialDeclarations_ReportsDiagnostic()
    {
        var test =
            @"
using System.Linq;
using System.Collections.Generic;
using Linqraft;
using Linqraft;

class Entity
{
    public int Id { get; set; }
    public string Name { get; set; }
    public IQueryable<Item> Items { get; set; }
}

class Item
{
    public int Id { get; set; }
    public string Title { get; set; }
}

class Test
{
    void Method()
    {
        var list = new List<Entity>();
        var result = list.AsQueryable().SelectExpr<Entity, EntityDto>(e => new
        {
            e.Id,
            e.Name,
            Items = e.Items.SelectExpr<Item, ItemDto>(i => new
            {
                i.Id,
                i.Title
            })
        });
    }
}

// Non-partial classes - should trigger analyzer error
class EntityDto { }
class ItemDto { }

" + TestSourceCodes.SelectExprWithFuncObjectInLinqraftNamespace;

        var expected = VerifyCS
            .Diagnostic(NestedSelectExprPartialDtoAnalyzer.AnalyzerId)
            
            .WithArguments("EntityDto, ItemDto")
            .WithSeverity(DiagnosticSeverity.Error);

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task NestedSelectExpr_WithAllPartialDeclarations_NoDiagnostic()
    {
        var test =
            @"
using System.Linq;
using System.Collections.Generic;
using Linqraft;

class Entity
{
    public int Id { get; set; }
    public string Name { get; set; }
    public IQueryable<Item> Items { get; set; }
}

class Item
{
    public int Id { get; set; }
    public string Title { get; set; }
}

class Test
{
    void Method()
    {
        var list = new List<Entity>();
        var result = list.AsQueryable().SelectExpr<Entity, EntityDto>(e => new
        {
            e.Id,
            e.Name,
            Items = e.Items.SelectExpr<Item, ItemDto>(i => new
            {
                i.Id,
                i.Title
            })
        });
    }
}

internal partial class EntityDto;
internal partial class ItemDto;

" + TestSourceCodes.SelectExprWithFuncObjectInLinqraftNamespace;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task NestedSelectExpr_WithSomePartialDeclarations_ReportsDiagnostic()
    {
        var test =
            @"
using System.Linq;
using System.Collections.Generic;
using Linqraft;

class Entity
{
    public int Id { get; set; }
    public string Name { get; set; }
    public IQueryable<Item> Items { get; set; }
}

class Item
{
    public int Id { get; set; }
    public string Title { get; set; }
}

class Test
{
    void Method()
    {
        var list = new List<Entity>();
        var result = list.AsQueryable().SelectExpr<Entity, EntityDto>(e => new
        {
            e.Id,
            e.Name,
            Items = e.Items.SelectExpr<Item, ItemDto>(i => new
            {
                i.Id,
                i.Title
            })
        });
    }
}

internal partial class EntityDto;

" + TestSourceCodes.SelectExprWithFuncObjectInLinqraftNamespace;

        var expected = VerifyCS
            .Diagnostic(NestedSelectExprPartialDtoAnalyzer.AnalyzerId)
            
            .WithArguments("ItemDto")
            .WithSeverity(DiagnosticSeverity.Error);

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task MultipleNestedSelectExpr_WithoutPartialDeclarations_ReportsDiagnostic()
    {
        var test =
            @"
using System.Linq;
using System.Collections.Generic;
using Linqraft;

class Entity
{
    public int Id { get; set; }
    public IQueryable<Item> Items { get; set; }
}

class Item
{
    public int Id { get; set; }
    public IQueryable<SubItem> SubItems { get; set; }
}

class SubItem
{
    public int Value { get; set; }
}

class Test
{
    void Method()
    {
        var list = new List<Entity>();
        var result = list.AsQueryable().SelectExpr<Entity, EntityDto>(e => new
        {
            e.Id,
            Items = e.Items.SelectExpr<Item, ItemDto>(i => new
            {
                i.Id,
                SubItems = i.SubItems.SelectExpr<SubItem, SubItemDto>(si => new
                {
                    si.Value
                })
            })
        });
    }
}

" + TestSourceCodes.SelectExprWithFuncObjectInLinqraftNamespace;

        var expected = VerifyCS
            .Diagnostic(NestedSelectExprPartialDtoAnalyzer.AnalyzerId)
            
            .WithArguments("EntityDto, ItemDto, SubItemDto")
            .WithSeverity(DiagnosticSeverity.Error);

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task SelectExpr_WithoutNestedSelectExpr_NoDiagnostic()
    {
        var test =
            @"
using System.Linq;
using System.Collections.Generic;
using Linqraft;

class Entity
{
    public int Id { get; set; }
    public string Name { get; set; }
}

class Test
{
    void Method()
    {
        var list = new List<Entity>();
        var result = list.AsQueryable().SelectExpr<Entity, EntityDto>(e => new
        {
            e.Id,
            e.Name
        });
    }
}

// Stub type to avoid compiler error
class EntityDto { }

" + TestSourceCodes.SelectExprWithFuncObjectInLinqraftNamespace;

        // No diagnostic expected - outer SelectExpr without nested SelectExpr doesn't require partial declarations
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task SelectExpr_WithRegularSelect_NoDiagnostic()
    {
        var test =
            @"
using System.Linq;
using System.Collections.Generic;
using Linqraft;

class Entity
{
    public int Id { get; set; }
    public IQueryable<Item> Items { get; set; }
}

class Item
{
    public int Id { get; set; }
}

class Test
{
    void Method()
    {
        var list = new List<Entity>();
        var result = list.AsQueryable().SelectExpr<Entity, EntityDto>(e => new
        {
            e.Id,
            Items = e.Items.Select(i => new { i.Id })
        });
    }
}

" + TestSourceCodes.SelectExprWithFuncObjectInLinqraftNamespace;

        // No diagnostic expected - only nested SelectExpr triggers the requirement
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task SelectExpr_WithAnonymousType_NoDiagnostic()
    {
        var test =
            @"
using System.Linq;
using System.Collections.Generic;
using Linqraft;

class Entity
{
    public int Id { get; set; }
    public string Name { get; set; }
}

class Test
{
    void Method()
    {
        var list = new List<Entity>();
        var result = list.AsQueryable().SelectExpr(e => new
        {
            e.Id,
            e.Name
        });
    }
}

" + TestSourceCodes.SelectExprWithFuncInLinqraftNamespace;

        // No diagnostic expected - SelectExpr without explicit type doesn't require partial declarations
        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
