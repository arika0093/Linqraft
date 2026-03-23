using System.Collections.Generic;
using System.Linq;

namespace Linqraft.Tests;

public sealed class GroupByExprRuntimeTests
{
    private static readonly List<GroupByExprRecord> Records =
    [
        new()
        {
            Id = 1,
            Region = "North",
            Status = "Healthy",
            Score = 10,
        },
        new()
        {
            Id = 2,
            Region = "North",
            Status = "Healthy",
            Score = 20,
        },
        new()
        {
            Id = 3,
            Region = "South",
            Status = "Warning",
            Score = 30,
        },
        new()
        {
            Id = 4,
            Region = "South",
            Status = "Healthy",
            Score = 40,
        },
    ];

    [Test]
    public void Queryable_GroupByExpr_projects_aggregates_and_nested_rows()
    {
        var result = Records
            .AsTestQueryable()
            .GroupByExpr<GroupByExprRecord, string, GroupByExprSummaryDto>(
                record => record.Region,
                group => new
                {
                    Region = group.Key,
                    IsHealthy = group.All(x => x.Status == "Healthy"),
                    Rows = group.Select(x => new { x.Id, x.Status }),
                }
            )
            .OrderBy(x => x.Region)
            .ToList();

        result.Count.ShouldBe(2);
        result[0].Region.ShouldBe("North");
        result[0].IsHealthy.ShouldBeTrue();
        result[0].Rows.Select(x => x.Id).ShouldBe([1, 2]);
        result[1].Region.ShouldBe("South");
        result[1].IsHealthy.ShouldBeFalse();
        result[1].Rows.Select(x => x.Status).ShouldBe(["Warning", "Healthy"]);
    }

    [Test]
    public void UseLinqraft_GroupBy_projects_aggregates()
    {
        var result = Records
            .AsTestQueryable()
            .UseLinqraft()
            .GroupBy<string, GroupByExprFluentSummaryDto>(
                record => record.Region,
                group => new
                {
                    Region = group.Key,
                    ScoreTotal = group.Sum(x => x.Score),
                    Count = group.Count(),
                }
            )
            .OrderBy(x => x.Region)
            .ToList();

        result.Count.ShouldBe(2);
        result[0].Region.ShouldBe("North");
        result[0].ScoreTotal.ShouldBe(30);
        result[1].Region.ShouldBe("South");
        result[1].Count.ShouldBe(2);
    }

    [Test]
    public void IEnumerable_UseLinqraft_GroupBy_projects_aggregates()
    {
        var result = Records
            .UseLinqraft()
            .GroupBy<string, GroupByExprFluentSummaryDto>(
                record => record.Region,
                group => new
                {
                    Region = group.Key,
                    ScoreTotal = group.Sum(x => x.Score),
                    Count = group.Count(),
                }
            )
            .OrderBy(x => x.Region)
            .ToList();

        result.Count.ShouldBe(2);
        result[0].Region.ShouldBe("North");
        result[0].ScoreTotal.ShouldBe(30);
        result[1].Count.ShouldBe(2);
    }

    [Test]
    public void UseLinqraft_GroupBy_supports_delegate_capture()
    {
        var threshold = 25;
        var result = Records
            .AsTestQueryable()
            .UseLinqraft()
            .GroupBy<string, GroupByExprFluentSummaryDto>(
                record => record.Score >= threshold ? "High" : "Low",
                group => new
                {
                    Region = group.Key,
                    ScoreTotal = group.Sum(x => x.Score),
                    Count = group.Count(),
                },
                capture: () => threshold
            )
            .OrderBy(x => x.Region)
            .ToList();

        result.Count.ShouldBe(2);
        result[0].Region.ShouldBe("High");
        result[0].Count.ShouldBe(2);
        result[1].Region.ShouldBe("Low");
        result[1].ScoreTotal.ShouldBe(30);
    }

    [Test]
    public void IEnumerable_GroupByExpr_supports_nested_object_and_internal_select()
    {
        var result = Records.GroupByExpr<GroupByExprRecord, string, GroupByExprNestedDto>(
            record => record.Region,
            group => new
            {
                Region = group.Key,
                Stats = new
                {
                    Count = group.Count(),
                    Labels = group.Select(x => x.Status).OrderBy(x => x),
                },
            }
        );

        var ordered = result.OrderBy(x => x.Region).ToList();
        ordered.Count.ShouldBe(2);
        ordered[0].Stats.Count.ShouldBe(2);
        ordered[0].Stats.Labels.ShouldBe(["Healthy", "Healthy"]);
        ordered[1].Stats.Labels.ShouldBe(["Healthy", "Warning"]);
    }

    [Test]
    public void GroupByExpr_can_project_nested_select_with_calculated_values()
    {
        var result = Records
            .AsTestQueryable()
            .GroupByExpr<GroupByExprRecord, string, GroupByExprScoreDto>(
                record => record.Region,
                group => new
                {
                    Region = group.Key,
                    ScoreRows = group
                        .Select(x => new { x.Id, Label = x.Status + ":" + x.Score })
                        .OrderBy(x => x.Id),
                }
            )
            .OrderBy(x => x.Region)
            .ToList();

        result[0].ScoreRows.Select(x => x.Label).ShouldBe(["Healthy:10", "Healthy:20"]);
        result[1].ScoreRows.Select(x => x.Label).ShouldBe(["Warning:30", "Healthy:40"]);
    }
}

public sealed class GroupByExprRecord
{
    public int Id { get; set; }
    public string Region { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int Score { get; set; }
}

public partial class GroupByExprSummaryDto;

public partial class GroupByExprFluentSummaryDto;

public partial class GroupByExprNestedDto;

public partial class GroupByExprScoreDto;
