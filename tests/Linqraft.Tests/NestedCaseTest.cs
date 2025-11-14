using System.Collections.Generic;
using System.Linq;

namespace Linqraft.Tests;

public class NestedCaseTest
{
    private readonly List<NestBase> NestData =
    [
        new NestBase
        {
            Id = 1,
            Name = "Base1",
            Child = new NestChild1
            {
                Description = "Child1 of Base1",
                GrandChild = new NestGrandChild
                {
                    Details = "GrandChild1 of Child1 of Base1",
                    GreatGrandChild = new NestGreatGrandChild
                    {
                        Info = "GreatGrandChild1 of GrandChild1 of Child1 of Base1",
                    },
                },
            },
            Child2 = new List<NestChild2>
            {
                new NestChild2
                {
                    Summary = "Child2-1 of Base1",
                    GrandChilds = new List<NestGrandChild2>
                    {
                        new NestGrandChild2
                        {
                            Notes = "GrandChild2-1 of Child2-1 of Base1",
                            Value = 1,
                        },
                        new NestGrandChild2
                        {
                            Notes = "GrandChild2-2 of Child2-1 of Base1",
                            Value = 2,
                        },
                    },
                },
                new NestChild2
                {
                    Summary = "Child2-2 of Base1",
                    GrandChilds = new List<NestGrandChild2>
                    {
                        new NestGrandChild2
                        {
                            Notes = "GrandChild2-1 of Child2-2 of Base1",
                            Value = 3,
                        },
                    },
                },
            },
        },
        // Additional test data can be added here
    ];

    [Fact]
    public void NestedCase_SelectExpr_Anonymous()
    {
        var converted = NestData
            .AsQueryable()
            .SelectExpr(s => new
            {
                s.Id,
                s.Name,
                ChildDescription = s.Child?.Description,
                GrandChildDetails = s.Child?.GrandChild?.Details,
                GreatGrandChildInfo = s.Child?.GrandChild?.GreatGrandChild?.Info,
                Child2Summaries = s
                    .Child2.Select(c2 => new
                    {
                        c2.Summary,
                        GrandChild2 = c2.GrandChilds,
                        GrandChild2Notes = c2.GrandChilds.Select(gc2 => gc2.Notes),
                        GrandChild2Values = c2.GrandChilds.Select(gc2 => gc2.Value),
                    })
                    .ToList(),
            })
            .ToList();
        converted.Count.ShouldBe(1);
        var first = converted[0];
        first.Id.ShouldBe(1);
        first.Name.ShouldBe("Base1");
        first.ChildDescription.ShouldBe("Child1 of Base1");
        first.GrandChildDetails.ShouldBe("GrandChild1 of Child1 of Base1");
        first.GreatGrandChildInfo.ShouldBe("GreatGrandChild1 of GrandChild1 of Child1 of Base1");
        first.Child2Summaries.Count.ShouldBe(2);
        var child2First = first.Child2Summaries.First();
        child2First.Summary.ShouldBe("Child2-1 of Base1");
        child2First.GrandChild2.Count().ShouldBe(2);
        child2First.GrandChild2Notes.ShouldBe(
            ["GrandChild2-1 of Child2-1 of Base1", "GrandChild2-2 of Child2-1 of Base1"]
        );
        child2First.GrandChild2Values.ShouldBe([1, 2]);
    }

    [Fact]
    public void NestedCase_SelectExpr_Explicit()
    {
        var converted = NestData
            .AsQueryable()
            .SelectExpr<NestBase, NestBaseDto>(s => new
            {
                s.Id,
                s.Name,
                ChildDescription = s.Child?.Description,
                GrandChildDetails = s.Child?.GrandChild?.Details,
                GreatGrandChildInfo = s.Child?.GrandChild?.GreatGrandChild?.Info,
                Child2Summaries = s
                    .Child2.Select(c2 => new
                    {
                        c2.Summary,
                        GrandChild2 = c2.GrandChilds,
                        GrandChild2Notes = c2.GrandChilds.Select(gc2 => gc2.Notes),
                        GrandChild2Values = c2.GrandChilds.Select(gc2 => gc2.Value),
                    })
                    .ToList(),
            })
            .ToList();
        converted.Count.ShouldBe(1);
        var first = converted[0];
        first.Id.ShouldBe(1);
        first.Name.ShouldBe("Base1");
        first.ChildDescription.ShouldBe("Child1 of Base1");
        first.GrandChildDetails.ShouldBe("GrandChild1 of Child1 of Base1");
        first.GreatGrandChildInfo.ShouldBe("GreatGrandChild1 of GrandChild1 of Child1 of Base1");
        first.Child2Summaries.Count.ShouldBe(2);
        var child2First = first.Child2Summaries.First();
        child2First.Summary.ShouldBe("Child2-1 of Base1");
        child2First.GrandChild2.Count().ShouldBe(2);
        child2First.GrandChild2Notes.ShouldBe(
            ["GrandChild2-1 of Child2-1 of Base1", "GrandChild2-2 of Child2-1 of Base1"]
        );
        child2First.GrandChild2Values.ShouldBe([1, 2]);
    }

    [Fact]
    public void NestedCase_SelectExpr_PredefinedDto_WithNullConditional()
    {
        // Create test data with both null and non-null cases
        var testData = new List<NestBase>
        {
            new NestBase
            {
                Id = 1,
                Name = "Base1",
                Child = new NestChild1
                {
                    Description = "Child1 of Base1",
                    GrandChild = new NestGrandChild
                    {
                        Details = "GrandChild1 of Child1 of Base1",
                        GreatGrandChild = null,
                    },
                },
                Child2 = [],
            },
            new NestBase
            {
                Id = 2,
                Name = "Base2",
                Child = null, // null child to test null-conditional operator
                Child2 = [],
            },
        };

        var converted = testData
            .AsQueryable()
            .SelectExpr(s => new NestBasePredefinedDto
            {
                Id = s.Id,
                Name = s.Name,
                ChildDescription = s.Child?.Description,
                GrandChildDetails = s.Child?.GrandChild?.Details,
            })
            .ToList();

        converted.Count.ShouldBe(2);

        // First item has non-null child
        var first = converted[0];
        first.Id.ShouldBe(1);
        first.Name.ShouldBe("Base1");
        first.ChildDescription.ShouldBe("Child1 of Base1");
        first.GrandChildDetails.ShouldBe("GrandChild1 of Child1 of Base1");

        // Second item has null child
        var second = converted[1];
        second.Id.ShouldBe(2);
        second.Name.ShouldBe("Base2");
        second.ChildDescription.ShouldBeNull();
        second.GrandChildDetails.ShouldBeNull();
    }
}

internal class NestBase
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public NestChild1? Child { get; set; }
    public IEnumerable<NestChild2> Child2 { get; set; } = [];
}

internal class NestChild1
{
    public string? Description { get; set; }
    public NestGrandChild? GrandChild { get; set; }
}

internal class NestGrandChild
{
    public string? Details { get; set; }
    public NestGreatGrandChild? GreatGrandChild { get; set; }
}

internal class NestGreatGrandChild
{
    public string? Info { get; set; }
}

internal class NestChild2
{
    public required string Summary { get; set; }
    public IEnumerable<NestGrandChild2> GrandChilds { get; set; } = [];
}

internal class NestGrandChild2
{
    public required string Notes { get; set; }
    public required int Value { get; set; }
}

internal class NestBasePredefinedDto
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string? ChildDescription { get; set; }
    public string? GrandChildDetails { get; set; }
}
