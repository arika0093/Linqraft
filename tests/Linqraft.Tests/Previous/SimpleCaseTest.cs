using System.Collections.Generic;
using System.Linq;

namespace Linqraft.Tests;

public class SimpleCaseTest
{
    [Fact]
    public void Case1()
    {
        var converted = Case1Data
            .AsQueryable()
            .SelectExpr<Simple1, Case1Dto>(s => new
            {
                s.Id,
                FullName = s.FirstName + " " + s.LastName,
            })
            .ToList();
        converted.Count.ShouldBe(2);
        var first = converted[0];
        first.GetType().Name.ShouldBe("Case1Dto");
        first.Id.ShouldBe(1);
        first.FullName.ShouldBe("John Doe");
    }

    [Fact]
    public void Case1Other()
    {
        var converted = Case1Data
            .AsQueryable()
            .SelectExpr<Simple1, Case1OtherDto>(s => new
            {
                s.Id,
                FullName = s.FirstName + " + " + s.LastName,
            })
            .ToList();
        converted.Count.ShouldBe(2);
        var first = converted[0];
        first.GetType().Name.ShouldBe("Case1OtherDto");
        first.Id.ShouldBe(1);
        first.FullName.ShouldBe("John + Doe");
    }

    [Fact]
    public void Case1AnotherParamName()
    {
        var converted = Case1Data
            .AsQueryable()
            .SelectExpr<Simple1, Case1OtherDto>(info => new
            {
                info.Id,
                FullName = info.FirstName + " + " + info.LastName,
            })
            .ToList();
        converted.Count.ShouldBe(2);
        var first = converted[0];
        first.GetType().Name.ShouldBe("Case1OtherDto");
        first.Id.ShouldBe(1);
        first.FullName.ShouldBe("John + Doe");
    }

    [Fact]
    public void Case1ManyLinqMethods()
    {
        var converted = Case1Data
            .AsQueryable()
            .Where(s => s.Id > 0)
            .OrderBy(s => s.LastName)
            .SelectExpr<Simple1, Case1ManyLinqMethodsDto>(s => new
            {
                s.Id,
                FullName = s.FirstName + " " + s.LastName,
            })
            .ToList();
        converted.Count.ShouldBe(2);
        var first = converted[0];
        first.GetType().Name.ShouldBe("Case1ManyLinqMethodsDto");
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
        first.GetType().ShouldBe(typeof(Simple1Dto));
        first.Id.ShouldBe(1);
        first.FullName.ShouldBe("John Doe");
    }

    [Fact]
    public void Case1ManuallyOther()
    {
        var converted = Case1Data
            .AsQueryable()
            .SelectExpr(s => new Simple1Dto
            {
                Id = s.Id,
                FullName = s.FirstName + " + " + s.LastName,
            })
            .ToList();
        converted.Count.ShouldBe(2);
        var first = converted[0];
        first.GetType().ShouldBe(typeof(Simple1Dto));
        first.Id.ShouldBe(1);
        first.FullName.ShouldBe("John + Doe");
    }

    [Fact]
    public void Case2()
    {
        var converted = Case2Data
            .AsQueryable()
            .SelectExpr<Simple2, Case2AutoDto>(s => new
            {
                s.Id,
                ItemCount = s.ItemList.Count,
                NumberSum = s.NumberEnumerable.Sum(),
            })
            .ToList();
        converted.Count.ShouldBe(2);
        var first = converted[0];
        first.GetType().Name.ShouldBe("Case2AutoDto");
        first.Id.ShouldBe(1);
        first.ItemCount.ShouldBe(3);
        first.NumberSum.ShouldBe(6);
    }

    [Fact]
    public void Case2ManyLinqMethods()
    {
        var converted = Case2Data
            .AsQueryable()
            .Where(s => s.Id > 0)
            .OrderBy(s => s.Id)
            .SelectExpr<Simple2, Case2ManyLinqMethodsAutoDto>(s => new
            {
                s.Id,
                ItemCount = s.ItemList.Where(i => i != "C").Count(),
                NumberSum = s.NumberEnumerable.Where(n => n % 2 == 0).Sum(),
            })
            .ToList();
        converted.Count.ShouldBe(2);
        var first = converted[0];
        first.GetType().Name.ShouldBe("Case2ManyLinqMethodsAutoDto");
        first.Id.ShouldBe(1);
        first.ItemCount.ShouldBe(2);
        first.NumberSum.ShouldBe(2);
    }

    [Fact]
    public void Case2Manually()
    {
        var converted = Case2Data
            .AsQueryable()
            .SelectExpr(s => new Simple2Dto
            {
                Id = s.Id,
                ItemCount = s.ItemList.Count,
                NumberSum = s.NumberEnumerable.Sum(),
            })
            .ToList();
        converted.Count.ShouldBe(2);
        var first = converted[0];
        first.GetType().ShouldBe(typeof(Simple2Dto));
        first.Id.ShouldBe(1);
        first.ItemCount.ShouldBe(3);
        first.NumberSum.ShouldBe(6);
    }

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
