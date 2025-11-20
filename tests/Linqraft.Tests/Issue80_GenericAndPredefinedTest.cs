using System;
using System.Collections.Generic;
using System.Linq;

namespace Linqraft.Tests;

public class Issue80_GenericAndPredefinedTest
{
    [Fact]
    public void Test_GenericAndPredefined()
    {
        List<TestForIssue80> datas =
        [
            new()
            {
                Id = 1,
                Name = "Parent 1",
                Children =
                [
                    new() { Id = 1, Description = "Child 1-1" },
                    new() { Id = 2, Description = "Child 1-2" },
                ],
            },
            new()
            {
                Id = 2,
                Name = "Parent 2",
                Children =
                [
                    new() { Id = 3, Description = "Child 2-1" },
                    new() { Id = 4, Description = "Child 2-2" },
                ],
            },
        ];

        var result = datas
            .AsQueryable()
            .SelectExpr<TestForIssue80, TestForIssue80Dto>(d => new TestForIssue80Dto
            {
                Id = d.Id,
                Descriptions = d.Children.Select(child => child.Description),
            })
            .ToList();

        result.Count.ShouldBe(2);
        result[0].Id.ShouldBe(1);
        result[0].Descriptions.ShouldBe(["Child 1-1", "Child 1-2"]);
        result[1].Id.ShouldBe(2);
        result[1].Descriptions.ShouldBe(["Child 2-1", "Child 2-2"]);
    }
}

internal class TestForIssue80
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required List<TestForIssue80Child> Children { get; set; }
}

internal class TestForIssue80Child
{
    public int Id { get; set; }
    public required string Description { get; set; }
}

internal class TestForIssue80Dto
{
    public int Id { get; set; }
    public IEnumerable<string> Descriptions { get; set; } = [];
}
