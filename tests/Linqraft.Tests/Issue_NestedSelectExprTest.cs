using System.Collections.Generic;
using System.Linq;

namespace Linqraft.Tests;

/// <summary>
/// Test case for nested SelectExpr calls.
/// When SelectExpr is used inside another SelectExpr, the inner SelectExpr should be
/// converted to a regular Select call and only the outer SelectExpr should generate an interceptor.
/// </summary>
public class Issue_NestedSelectExprTest
{
    private readonly List<NestedEntity> TestData =
    [
        new NestedEntity
        {
            Id = 1,
            Name = "Entity1",
            Items =
            [
                new NestedItem
                {
                    Id = 101,
                    Title = "Item1-1",
                    SubItems =
                    [
                        new NestedSubItem { Id = 1001, Value = "SubItem1-1-1" },
                        new NestedSubItem { Id = 1002, Value = "SubItem1-1-2" },
                    ],
                },
                new NestedItem
                {
                    Id = 102,
                    Title = "Item1-2",
                    SubItems =
                    [
                        new NestedSubItem { Id = 1003, Value = "SubItem1-2-1" },
                    ],
                },
            ],
        },
        new NestedEntity
        {
            Id = 2,
            Name = "Entity2",
            Items =
            [
                new NestedItem
                {
                    Id = 201,
                    Title = "Item2-1",
                    SubItems = [],
                },
            ],
        },
    ];

    /// <summary>
    /// Test: Simple case - SelectExpr at the property level inside an outer SelectExpr.
    /// The inner SelectExpr should be converted to a regular Select.
    /// </summary>
    [Fact]
    public void NestedSelectExpr_SimpleCase_ShouldBeConvertedToSelect()
    {
        var query = TestData.AsQueryable();

        // Outer SelectExpr with explicit DTO type
        // Items uses inner SelectExpr (should be converted to Select)
        var result = query
            .SelectExpr<NestedEntity, NestedEntityDto>(x => new
            {
                x.Id,
                x.Name,
                // This inner SelectExpr should be converted to Select
                Items = x.Items.SelectExpr<NestedItem, NestedItemDto>(i => new
                {
                    i.Id,
                    i.Title,
                }),
            })
            .ToList();

        result.Count.ShouldBe(2);

        // Verify first entity
        var first = result[0];
        first.Id.ShouldBe(1);
        first.Name.ShouldBe("Entity1");
        first.Items.Count().ShouldBe(2);

        var firstItem = first.Items.First();
        firstItem.Id.ShouldBe(101);
        firstItem.Title.ShouldBe("Item1-1");

        // Verify second entity
        var second = result[1];
        second.Id.ShouldBe(2);
        second.Items.Count().ShouldBe(1);
    }
}

// Test data classes
internal class NestedEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public List<NestedItem> Items { get; set; } = [];
}

internal class NestedItem
{
    public int Id { get; set; }
    public string Title { get; set; } = null!;
    public List<NestedSubItem> SubItems { get; set; } = [];
}

internal class NestedSubItem
{
    public int Id { get; set; }
    public string Value { get; set; } = null!;
}
