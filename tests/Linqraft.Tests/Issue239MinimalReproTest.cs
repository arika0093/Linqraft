using System;
using System.Linq;
using System.Collections.Generic;

namespace Linqraft.Tests;

public class Issue239MinimalReproTest
{
    private readonly List<Entity> _testData =
    [
        new Entity
        {
            Id = 1,
            Name = "Test",
            Child = new Child { Description = "Desc" },
            Items = [new Item { Title = "Title1" }],
        },
    ];

    [Fact]
    public void TwoSelectExprWithSameStructureShouldShareNestedDto()
    {
        var data = _testData.AsQueryable();
        
        // result1 and result2 use the exact same anonymous structure
        // They should share the same ItemTitlesDto definition
        var result1 = data.SelectExpr(x => new
        {
            x.Id,
            x.Name,
            ChildDescription = x.Child?.Description,
            ItemTitles = x.Items.Select(i => new{i.Title}),
        }).ToList();
        
        var result2 = data.SelectExpr(x => new
        {
            x.Id,
            x.Name,
            ChildDescription = x.Child?.Description,
            ItemTitles = x.Items.Select(i => new{i.Title}),
        }).ToList();

        result1.Count.ShouldBe(1);
        result2.Count.ShouldBe(1);
    }

    internal class Entity
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public Child? Child { get; set; }
        public List<Item> Items { get; set; } = [];
    }

    internal class Child
    {
        public string Description { get; set; } = "";
    }

    internal class Item
    {
        public string Title { get; set; } = "";
    }
}
