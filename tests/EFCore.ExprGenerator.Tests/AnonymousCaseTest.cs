using System.Collections.Generic;
using System.Linq;

namespace EFCore.ExprGenerator.Tests;

public class AnonymousCaseTest
{
    [Fact]
    public void Case1Basic()
    {
        var converted = SampleData
            .AsQueryable()
            .SelectExpr(s => new { s.Id, FullName = s.FirstName + " + " + s.LastName })
            .ToList();
        converted.Count.ShouldBe(2);
        var first = converted[0];
        first.FullName.ShouldBe("John + Doe");
    }

    [Fact]
    public void Case1Other1()
    {
        var converted = SampleData
            .AsQueryable()
            .SelectExpr(s => new { s.Id, FullName = s.FirstName + " - " + s.LastName })
            .ToList();
        converted.Count.ShouldBe(2);
        var first = converted[0];
        first.FullName.ShouldBe("John - Doe");
    }

    [Fact]
    public void Case1Other2()
    {
        var converted = SampleData
            .AsQueryable()
            .SelectExpr(s => new { s.Id, FullName = s.FirstName + " | " + s.LastName })
            .ToList();
        converted.Count.ShouldBe(2);
        var first = converted[0];
        first.FullName.ShouldBe("John | Doe");
    }

    private static readonly List<AnonymousTestClass> SampleData =
    [
        new AnonymousTestClass
        {
            Id = 1,
            FirstName = "John",
            LastName = "Doe",
        },
        new AnonymousTestClass
        {
            Id = 2,
            FirstName = "Jane",
            LastName = "Smith",
        },
    ];

    internal class AnonymousTestClass
    {
        public int Id { get; set; }
        public string FirstName { get; set; } = "";
        public string LastName { get; set; } = "";
    }
}
