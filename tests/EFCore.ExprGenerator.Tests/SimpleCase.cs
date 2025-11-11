using System.Collections.Generic;
using System.Linq;

namespace EFCore.ExprGenerator.Tests;

public class SimpleCase
{
    private readonly List<Simple1> Case1Data =
    [
        new()
        {
            Id = 1,
            FirstName = "John",
            LastName = "Doe",
        },
        new()
        {
            Id = 2,
            FirstName = "Jane",
            LastName = "Smith",
        },
    ];
    private readonly List<Simple2> Case2Data =
    [
        new()
        {
            Id = 1,
            ItemList = ["A", "B", "C"],
            NumberEnumerable = [1, 2, 3],
        },
        new()
        {
            Id = 2,
            ItemList = ["D", "E", "F"],
            NumberEnumerable = [4, 5, 6],
        },
    ];

    [Fact]
    public void Case1()
    {
        var converted = Case1Data
            .AsQueryable()
            .SelectExpr(s => new { s.Id, FullName = s.FirstName + " " + s.LastName })
            .ToList();
        converted.Count.ShouldBe(2);
        var first = converted[0];
        first.Id.ShouldBe(1);
        first.FullName.ShouldBe("John Doe");
    }

    [Fact]
    public void Case1Manually()
    {
        var converted = Case1Data
            .AsQueryable()
            .SelectExpr(s => new Simple1Dto
            {
                Id = s.Id,
                FullName = s.FirstName + " " + s.LastName,
            })
            .ToList();
        converted.Count.ShouldBe(2);
        var first = converted[0];
        first.Id.ShouldBe(1);
        first.FullName.ShouldBe("John Doe");
    }

    [Fact]
    public void Case2()
    {
        var converted = Case2Data
            .AsQueryable()
            .SelectExpr(s => new
            {
                s.Id,
                ItemCount = s.ItemList.Count(),
                NumberSum = s.NumberEnumerable.Sum(),
            })
            .ToList();
        converted.Count.ShouldBe(2);
        var first = converted[0];
        first.Id.ShouldBe(1);
        first.ItemCount.ShouldBe(3);
        first.NumberSum.ShouldBe(6);
    }

    [Fact]
    public void Case2Manually()
    {
        var converted = Case2Data
            .AsQueryable()
            .SelectExpr(s => new Simple2Dto
            {
                Id = s.Id,
                ItemCount = s.ItemList.Count(),
                NumberSum = s.NumberEnumerable.Sum(),
            })
            .ToList();
        converted.Count.ShouldBe(2);
        var first = converted[0];
        first.Id.ShouldBe(1);
        first.ItemCount.ShouldBe(3);
        first.NumberSum.ShouldBe(6);
    }
}

internal class Simple1
{
    public int Id { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
}

internal class Simple1Dto
{
    public int Id { get; set; }
    public string FullName { get; set; } = "";
}

internal class Simple2
{
    public int Id { get; set; }
    public List<string> ItemList { get; set; } = [];
    public IEnumerable<int> NumberEnumerable { get; set; } = [];
}

internal class Simple2Dto
{
    public int Id { get; set; }
    public int ItemCount { get; set; }
    public int NumberSum { get; set; }
}
