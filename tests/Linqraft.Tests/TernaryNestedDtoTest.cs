using System.Collections.Generic;
using System.Linq;

namespace Linqraft.Tests;

/// <summary>
/// Tests for nested DTO patterns that include ternary operators.
/// Addresses issue where ternary operators with nested anonymous types generate invalid type names.
/// </summary>
public class TernaryNestedDtoTest
{
    internal class Parent
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public Child? Child { get; set; }
    }

    internal class Child
    {
        public string Name { get; set; } = "";
        public int Value { get; set; }
    }

    [Fact]
    public void TernaryWithNestedAnonymousType_ExplicitDto()
    {
        var testData = new List<Parent>
        {
            new Parent
            {
                Id = 1,
                Name = "Parent1",
                Child = new Child { Name = "Child1", Value = 10 },
            },
            new Parent
            {
                Id = 2,
                Name = "Parent2",
                Child = null,
            },
        };

        var result = testData
            .AsQueryable()
            .SelectExpr<Parent, ParentDto>(p => new
            {
                p.Id,
                ChildItemInfo = p.Child != null
                    ? new { Name = p.Child.Name }
                    : null
            })
            .ToList();

        result.Count.ShouldBe(2);

        var first = result[0];
        first.Id.ShouldBe(1);
        first.ChildItemInfo.ShouldNotBeNull();
        first.ChildItemInfo!.Name.ShouldBe("Child1");

        var second = result[1];
        second.Id.ShouldBe(2);
        second.ChildItemInfo.ShouldBeNull();
    }

    [Fact]
    public void TernaryWithNestedAnonymousType_Anonymous()
    {
        var testData = new List<Parent>
        {
            new Parent
            {
                Id = 1,
                Name = "Parent1",
                Child = new Child { Name = "Child1", Value = 10 },
            },
            new Parent
            {
                Id = 2,
                Name = "Parent2",
                Child = null,
            },
        };

        var result = testData
            .AsQueryable()
            .SelectExpr(p => new
            {
                p.Id,
                ChildItemInfo = p.Child != null
                    ? new { Name = p.Child.Name }
                    : null
            })
            .ToList();

        result.Count.ShouldBe(2);

        var first = result[0];
        first.Id.ShouldBe(1);
        first.ChildItemInfo.ShouldNotBeNull();
        first.ChildItemInfo!.Name.ShouldBe("Child1");

        var second = result[1];
        second.Id.ShouldBe(2);
        second.ChildItemInfo.ShouldBeNull();
    }
}
