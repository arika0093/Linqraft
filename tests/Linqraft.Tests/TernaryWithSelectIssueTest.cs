using System;
using System.Collections.Generic;
using System.Linq;

namespace Linqraft.Tests;

/// <summary>
/// Tests for issue: The conversion of very complex objects is not handled correctly.
/// Specifically tests ternary expressions containing .Select() calls with anonymous types.
/// </summary>
public class TernaryWithSelectIssueTest
{
    #region Test Data Classes
    internal class TestData
    {
        public int Id { get; set; }
        public required string Name { get; set; }
        public required List<ChildData> Children { get; set; }
    }

    internal class ChildData
    {
        public int Id { get; set; }
        public required string Description { get; set; }
        public required Child2 Child2 { get; set; }
    }

    internal class Child2
    {
        public Child3? Child3 { get; set; }
    }

    internal class Child3
    {
        public required List<Child4> Child4s { get; set; }
    }

    internal class Child4
    {
        public required DateTimeOffset CreatedAt { get; set; }
    }
    #endregion

    private readonly List<TestData> _datas =
    [
        new TestData
        {
            Id = 1,
            Name = "Test1",
            Children =
            [
                new ChildData
                {
                    Id = 10,
                    Description = "Child10",
                    Child2 = new Child2
                    {
                        Child3 = new Child3
                        {
                            Child4s =
                            [
                                new Child4
                                {
                                    CreatedAt = new DateTimeOffset(
                                        2024,
                                        1,
                                        1,
                                        0,
                                        0,
                                        0,
                                        TimeSpan.Zero
                                    ),
                                },
                                new Child4
                                {
                                    CreatedAt = new DateTimeOffset(
                                        2024,
                                        2,
                                        1,
                                        0,
                                        0,
                                        0,
                                        TimeSpan.Zero
                                    ),
                                },
                            ],
                        },
                    },
                },
            ],
        },
        new TestData
        {
            Id = 2,
            Name = "Test2",
            Children =
            [
                new ChildData
                {
                    Id = 20,
                    Description = "Child20",
                    Child2 = new Child2 { Child3 = null }, // null Child3
                },
            ],
        },
    ];

    [Fact]
    public void TernaryWithSelect_NotNullCondition_ExplicitDto()
    {
        // Pattern: c.Child2.Child3 != null ? c.Child2.Child3.Child4s.Select(...) : null
        var result = _datas
            .AsQueryable()
            .SelectExpr<TestData, TestDataDto1>(d => new
            {
                d.Id,
                d.Name,
                Children = d.Children.Select(c => new
                {
                    c.Id,
                    c.Description,
                    // This is the problematic pattern - ternary with .Select() inside
                    ChildQuery = c.Child2.Child3 != null
                        ? c.Child2.Child3.Child4s.Select(ch4 => new { ch4.CreatedAt })
                        : null,
                }),
            })
            .ToList();

        result.Count.ShouldBe(2);

        // First item has Child3
        var first = result[0];
        first.Id.ShouldBe(1);
        first.Name.ShouldBe("Test1");
        first.Children.ShouldNotBeNull();
        var firstChild = first.Children.First();
        firstChild.Id.ShouldBe(10);
        firstChild.Description.ShouldBe("Child10");
        firstChild.ChildQuery.ShouldNotBeNull();
        firstChild.ChildQuery!.Count().ShouldBe(2);

        // Second item has null Child3
        var second = result[1];
        second.Id.ShouldBe(2);
        var secondChild = second.Children.First();
        secondChild.ChildQuery.ShouldBeNull();
    }

    [Fact]
    public void TernaryWithSelect_EqualNullCondition_ExplicitDto()
    {
        // Pattern: c.Child2.Child3 == null ? null : c.Child2.Child3.Child4s.Select(...)
        var result = _datas
            .AsQueryable()
            .SelectExpr<TestData, TestDataDto2>(d => new
            {
                d.Id,
                d.Name,
                Children = d.Children.Select(c => new
                {
                    c.Id,
                    c.Description,
                    // This is the reversed condition pattern
                    ChildQuery = c.Child2.Child3 == null
                        ? null
                        : c.Child2.Child3.Child4s.Select(ch4 => new { ch4.CreatedAt }),
                }),
            })
            .ToList();

        result.Count.ShouldBe(2);

        // First item has Child3
        var first = result[0];
        first.Id.ShouldBe(1);
        var firstChild = first.Children.First();
        firstChild.ChildQuery.ShouldNotBeNull();
        firstChild.ChildQuery!.Count().ShouldBe(2);

        // Second item has null Child3
        var second = result[1];
        var secondChild = second.Children.First();
        secondChild.ChildQuery.ShouldBeNull();
    }

    // Note: The null-conditional pattern (c.Child2.Child3?.Child4s.Select(...)) is tested elsewhere
    // and requires separate null-check expression tracking. This test file focuses on ternary patterns.
}
