using System.Collections.Generic;
using System.Linq;
using Linqraft;

namespace Linqraft.Tests;

public sealed class AdvancedProjectionOperatorRuntimeTests
{
    private static readonly List<ProjectionHealthRecord> HealthRecords =
    [
        new() { Id = 1, Region = "North", Status = "Healthy" },
        new() { Id = 2, Region = "North", Status = "Healthy" },
        new() { Id = 3, Region = "South", Status = "Warning" },
    ];

    [Test]
    public void GroupByExpr_projects_aggregates_and_nested_rows()
    {
        var result = HealthRecords
            .AsTestQueryable()
            .GroupByExpr<ProjectionHealthRecord, string, ProjectionHealthGroupDto>(
                record => record.Region,
                group => new
                {
                    Region = group.Key,
                    AllHealthy = group.All(x => x.Status == "Healthy"),
                    Records = group.Select(x => new { x.Id, x.Status }),
                }
            )
            .OrderBy(x => x.Region)
            .ToList();

        result.Count.ShouldBe(2);
        result[0].Region.ShouldBe("North");
        result[0].AllHealthy.ShouldBeTrue();
        result[0].Records.Select(x => x.Id).ShouldBe([1, 2]);
        result[1].Region.ShouldBe("South");
        result[1].AllHealthy.ShouldBeFalse();
        result[1].Records.Select(x => x.Status).ShouldBe(["Warning"]);
    }

    [Test]
    public void SelectManyExpr_projects_flattened_rows()
    {
        var result = new List<ParentEntity>
        {
            new()
            {
                Id = 10,
                Name = "ParentA",
                Children =
                [
                    new ChildEntity { ChildId = 101, ChildName = "ChildA-1" },
                    new ChildEntity { ChildId = 102, ChildName = "ChildA-2" },
                ],
            },
            new()
            {
                Id = 20,
                Name = "ParentB",
                Children = [new ChildEntity { ChildId = 201, ChildName = "ChildB-1" }],
            },
        }
            .AsTestQueryable()
            .SelectManyExpr<ParentEntity, ProjectionFlatChildDto>(parent =>
                parent.Children.Select(child => new
                {
                    ParentId = parent.Id,
                    child.ChildId,
                    child.ChildName,
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
    public void LinqraftKit_Generate_converts_anonymous_object_variable()
    {
        var dto = LinqraftKit.Generate<ProjectionGeneratedOrderDto>(
            new
            {
                Id = 42,
                Customer = new { Name = "Ada" },
                Items = new[]
                {
                    new { Name = "Keyboard" },
                    new { Name = "Mouse" },
                },
            }
        );

        dto.Id.ShouldBe(42);
        dto.Customer.Name.ShouldBe("Ada");
        dto.Items.Select(x => x.Name).ShouldBe(["Keyboard", "Mouse"]);
    }
}

public sealed class ProjectionHealthRecord
{
    public int Id { get; set; }
    public string Region { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}

public partial class ProjectionHealthGroupDto;

public partial class ProjectionFlatChildDto;

public partial class ProjectionGeneratedOrderDto
{
    public int Id { get; set; }
    public ProjectionGeneratedCustomerDto Customer { get; set; } = new();
    public IEnumerable<ProjectionGeneratedItemDto> Items { get; set; } = [];
}

public sealed class ProjectionGeneratedCustomerDto
{
    public string Name { get; set; } = string.Empty;
}

public sealed class ProjectionGeneratedItemDto
{
    public string Name { get; set; } = string.Empty;
}
