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
    private readonly List<NestedEntity207> TestData =
    [
        new NestedEntity207
        {
            Id = 1,
            Name = "Entity1",
            Items =
            [
                new NestedItem207
                {
                    Id = 101,
                    Title = "Item1-1",
                    SubItems =
                    [
                        new NestedSubItem207 { Id = 1001, Value = "SubItem1-1-1" },
                        new NestedSubItem207 { Id = 1002, Value = "SubItem1-1-2" },
                    ],
                },
                new NestedItem207
                {
                    Id = 102,
                    Title = "Item1-2",
                    SubItems =
                    [
                        new NestedSubItem207 { Id = 1003, Value = "SubItem1-2-1" },
                    ],
                },
            ],
        },
        new NestedEntity207
        {
            Id = 2,
            Name = "Entity2",
            Items =
            [
                new NestedItem207
                {
                    Id = 201,
                    Title = "Item2-1",
                    SubItems = [],
                },
            ],
        },
    ];

    /// <summary>
    /// Test: Outer SelectExpr with inner Select (not SelectExpr).
    /// This verifies the basic behavior works correctly.
    /// </summary>
    [Fact]
    public void NestedSelectExpr_WithInnerSelect_ShouldWork()
    {
        var query = TestData.AsQueryable();

        // Outer SelectExpr with explicit DTO type
        // Items uses regular Select (not SelectExpr)
        var result = query
            .SelectExpr<NestedEntity207, NestedEntity207Dto>(x => new
            {
                x.Id,
                x.Name,
                // Using regular Select - the DTO will be auto-generated
                Items = x.Items.Select(i => new
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

        // Verify that NestedEntity207Dto is NOT in the LinqraftGenerated_ namespace
        var nestedEntityDtoType = typeof(NestedEntity207Dto);
        nestedEntityDtoType.Namespace!.ShouldNotContain("LinqraftGenerated");
        nestedEntityDtoType.Namespace.ShouldBe("Linqraft.Tests");
    }
}

// Test data classes for the simple nested test
internal class NestedEntity207
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public List<NestedItem207> Items { get; set; } = [];
}

internal class NestedItem207
{
    public int Id { get; set; }
    public string Title { get; set; } = null!;
    public List<NestedSubItem207> SubItems { get; set; } = [];
}

internal class NestedSubItem207
{
    public int Id { get; set; }
    public string Value { get; set; } = null!;
}
