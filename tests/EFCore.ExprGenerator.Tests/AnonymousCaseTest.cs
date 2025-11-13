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

    private static readonly List<SimpleClass> SampleData =
    [
        new SimpleClass
        {
            Id = 1,
            FirstName = "John",
            LastName = "Doe",
        },
        new SimpleClass
        {
            Id = 2,
            FirstName = "Jane",
            LastName = "Smith",
        },
    ];

    internal class SimpleClass
    {
        public int Id { get; set; }
        public string FirstName { get; set; } = "";
        public string LastName { get; set; } = "";
    }
}
