using System.Collections.Generic;
using System.Linq;

namespace Linqraft.Tests;

public sealed class SelectManyExprRuntimeTests
{
    private static readonly List<SelectManyExprParent> Parents =
    [
        new()
        {
            Id = 10,
            Name = "ParentA",
            Children =
            [
                new()
                {
                    Id = 101,
                    Name = "ChildA-1",
                    GrandChildren =
                    [
                        new() { Id = 1001, Description = "GC-A-1" },
                        new() { Id = 1002, Description = "GC-A-2" },
                    ],
                },
                new()
                {
                    Id = 102,
                    Name = "ChildA-2",
                    GrandChildren = [new() { Id = 1003, Description = "GC-A-3" }],
                },
            ],
        },
        new()
        {
            Id = 20,
            Name = "ParentB",
            Children =
            [
                new()
                {
                    Id = 201,
                    Name = "ChildB-1",
                    GrandChildren = [],
                },
            ],
        },
    ];

    [Test]
    public void Queryable_SelectManyExpr_projects_flattened_rows()
    {
        var result = Parents
            .AsTestQueryable()
            .SelectManyExpr<SelectManyExprParent, SelectManyExprChildRowDto>(parent =>
                parent.Children.Select(child => new
                {
                    ParentId = parent.Id,
                    ChildId = child.Id,
                    ChildName = child.Name,
                })
            )
            .OrderBy(x => x.ChildId)
            .ToList();

        result.Count.ShouldBe(3);
        result[0].ParentId.ShouldBe(10);
        result[0].ChildName.ShouldBe("ChildA-1");
        result[2].ParentId.ShouldBe(20);
        result[2].ChildId.ShouldBe(201);
    }

    [Test]
    public void UseLinqraft_SelectMany_projects_flattened_rows()
    {
        var result = Parents
            .AsTestQueryable()
            .UseLinqraft()
            .SelectMany<SelectManyExprFluentChildRowDto>(parent =>
                parent.Children.Select(child => new
                {
                    ParentId = parent.Id,
                    ChildId = child.Id,
                    ChildName = child.Name,
                })
            )
            .OrderBy(x => x.ChildId)
            .ToList();

        result.Count.ShouldBe(3);
        result[0].ParentId.ShouldBe(10);
        result[0].ChildName.ShouldBe("ChildA-1");
        result[2].ParentId.ShouldBe(20);
        result[2].ChildId.ShouldBe(201);
    }

    [Test]
    public void IEnumerable_UseLinqraft_SelectMany_projects_flattened_rows()
    {
        var result = Parents
            .UseLinqraft()
            .SelectMany<SelectManyExprFluentChildRowDto>(parent =>
                parent.Children.Select(child => new
                {
                    ParentId = parent.Id,
                    ChildId = child.Id,
                    ChildName = child.Name,
                })
            )
            .OrderBy(x => x.ChildId)
            .ToList();

        result.Count.ShouldBe(3);
        result[0].ParentId.ShouldBe(10);
        result[2].ChildId.ShouldBe(201);
    }

    [Test]
    public void UseLinqraft_SelectMany_supports_delegate_capture()
    {
        var minimumGrandChildren = 1;
        var result = Parents
            .AsTestQueryable()
            .UseLinqraft()
            .SelectMany<SelectManyExprFluentChildRowDto>(
                parent =>
                    parent
                        .Children.Where(child => child.GrandChildren.Count >= minimumGrandChildren)
                        .Select(child => new
                        {
                            ParentId = parent.Id,
                            ChildId = child.Id,
                            ChildName = child.Name,
                        }),
                capture: () => minimumGrandChildren
            )
            .OrderBy(x => x.ChildId)
            .ToList();

        result.Count.ShouldBe(2);
        result.Select(x => x.ChildId).ShouldBe([101, 102]);
    }

    [Test]
    public void SelectManyExpr_supports_internal_select_in_projected_members()
    {
        var result = Parents
            .AsTestQueryable()
            .SelectManyExpr<SelectManyExprParent, SelectManyExprGrandChildListDto>(parent =>
                parent.Children.Select(child => new
                {
                    ParentName = parent.Name,
                    ChildName = child.Name,
                    GrandChildren = child.GrandChildren.Select(gc => new { gc.Id, gc.Description }),
                })
            )
            .OrderBy(x => x.ChildName)
            .ToList();

        result.Count.ShouldBe(3);
        result[0].GrandChildren.Select(x => x.Description).ShouldBe(["GC-A-1", "GC-A-2"]);
        result[2].GrandChildren.ShouldBeEmpty();
    }

    [Test]
    public void IEnumerable_SelectManyExpr_can_flatten_nested_selectmany()
    {
        var result = Parents.SelectManyExpr<SelectManyExprParent, SelectManyExprGrandChildRowDto>(
            parent =>
                parent.Children.SelectMany(child =>
                    child.GrandChildren.Select(grandChild => new
                    {
                        ParentId = parent.Id,
                        ChildId = child.Id,
                        GrandChildId = grandChild.Id,
                        grandChild.Description,
                    })
                )
        );

        var ordered = result.OrderBy(x => x.GrandChildId).ToList();
        ordered.Count.ShouldBe(3);
        ordered[0].ChildId.ShouldBe(101);
        ordered[2].Description.ShouldBe("GC-A-3");
    }
}

public sealed class SelectManyExprParent
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<SelectManyExprChild> Children { get; set; } = [];
}

public sealed class SelectManyExprChild
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<SelectManyExprGrandChild> GrandChildren { get; set; } = [];
}

public sealed class SelectManyExprGrandChild
{
    public int Id { get; set; }
    public string Description { get; set; } = string.Empty;
}
