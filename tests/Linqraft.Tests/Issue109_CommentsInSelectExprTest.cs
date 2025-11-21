using System;
using System.Collections.Generic;
using System.Linq;

namespace Linqraft.Tests;

public class Issue109_CommentsInSelectExprTest
{
    [Fact]
    public void Test_CommentsInLinqChain()
    {
        List<TestForIssue109> datas =
        [
            new()
            {
                Id = 1,
                Name = "Parent 1",
                Children =
                [
                    new()
                    {
                        Id = 1,
                        Description = "Child 1-1",
                        IsActive = true,
                    },
                    new()
                    {
                        Id = 2,
                        Description = "Child 1-2",
                        IsActive = false,
                    },
                ],
            },
            new()
            {
                Id = 2,
                Name = "Parent 2",
                Children =
                [
                    new()
                    {
                        Id = 3,
                        Description = "Child 2-1",
                        IsActive = true,
                    },
                    new()
                    {
                        Id = 4,
                        Description = "Child 2-2",
                        IsActive = true,
                    },
                ],
            },
        ];

        var result = datas
            .AsQueryable()
            .SelectExpr<TestForIssue109, TestForIssue109Dto>(s => new
            {
                Id = s.Id,
                ActiveChildren = s
                    .Children
                    // Filter only active children
                    .Where(x => x.IsActive)
                    .Select(x => new { Id = x.Id, Description = x.Description })
                    .ToList(),
            })
            .ToList();

        result.Count.ShouldBe(2);
        result[0].Id.ShouldBe(1);
        result[0].ActiveChildren.Count.ShouldBe(1);
        result[0].ActiveChildren[0].Id.ShouldBe(1);
        result[0].ActiveChildren[0].Description.ShouldBe("Child 1-1");

        result[1].Id.ShouldBe(2);
        result[1].ActiveChildren.Count.ShouldBe(2);
        result[1].ActiveChildren[0].Id.ShouldBe(3);
        result[1].ActiveChildren[1].Id.ShouldBe(4);
    }

    [Fact]
    public void Test_MultipleCommentsInLinqChain()
    {
        List<TestForIssue109> datas =
        [
            new()
            {
                Id = 1,
                Name = "Parent 1",
                Children =
                [
                    new()
                    {
                        Id = 1,
                        Description = "Child 1-1",
                        IsActive = true,
                    },
                    new()
                    {
                        Id = 2,
                        Description = "Child 1-2",
                        IsActive = false,
                    },
                    new()
                    {
                        Id = 3,
                        Description = "Child 1-3",
                        IsActive = true,
                    },
                ],
            },
        ];

        var result = datas
            .AsQueryable()
            .SelectExpr<TestForIssue109, TestForIssue109Dto>(s => new
            {
                Id = s.Id,
                ActiveChildren = s
                    .Children
                    // First filter by IsActive
                    .Where(x => x.IsActive)
                    // Then select the DTO
                    .Select(x => new
                    {
                        // Map the ID
                        Id = x.Id,
                        // Map the description
                        Description = x.Description,
                    })
                    // Convert to list
                    .ToList(),
            })
            .ToList();

        result.Count.ShouldBe(1);
        result[0].ActiveChildren.Count.ShouldBe(2);
        result[0].ActiveChildren[0].Id.ShouldBe(1);
        result[0].ActiveChildren[1].Id.ShouldBe(3);
    }
}

internal class TestForIssue109
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required List<TestForIssue109Child> Children { get; set; }
}

internal class TestForIssue109Child
{
    public int Id { get; set; }
    public required string Description { get; set; }
    public bool IsActive { get; set; }
}

internal partial class TestForIssue109Dto { }
