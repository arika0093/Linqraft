using System.Collections.Generic;
using System.Linq;

namespace Linqraft.Tests;

/// <summary>
/// Test for ensuring that when ChildDto of the same shape appears multiple times,
/// the definition of ChildDto should be generated only once.
/// This is a reproduction of the bug reported in issue #239.
/// </summary>
public partial class DuplicateChildDtoTest
{
    private readonly List<Entity> _testData =
    [
        new Entity
        {
            Id = 1,
            Name = "Entity1",
            Child = new Child { Description = "Child1" },
            Items = [new Item { Title = "Item1" }, new Item { Title = "Item2" }],
        },
    ];

    [Fact]
    public void ShouldGenerateSingleItemDtoForMultipleSelectExprWithSameShape()
    {
        // Arrange & Act - Two separate SelectExpr calls with identical anonymous structure
        // This should generate only ONE ItemDto class definition, not two
        var result1 = _testData
            .AsQueryable()
            .SelectExpr(x => new
            {
                x.Id,
                x.Name,
                ChildDescription = x.Child?.Description,
                ItemTitles = x.Items.Select(i => new { i.Title }),
            })
            .ToList();

        var result2 = _testData
            .AsQueryable()
            .SelectExpr(x => new
            {
                x.Id,
                x.Name,
                ChildDescription = x.Child?.Description,
                ItemTitles = x.Items.Select(i => new { i.Title }),
            })
            .ToList();

        // Assert
        result1.ShouldNotBeNull();
        result1.Count.ShouldBe(1);
        result1[0].Id.ShouldBe(1);
        result1[0].Name.ShouldBe("Entity1");
        result1[0].ChildDescription.ShouldBe("Child1");
        result1[0].ItemTitles.Count().ShouldBe(2);

        result2.ShouldNotBeNull();
        result2.Count.ShouldBe(1);
        result2[0].Id.ShouldBe(1);
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
