using System.Threading.Tasks;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixVerifier<
    Linqraft.Analyzer.NestedSelectExprPartialDtoAnalyzer,
    Linqraft.Analyzer.NestedSelectExprPartialDtoCodeFixProvider,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier
>;

namespace Linqraft.Analyzer.Tests;

public class NestedSelectExprPartialDtoCodeFixProviderTests
{
    [Fact]
    public async Task NestedSelectExpr_AddsPartialDeclarations()
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
    public List<Item> Items { get; set; }
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
        var result = list.AsQueryable().{|#0:SelectExpr<Entity, EntityDto>(e => new
        {
            e.Id,
            e.Name,
            Items = e.Items.SelectExpr<Item, ItemDto>(i => new
            {
                i.Id,
                i.Title
            })
        })|};
    }
}

" + TestSourceCodes.SelectExprWithFuncObjectInLinqraftNamespace;

        var fixedCode =
            @"
using System.Linq;
using System.Collections.Generic;
using Linqraft;

class Entity
{
    public int Id { get; set; }
    public string Name { get; set; }
    public List<Item> Items { get; set; }
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

        var expected = VerifyCS
            .Diagnostic(NestedSelectExprPartialDtoAnalyzer.AnalyzerId)
            .WithLocation(0)
            .WithArguments("EntityDto, ItemDto");

        await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task NestedSelectExpr_AddsOnlyMissingPartialDeclarations()
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
    public List<Item> Items { get; set; }
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
        var result = list.AsQueryable().{|#0:SelectExpr<Entity, EntityDto>(e => new
        {
            e.Id,
            e.Name,
            Items = e.Items.SelectExpr<Item, ItemDto>(i => new
            {
                i.Id,
                i.Title
            })
        })|};
    }
}

internal partial class EntityDto;

" + TestSourceCodes.SelectExprWithFuncObjectInLinqraftNamespace;

        var fixedCode =
            @"
using System.Linq;
using System.Collections.Generic;
using Linqraft;

class Entity
{
    public int Id { get; set; }
    public string Name { get; set; }
    public List<Item> Items { get; set; }
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

        var expected = VerifyCS
            .Diagnostic(NestedSelectExprPartialDtoAnalyzer.AnalyzerId)
            .WithLocation(0)
            .WithArguments("ItemDto");

        await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task MultipleNestedSelectExpr_AddsAllPartialDeclarations()
    {
        var test =
            @"
using System.Linq;
using System.Collections.Generic;
using Linqraft;

class Entity
{
    public int Id { get; set; }
    public List<Item> Items { get; set; }
}

class Item
{
    public int Id { get; set; }
    public List<SubItem> SubItems { get; set; }
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
        var result = list.AsQueryable().{|#0:SelectExpr<Entity, EntityDto>(e => new
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
        })|};
    }
}

" + TestSourceCodes.SelectExprWithFuncObjectInLinqraftNamespace;

        var fixedCode =
            @"
using System.Linq;
using System.Collections.Generic;
using Linqraft;

class Entity
{
    public int Id { get; set; }
    public List<Item> Items { get; set; }
}

class Item
{
    public int Id { get; set; }
    public List<SubItem> SubItems { get; set; }
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

internal partial class EntityDto;
internal partial class ItemDto;
internal partial class SubItemDto;

" + TestSourceCodes.SelectExprWithFuncObjectInLinqraftNamespace;

        var expected = VerifyCS
            .Diagnostic(NestedSelectExprPartialDtoAnalyzer.AnalyzerId)
            .WithLocation(0)
            .WithArguments("EntityDto, ItemDto, SubItemDto");

        await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task NestedSelectExpr_InNamespace_AddsPartialDeclarationsInNamespace()
    {
        var test =
            @"
using System.Linq;
using System.Collections.Generic;
using Linqraft;

namespace MyNamespace
{
    class Entity
    {
        public int Id { get; set; }
        public List<Item> Items { get; set; }
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
            var result = list.AsQueryable().{|#0:SelectExpr<Entity, EntityDto>(e => new
            {
                e.Id,
                Items = e.Items.SelectExpr<Item, ItemDto>(i => new { i.Id })
            })|};
        }
    }
}

" + TestSourceCodes.SelectExprWithFuncObjectInLinqraftNamespace;

        var fixedCode =
            @"
using System.Linq;
using System.Collections.Generic;
using Linqraft;

namespace MyNamespace
{
    class Entity
    {
        public int Id { get; set; }
        public List<Item> Items { get; set; }
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
                Items = e.Items.SelectExpr<Item, ItemDto>(i => new { i.Id })
            });
        }
    }

    internal partial class EntityDto;
    internal partial class ItemDto;
}

" + TestSourceCodes.SelectExprWithFuncObjectInLinqraftNamespace;

        var expected = VerifyCS
            .Diagnostic(NestedSelectExprPartialDtoAnalyzer.AnalyzerId)
            .WithLocation(0)
            .WithArguments("EntityDto, ItemDto");

        await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
    }
}
