using System;
using System.Collections.Generic;
using System.Linq;

namespace Linqraft.Tests;

public class Issue_SelectFirstOrDefaultTest
{
    [Fact]
    public void SelectFirstOrDefault_ShouldGenerateValidType()
    {
        var converted = TestData
            .AsQueryable()
            .SelectExpr<Sample, SampleDto>(s => new
            {
                s.Id,
                LatestFilteredData = s.MyData
                    .OrderByDescending(d => d.CreatedAt)
                    .Select(d => new { d.Foo, d.Bar })
                    .FirstOrDefault()
            })
            .ToList();

        converted.Count.ShouldBe(2);
        var first = converted[0];
        first.Id.ShouldBe(1);
        first.LatestFilteredData.ShouldNotBeNull();
        first.LatestFilteredData!.Foo.ShouldBe("Foo3");
        first.LatestFilteredData!.Bar.ShouldBe(300);

        var second = converted[1];
        second.Id.ShouldBe(2);
        second.LatestFilteredData.ShouldBeNull();
    }

    private readonly List<Sample> TestData =
    [
        new()
        {
            Id = 1,
            MyData =
            [
                new() { Foo = "Foo1", Bar = 100, CreatedAt = new DateTime(2024, 1, 1) },
                new() { Foo = "Foo2", Bar = 200, CreatedAt = new DateTime(2024, 1, 2) },
                new() { Foo = "Foo3", Bar = 300, CreatedAt = new DateTime(2024, 1, 3) },
            ]
        },
        new()
        {
            Id = 2,
            MyData = []
        },
    ];
}

internal class Sample
{
    public int Id { get; set; }
    public List<MyData> MyData { get; set; } = [];
}

internal class MyData
{
    public string Foo { get; set; } = "";
    public int Bar { get; set; }
    public DateTime CreatedAt { get; set; }
}
