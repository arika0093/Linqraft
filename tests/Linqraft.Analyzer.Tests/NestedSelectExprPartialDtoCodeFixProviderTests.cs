using System.Threading.Tasks;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixVerifier<
    Linqraft.Analyzer.NestedSelectExprPartialDtoAnalyzer,
    Linqraft.Analyzer.NestedSelectExprPartialDtoCodeFixProvider,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier
>;

namespace Linqraft.Analyzer.Tests;

public class NestedSelectExprPartialDtoCodeFixProviderTests
{
    private const string TestSetup = @"
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

";

    [Fact]
    public async Task NestedSelectExpr_AddsPartialDeclarations()
    {
        var test = TestSetup + @"
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

class EntityDto { }
class ItemDto { }

" + TestSourceCodes.SelectExprWithFuncObjectInLinqraftNamespace;

        var fixedCode = TestSetup + @"
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

class EntityDto { }
class ItemDto { }
internal partial class EntityDto;
internal partial class ItemDto;

" + TestSourceCodes.SelectExprWithFuncObjectInLinqraftNamespace;

        var expected = VerifyCS
            .Diagnostic(NestedSelectExprPartialDtoAnalyzer.AnalyzerId)
            
            .WithArguments("EntityDto, ItemDto");

        await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task NestedSelectExpr_AddsOnlyMissingPartialDeclarations()
    {
        var test = TestSetup + @"
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

class EntityDto { }
internal partial class EntityDto;
class ItemDto { }

" + TestSourceCodes.SelectExprWithFuncObjectInLinqraftNamespace;

        var fixedCode = TestSetup + @"
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

class EntityDto { }
internal partial class EntityDto;
class ItemDto { }
internal partial class ItemDto;

" + TestSourceCodes.SelectExprWithFuncObjectInLinqraftNamespace;

        var expected = VerifyCS
            .Diagnostic(NestedSelectExprPartialDtoAnalyzer.AnalyzerId)
            
            .WithArguments("ItemDto");

        await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
    }
}
