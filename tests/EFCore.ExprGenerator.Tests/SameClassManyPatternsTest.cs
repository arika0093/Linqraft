using System.Collections.Generic;
using System.Linq;

namespace EFCore.ExprGenerator.Tests;

public class SameClassManyPatternsTest
{
    private List<SimpleClass> Datas =
    [
        new SimpleClass
        {
            Id = 1,
            FirstName = "Alice",
            LastName = "Smith",
            Age = 30,
            Score = 85,
        },
    ];

    [Fact]
    public void Default()
    {
        var converted = Datas
            .AsQueryable()
            .SelectExpr<SimpleClass, DefaultDto>(s => new
            {
                s.Id,
                FullName = s.FirstName + " " + s.LastName,
            })
            .ToList();
        converted[0].FullName.ShouldBe("Alice Smith");
    }

    [Fact]
    public void Default2()
    {
        var converted = Datas
            .AsQueryable()
            .SelectExpr<SimpleClass, DefaultDto>(s => new
            {
                s.Id,
                FullName = s.FirstName + " & " + s.LastName,
            })
            .ToList();
        converted[0].FullName.ShouldBe("Alice & Smith");
    }

    [Fact]
    public void Custom1()
    {
        var converted = Datas
            .AsQueryable()
            .SelectExpr<SimpleClass, Custom1Dto>(s => new
            {
                s.Id,
                FullName = s.FirstName + " + " + s.LastName,
            })
            .ToList();
        converted[0].FullName.ShouldBe("Alice + Smith");
    }

    [Fact]
    public void Custom2()
    {
        var converted = Datas
            .AsQueryable()
            .SelectExpr<SimpleClass, Custom2Dto>(s => new
            {
                s.Id,
                FullName = s.FirstName + " - " + s.LastName,
            })
            .ToList();
        converted[0].FullName.ShouldBe("Alice - Smith");
    }

    [Fact]
    public void AnotherOutput1()
    {
        var converted = Datas
            .AsQueryable()
            .SelectExpr<SimpleClass, AnotherOutput1Dto>(s => new
            {
                s.Id,
                ScorePlusAge = s.Score + s.Age,
            })
            .ToList();
        converted[0].ScorePlusAge.ShouldBe(115); // 85 + 30
    }

    [Fact]
    public void AnotherOutput2()
    {
        var converted = Datas
            .AsQueryable()
            .SelectExpr<SimpleClass, AnotherOutput2Dto>(s => new
            {
                s.Id,
                ScorePlusAge = s.Score - s.Age,
            })
            .ToList();
        converted[0].ScorePlusAge.ShouldBe(55); // 85 - 30
    }

    internal class SimpleClass
    {
        public int Id { get; set; }
        public string FirstName { get; set; } = "";
        public string LastName { get; set; } = "";
        public int Age { get; set; }
        public int Score { get; set; }
    }
}
