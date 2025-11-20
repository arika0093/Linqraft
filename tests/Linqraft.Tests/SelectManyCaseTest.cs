using System.Collections.Generic;
using System.Linq;

namespace Linqraft.Tests;

public class SelectManyCaseTest
{
    private readonly List<ParentEntity> TestData =
    [
        new ParentEntity
        {
            Id = 1,
            Name = "Parent1",
            Children = new List<ChildEntity>
            {
                new ChildEntity
                {
                    ChildId = 101,
                    ChildName = "Child1-1",
                    GrandChildren = new List<GrandChildEntity>
                    {
                        new GrandChildEntity
                        {
                            GrandChildId = 1001,
                            Description = "GrandChild1-1-1",
                        },
                        new GrandChildEntity
                        {
                            GrandChildId = 1002,
                            Description = "GrandChild1-1-2",
                        },
                    },
                },
                new ChildEntity
                {
                    ChildId = 102,
                    ChildName = "Child1-2",
                    GrandChildren = new List<GrandChildEntity>
                    {
                        new GrandChildEntity
                        {
                            GrandChildId = 1003,
                            Description = "GrandChild1-2-1",
                        },
                    },
                },
            },
        },
        new ParentEntity
        {
            Id = 2,
            Name = "Parent2",
            Children = new List<ChildEntity>
            {
                new ChildEntity
                {
                    ChildId = 201,
                    ChildName = "Child2-1",
                    GrandChildren = new List<GrandChildEntity>
                    {
                        new GrandChildEntity
                        {
                            GrandChildId = 2001,
                            Description = "GrandChild2-1-1",
                        },
                    },
                },
            },
        },
    ];

    [Fact]
    public void SelectMany_SimpleCase_Anonymous()
    {
        // Flatten children collection using SelectMany
        var converted = TestData
            .AsQueryable()
            .SelectExpr(x => new
            {
                x.Id,
                x.Name,
                AllGrandChildren = x.Children.SelectMany(c => c.GrandChildren),
            })
            .ToList();

        converted.Count.ShouldBe(2);

        var first = converted[0];
        first.Id.ShouldBe(1);
        first.Name.ShouldBe("Parent1");
        // Parent1 has 2 children with 3 grandchildren total (2 + 1)
        first.AllGrandChildren.Count().ShouldBe(3);
        first.AllGrandChildren.Select(gc => gc.GrandChildId).ShouldBe([1001, 1002, 1003]);

        var second = converted[1];
        second.Id.ShouldBe(2);
        second.Name.ShouldBe("Parent2");
        // Parent2 has 1 child with 1 grandchild
        second.AllGrandChildren.Count().ShouldBe(1);
        second.AllGrandChildren.Select(gc => gc.GrandChildId).ShouldBe([2001]);
    }

    [Fact]
    public void SelectMany_WithProjection_Anonymous()
    {
        // SelectMany with projection in lambda
        var converted = TestData
            .AsQueryable()
            .SelectExpr(x => new
            {
                x.Id,
                AllGrandChildDescriptions = x.Children.SelectMany(c =>
                    c.GrandChildren.Select(gc => gc.Description)
                ),
            })
            .ToList();

        converted.Count.ShouldBe(2);

        var first = converted[0];
        first.Id.ShouldBe(1);
        first.AllGrandChildDescriptions.ShouldBe(
            ["GrandChild1-1-1", "GrandChild1-1-2", "GrandChild1-2-1"]
        );

        var second = converted[1];
        second.Id.ShouldBe(2);
        second.AllGrandChildDescriptions.ShouldBe(["GrandChild2-1-1"]);
    }

    [Fact]
    public void SelectMany_ExplicitDto()
    {
        // SelectMany with explicit DTO type
        var converted = TestData
            .AsQueryable()
            .SelectExpr<ParentEntity, ParentWithFlatGrandChildrenDto>(x => new
            {
                x.Id,
                x.Name,
                AllGrandChildren = x.Children.SelectMany(c => c.GrandChildren),
            })
            .ToList();

        converted.Count.ShouldBe(2);
        converted[0].GetType().Name.ShouldBe("ParentWithFlatGrandChildrenDto");

        var first = converted[0];
        first.Id.ShouldBe(1);
        first.AllGrandChildren.Count().ShouldBe(3);
    }

    [Fact]
    public void SelectMany_WithNullConditional()
    {
        // Test data with null collection
        var testData = new List<ParentEntity>
        {
            new ParentEntity
            {
                Id = 1,
                Name = "Parent1",
                Children = new List<ChildEntity>
                {
                    new ChildEntity
                    {
                        ChildId = 101,
                        ChildName = "Child1",
                        GrandChildren = new List<GrandChildEntity>(), // Empty collection
                    },
                },
            },
            new ParentEntity
            {
                Id = 2,
                Name = "Parent2",
                Children = new List<ChildEntity>(), // Empty collection
            },
        };

        var converted = testData
            .AsQueryable()
            .SelectExpr(x => new
            {
                x.Id,
                AllGrandChildren = x.Children.SelectMany(c => c.GrandChildren),
            })
            .ToList();

        converted.Count.ShouldBe(2);

        // First parent has children but grandchildren is empty
        var first = converted[0];
        first.Id.ShouldBe(1);
        first.AllGrandChildren.ShouldBeEmpty();

        // Second parent has empty children
        var second = converted[1];
        second.Id.ShouldBe(2);
        second.AllGrandChildren.ShouldBeEmpty();
    }
}

public class ParentEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public List<ChildEntity> Children { get; set; } = [];
}

public class ChildEntity
{
    public int ChildId { get; set; }
    public string ChildName { get; set; } = null!;
    public List<GrandChildEntity> GrandChildren { get; set; } = [];
}

public class GrandChildEntity
{
    public int GrandChildId { get; set; }
    public string Description { get; set; } = null!;
}
