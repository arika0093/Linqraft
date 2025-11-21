using System.Collections.Generic;
using System.Linq;

namespace Linqraft.Tests;

// This file tests generation of DTOs in the global namespace when the source class is also in the global namespace
// It specifically tests nested .Select calls to ensure nested DTOs are properly generated without namespace issues
// NOTE: This test class is intentionally in the global namespace (no namespace declaration) to test the global namespace feature

public class GlobalNamespaceNestedTest
{
    [Fact]
    public void Test_GlobalNamespace_WithNestedSelect()
    {
        var result = SampleData
            .AsQueryable()
            .SelectExpr<GlobalParentClass, GlobalParentDto>(s => new
            {
                s.Id,
                s.Name,
                Children = s.Children.Select(c => new { c.ChildId, c.ChildName }).ToList(),
            })
            .ToList();

        result.ShouldNotBeEmpty();
        result.Count.ShouldBe(2);
        result[0].Id.ShouldBe(1);
        result[0].Name.ShouldBe("Parent1");
        result[0].Children.Count.ShouldBe(2);
        result[0].Children[0].ChildId.ShouldBe(101);
        result[0].Children[0].ChildName.ShouldBe("Child1");
    }

    [Fact]
    public void Test_GlobalNamespace_AnonymousType_WithNestedSelect()
    {
        var result = SampleData
            .AsQueryable()
            .SelectExpr(s => new
            {
                s.Id,
                s.Name,
                Children = s.Children.Select(c => new { c.ChildId, c.ChildName }).ToList(),
            })
            .ToList();

        result.ShouldNotBeEmpty();
        result.Count.ShouldBe(2);
        result[0].Id.ShouldBe(1);
        result[0].Name.ShouldBe("Parent1");
        result[0].Children.Count.ShouldBe(2);
        result[0].Children[0].ChildId.ShouldBe(101);
        result[0].Children[0].ChildName.ShouldBe("Child1");
    }

    private readonly List<GlobalParentClass> SampleData =
    [
        new GlobalParentClass
        {
            Id = 1,
            Name = "Parent1",
            Children =
            [
                new GlobalChildClass { ChildId = 101, ChildName = "Child1" },
                new GlobalChildClass { ChildId = 102, ChildName = "Child2" },
            ],
        },
        new GlobalParentClass
        {
            Id = 2,
            Name = "Parent2",
            Children = [new GlobalChildClass { ChildId = 201, ChildName = "Child3" }],
        },
    ];
}

// Test data classes in global namespace
public class GlobalParentClass
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public List<GlobalChildClass> Children { get; set; } = [];
}

public class GlobalChildClass
{
    public int ChildId { get; set; }
    public string ChildName { get; set; } = "";
}

public class GlobalParentClassNullable
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public List<GlobalChildClass> Children { get; set; } = [];
}
