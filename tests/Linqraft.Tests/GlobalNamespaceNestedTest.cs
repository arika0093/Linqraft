using System.Collections.Generic;
using System.Linq;
using Linqraft;

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
                Children = s.Children.Select(c => new
                {
                    c.ChildId,
                    c.ChildName
                }).ToList()
            })
            .ToList();

        Assert.NotEmpty(result);
        Assert.Equal(2, result.Count);
        Assert.Equal(1, result[0].Id);
        Assert.Equal("Parent1", result[0].Name);
        Assert.Equal(2, result[0].Children.Count);
        Assert.Equal(101, result[0].Children[0].ChildId);
        Assert.Equal("Child1", result[0].Children[0].ChildName);
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
                Children = s.Children.Select(c => new
                {
                    c.ChildId,
                    c.ChildName
                }).ToList()
            })
            .ToList();

        Assert.NotEmpty(result);
        Assert.Equal(2, result.Count);
        Assert.Equal(1, result[0].Id);
        Assert.Equal("Parent1", result[0].Name);
        Assert.Equal(2, result[0].Children.Count);
        Assert.Equal(101, result[0].Children[0].ChildId);
        Assert.Equal("Child1", result[0].Children[0].ChildName);
    }

    [Fact]
    public void Test_GlobalNamespace_WithNullableNestedSelect()
    {
        var result = SampleDataNullable
            .AsQueryable()
            .SelectExpr<GlobalParentClassNullable, GlobalParentNullableDto>(s => new
            {
                s.Id,
                s.Name,
                Children = s.Children?.Select(c => new
                {
                    c.ChildId,
                    c.ChildName
                })
            })
            .ToList();

        Assert.NotEmpty(result);
        Assert.Equal(2, result.Count);
        Assert.Equal(3, result[0].Id);
        Assert.Equal("Parent3", result[0].Name);
        Assert.NotNull(result[0].Children);
        Assert.Single(result[0].Children);
    }

    private List<GlobalParentClass> SampleData =
    [
        new GlobalParentClass
        {
            Id = 1,
            Name = "Parent1",
            Children =
            [
                new GlobalChildClass { ChildId = 101, ChildName = "Child1" },
                new GlobalChildClass { ChildId = 102, ChildName = "Child2" }
            ]
        },
        new GlobalParentClass
        {
            Id = 2,
            Name = "Parent2",
            Children =
            [
                new GlobalChildClass { ChildId = 201, ChildName = "Child3" }
            ]
        }
    ];

    private List<GlobalParentClassNullable> SampleDataNullable =
    [
        new GlobalParentClassNullable
        {
            Id = 3,
            Name = "Parent3",
            Children =
            [
                new GlobalChildClass { ChildId = 301, ChildName = "Child4" }
            ]
        },
        new GlobalParentClassNullable
        {
            Id = 4,
            Name = "Parent4",
            Children = null
        }
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
    public List<GlobalChildClass>? Children { get; set; }
}
