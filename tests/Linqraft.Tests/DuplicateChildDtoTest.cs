using System.Collections.Generic;
using System.Linq;

namespace Linqraft.Tests;

/// <summary>
/// Test for ensuring that when ChildDto of the same shape appears multiple times,
/// the definition of ChildDto should be generated only once.
/// This is a reproduction of the bug reported in the issue.
/// </summary>
public partial class DuplicateChildDtoTest
{
    private readonly List<Parent> _testData =
    [
        new Parent
        {
            Id = 1,
            Children1 = [new Child { Id = 1, Name = "Child1A" }, new Child { Id = 2, Name = "Child1B" }],
            Children2 = [new Child { Id = 3, Name = "Child2A" }, new Child { Id = 4, Name = "Child2B" }],
        },
    ];

    [Fact]
    public void ShouldGenerateSingleChildDtoForMultiplePropertiesWithSameShape()
    {
        // Arrange & Act
        var result = _testData
            .AsQueryable()
            .SelectExpr<Parent, ParentDto>(p => new
            {
                p.Id,
                // Both properties select the same shape: { Id, Name }
                // This should generate only ONE ChildDto class, not two
                Children1 = p.Children1.Select(c => new { c.Id, c.Name }).ToList(),
                Children2 = p.Children2.Select(c => new { c.Id, c.Name }).ToList(),
            })
            .ToList();

        // Assert
        result.ShouldNotBeNull();
        result.Count.ShouldBe(1);
        result[0].Id.ShouldBe(1);
        result[0].Children1.Count.ShouldBe(2);
        result[0].Children1[0].Id.ShouldBe(1);
        result[0].Children1[0].Name.ShouldBe("Child1A");
        result[0].Children2.Count.ShouldBe(2);
        result[0].Children2[0].Id.ShouldBe(3);
        result[0].Children2[0].Name.ShouldBe("Child2A");
    }

    internal partial class ParentDto
    {
        public int Id { get; set; }
    }

    internal class Parent
    {
        public int Id { get; set; }
        public List<Child> Children1 { get; set; } = [];
        public List<Child> Children2 { get; set; } = [];
    }

    internal class Child
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }
}
