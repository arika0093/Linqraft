using System.Collections.Generic;
using System.Linq;

namespace Linqraft.Tests;

public sealed class CollectionNullabilityRuntimeTests
{
    private static readonly List<OptionalParent> Parents =
    [
        new()
        {
            Id = 1,
            Children =
            [
                new OptionalChild { Name = "Alpha", Score = 10 },
                new OptionalChild { Name = "Beta", Score = 20 },
            ],
        },
        new()
        {
            Id = 2,
            Children = null,
        },
    ];

    [Fact]
    public void Null_conditional_collection_projection_uses_empty_fallbacks_by_default()
    {
        var result = Parents
            .AsQueryable()
            .SelectExpr<OptionalParent, OptionalParentDto>(parent => new
            {
                parent.Id,
                ChildNames = parent.Children?.Select(child => child.Name).ToList(),
                ChildRows = parent.Children?.Select(child => new
                {
                    child.Name,
                    child.Score,
                }).ToList(),
            })
            .ToList();

        result.Count.ShouldBe(2);
        result[0].ChildNames.Count.ShouldBe(2);
        result[0].ChildNames[0].ShouldBe("Alpha");
        result[0].ChildNames[1].ShouldBe("Beta");
        result[0].ChildRows.Count.ShouldBe(2);
        result[0].ChildRows[0].Name.ShouldBe("Alpha");
        result[0].ChildRows[0].Score.ShouldBe(10);
        var childNames = result[1].ChildNames.ShouldNotBeNull();
        childNames.ShouldBeEmpty();
        var childRows = result[1].ChildRows.ShouldNotBeNull();
        childRows.ShouldBeEmpty();
    }
}

public sealed class OptionalParent
{
    public int Id { get; set; }
    public List<OptionalChild>? Children { get; set; }
}

public sealed class OptionalChild
{
    public string Name { get; set; } = string.Empty;
    public int Score { get; set; }
}
