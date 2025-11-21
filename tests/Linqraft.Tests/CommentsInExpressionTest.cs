using System.Collections.Generic;
using System.Linq;

namespace Linqraft.Tests;

/// <summary>
/// Test cases for handling comments in SelectExpr expressions
/// Issue: Comments in LINQ expressions should not break generated code
/// </summary>
public class CommentsInExpressionTest
{
    private readonly List<CommentTestData> TestData =
    [
        new CommentTestData
        {
            Id = 1,
            Name = "Test1",
            Items = new List<CommentTestItem>
            {
                new CommentTestItem { Id = 1, IsActive = true, Value = "Active1" },
                new CommentTestItem { Id = 2, IsActive = false, Value = "Inactive1" },
                new CommentTestItem { Id = 3, IsActive = true, Value = "Active2" },
            },
        },
        new CommentTestData
        {
            Id = 2,
            Name = "Test2",
            Items = new List<CommentTestItem>
            {
                new CommentTestItem { Id = 4, IsActive = true, Value = "Active3" },
                new CommentTestItem { Id = 5, IsActive = false, Value = "Inactive2" },
            },
        },
    ];

    /// <summary>
    /// Test case for comments in nested Select expressions with explicit DTO
    /// This reproduces the bug described in the issue
    /// </summary>
    [Fact]
    public void SelectExpr_WithCommentsInNestedSelect_ExplicitDto()
    {
        var converted = TestData
            .AsQueryable()
            .SelectExpr<CommentTestData, CommentTestDto1>(s => new
            {
                s.Id,
                FilteredItems = s.Items
                    // Filter only active items
                    .Where(x => x.IsActive)
                    .Select(x => new CommentItemDto { ItemId = x.Id, ItemValue = x.Value }),
            })
            .ToList();

        converted.Count.ShouldBe(2);
        var first = converted[0];
        first.Id.ShouldBe(1);
        first.FilteredItems.Count.ShouldBe(2);
        first.FilteredItems[0].ItemId.ShouldBe(1);
        first.FilteredItems[0].ItemValue.ShouldBe("Active1");
    }

    /// <summary>
    /// Test case for comments in nested Select expressions with anonymous type
    /// </summary>
    [Fact]
    public void SelectExpr_WithCommentsInNestedSelect_Anonymous()
    {
        var converted = TestData
            .AsQueryable()
            .SelectExpr(s => new
            {
                s.Id,
                FilteredItems = s.Items
                    // Filter only active items
                    .Where(x => x.IsActive)
                    .Select(x => new { ItemId = x.Id, ItemValue = x.Value }),
            })
            .ToList();

        converted.Count.ShouldBe(2);
        var first = converted[0];
        first.Id.ShouldBe(1);
        first.FilteredItems.Count.ShouldBe(2);
        first.FilteredItems.First().ItemId.ShouldBe(1);
        first.FilteredItems.First().ItemValue.ShouldBe("Active1");
    }

    /// <summary>
    /// Test case for comments in nested Select with predefined DTO
    /// </summary>
    [Fact]
    public void SelectExpr_WithCommentsInNestedSelect_PredefinedDto()
    {
        var converted = TestData
            .AsQueryable()
            .SelectExpr(s => new CommentTestDto2
            {
                Id = s.Id,
                FilteredItems = s.Items
                    // Filter only active items
                    .Where(x => x.IsActive)
                    .Select(x => new CommentItemDto { ItemId = x.Id, ItemValue = x.Value }),
            })
            .ToList();

        converted.Count.ShouldBe(2);
        var first = converted[0];
        first.Id.ShouldBe(1);
        first.FilteredItems.Count.ShouldBe(2);
        first.FilteredItems[0].ItemId.ShouldBe(1);
        first.FilteredItems[0].ItemValue.ShouldBe("Active1");
    }

    /// <summary>
    /// Test case for multiline comments
    /// </summary>
    [Fact]
    public void SelectExpr_WithMultilineComments()
    {
        var converted = TestData
            .AsQueryable()
            .SelectExpr<CommentTestData, CommentTestDto3>(s => new
            {
                s.Id,
                FilteredItems = s.Items
                    /*
                     * This is a multiline comment
                     * Filter only active items
                     */
                    .Where(x => x.IsActive)
                    .Select(x => new CommentItemDto { ItemId = x.Id, ItemValue = x.Value }),
            })
            .ToList();

        converted.Count.ShouldBe(2);
        var first = converted[0];
        first.Id.ShouldBe(1);
        first.FilteredItems.Count.ShouldBe(2);
    }

    /// <summary>
    /// Test case for inline comments in property expressions
    /// </summary>
    [Fact]
    public void SelectExpr_WithInlineComments()
    {
        var converted = TestData
            .AsQueryable()
            .SelectExpr(s => new
            {
                s.Id, // Primary identifier
                s.Name, // Item name
                ActiveCount = s.Items.Where(x => x.IsActive).Count(), // Count of active items
            })
            .ToList();

        converted.Count.ShouldBe(2);
        var first = converted[0];
        first.Id.ShouldBe(1);
        first.Name.ShouldBe("Test1");
        first.ActiveCount.ShouldBe(2);
    }
}

// Test data classes
public class CommentTestData
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public List<CommentTestItem> Items { get; set; } = new();
}

public class CommentTestItem
{
    public int Id { get; set; }
    public bool IsActive { get; set; }
    public string Value { get; set; } = "";
}

// DTO classes
public partial class CommentTestDto1
{
    public int Id { get; set; }
}

public class CommentTestDto2
{
    public int Id { get; set; }
    public List<CommentItemDto> FilteredItems { get; set; } = new();
}

public partial class CommentTestDto3
{
    public int Id { get; set; }
}

public class CommentItemDto
{
    public int ItemId { get; set; }
    public string ItemValue { get; set; } = "";
}
