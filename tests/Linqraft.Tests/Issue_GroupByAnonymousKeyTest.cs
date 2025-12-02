using System.Collections.Generic;
using System.Linq;

namespace Linqraft.Tests;

/// <summary>
/// Tests for GroupBy with anonymous type key followed by SelectExpr.
/// </summary>
public class Issue_GroupByAnonymousKeyTest
{
    [Fact]
    public void GroupByAnonymousKey_WithSelectExpr_Anonymous()
    {
        // Test GroupBy with anonymous type key followed by SelectExpr returning anonymous type
        var grouped = SampleData
            .AsQueryable()
            .GroupBy(x => new { x.Category })
            .SelectExpr(g => new
            {
                Category = g.Key.Category,
                Count = g.Count(),
                TotalValue = g.Sum(x => x.Value),
            })
            .ToList();

        grouped.Count.ShouldBe(2);

        var electronics = grouped.FirstOrDefault(x => x.Category == "Electronics");
        electronics.ShouldNotBeNull();
        electronics.Count.ShouldBe(2);
        electronics.TotalValue.ShouldBe(130);

        var clothing = grouped.FirstOrDefault(x => x.Category == "Clothing");
        clothing.ShouldNotBeNull();
        clothing.Count.ShouldBe(2);
        clothing.TotalValue.ShouldBe(70);
    }

    [Fact]
    public void GroupByAnonymousKey_WithSelectExpr_Named()
    {
        // Test GroupBy with anonymous type key followed by SelectExpr with predefined DTO
        var grouped = SampleData
            .AsQueryable()
            .GroupBy(x => new { x.Category })
            .SelectExpr(g => new GroupedResultDto
            {
                Category = g.Key.Category,
                Count = g.Count(),
                TotalValue = g.Sum(x => x.Value),
            })
            .ToList();

        grouped.Count.ShouldBe(2);

        var electronics = grouped.FirstOrDefault(x => x.Category == "Electronics");
        electronics.ShouldNotBeNull();
        electronics.Count.ShouldBe(2);
        electronics.TotalValue.ShouldBe(130);

        var clothing = grouped.FirstOrDefault(x => x.Category == "Clothing");
        clothing.ShouldNotBeNull();
        clothing.Count.ShouldBe(2);
        clothing.TotalValue.ShouldBe(70);
    }

    [Fact]
    public void GroupByMultiplePropertiesAnonymousKey_WithSelectExpr()
    {
        // Test GroupBy with multiple properties in anonymous type key
        var grouped = SampleData
            .AsQueryable()
            .GroupBy(x => new { x.Category, x.SubCategory })
            .SelectExpr(g => new
            {
                Category = g.Key.Category,
                SubCategory = g.Key.SubCategory,
                Count = g.Count(),
            })
            .ToList();

        grouped.Count.ShouldBe(4);

        var phones = grouped.FirstOrDefault(x => x.Category == "Electronics" && x.SubCategory == "Phones");
        phones.ShouldNotBeNull();
        phones.Count.ShouldBe(1);
    }

    [Fact]
    public void GroupByAnonymousKey_WithNullConditional_TransformsCorrectly()
    {
        // Test that null-conditional operators are properly transformed
        // when used with GroupBy on anonymous key
        var dataWithChild = new List<TestItemWithChild>
        {
            new() { Id = 1, Category = "A", Child = new ChildItem { Description = "Desc1" } },
            new() { Id = 2, Category = "A", Child = null },
            new() { Id = 3, Category = "B", Child = new ChildItem { Description = "Desc3" } },
        };

        var grouped = dataWithChild
            .AsQueryable()
            .GroupBy(x => new { x.Category })
            .SelectExpr(g => new
            {
                Category = g.Key.Category,
                Count = g.Count(),
                // Test null-conditional operator on element properties (not Key)
                FirstDescription = g.FirstOrDefault()!.Child?.Description,
            })
            .ToList();

        grouped.Count.ShouldBe(2);
        var groupA = grouped.FirstOrDefault(x => x.Category == "A");
        groupA.ShouldNotBeNull();
        groupA.Count.ShouldBe(2);
        groupA.FirstDescription.ShouldBe("Desc1");
    }

    private static readonly List<TestItem> SampleData =
    [
        new() { Id = 1, Name = "Phone", Category = "Electronics", SubCategory = "Phones", Value = 100 },
        new() { Id = 2, Name = "Laptop", Category = "Electronics", SubCategory = "Computers", Value = 30 },
        new() { Id = 3, Name = "Shirt", Category = "Clothing", SubCategory = "Tops", Value = 25 },
        new() { Id = 4, Name = "Pants", Category = "Clothing", SubCategory = "Bottoms", Value = 45 },
    ];

    internal class TestItem
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string Category { get; set; } = "";
        public string SubCategory { get; set; } = "";
        public int Value { get; set; }
    }

    internal class TestItemWithChild
    {
        public int Id { get; set; }
        public string Category { get; set; } = "";
        public ChildItem? Child { get; set; }
    }

    internal class ChildItem
    {
        public string Description { get; set; } = "";
    }

    internal class GroupedResultDto
    {
        public string Category { get; set; } = "";
        public int Count { get; set; }
        public int TotalValue { get; set; }
    }
}
