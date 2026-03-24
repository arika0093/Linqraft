using System.Collections.Generic;
using System.Linq;

namespace Linqraft.Tests;

public static partial class MappingOperatorQueries
{
    [LinqraftMappingGenerate("ProjectToFlattenedChildren")]
    internal static IQueryable<MappingOperatorChildRowDto> DummyFlattened(
        this IQueryable<MappingOperatorParent> query
    ) =>
        query.SelectManyExpr<MappingOperatorParent, MappingOperatorChildRowDto>(parent =>
            parent.Children.Select(child => new
            {
                ParentId = parent.Id,
                ChildId = child.Id,
                ChildName = child.Name,
            })
        );

    [LinqraftMappingGenerate("ProjectToRegionSummaries")]
    internal static IQueryable<MappingOperatorSummaryDto> DummyGrouped(
        this IQueryable<MappingOperatorRecord> query
    ) =>
        query.GroupByExpr<MappingOperatorRecord, string, MappingOperatorSummaryDto>(
            record => record.Region,
            group => new
            {
                Region = group.Key,
                Total = group.Sum(x => x.Amount),
                Count = group.Count(),
            }
        );
}

public sealed class LinqraftMappingQueryOperatorTest
{
    [Test]
    public void MappingGenerate_supports_SelectManyExpr_declarations()
    {
        var result = MappingOperatorQueries
            .ProjectToFlattenedChildren(MappingOperatorParentData.Parents.AsTestQueryable())
            .OrderBy(x => x.ChildId)
            .ToList();

        result.Count.ShouldBe(3);
        result[0].ParentId.ShouldBe(10);
        result[0].ChildName.ShouldBe("ChildA-1");
        result[2].ChildId.ShouldBe(201);
    }

    [Test]
    public void MappingGenerate_supports_GroupByExpr_declarations()
    {
        var result = MappingOperatorQueries
            .ProjectToRegionSummaries(MappingOperatorParentData.Records.AsTestQueryable())
            .OrderBy(x => x.Region)
            .ToList();

        result.Count.ShouldBe(2);
        result[0].Region.ShouldBe("North");
        result[0].Total.ShouldBe(30);
        result[1].Count.ShouldBe(2);
    }
}

internal static class MappingOperatorParentData
{
    public static readonly List<MappingOperatorParent> Parents =
    [
        new()
        {
            Id = 10,
            Children =
            [
                new MappingOperatorChild { Id = 101, Name = "ChildA-1" },
                new MappingOperatorChild { Id = 102, Name = "ChildA-2" },
            ],
        },
        new()
        {
            Id = 20,
            Children = [new MappingOperatorChild { Id = 201, Name = "ChildB-1" }],
        },
    ];

    public static readonly List<MappingOperatorRecord> Records =
    [
        new() { Region = "North", Amount = 10 },
        new() { Region = "North", Amount = 20 },
        new() { Region = "South", Amount = 30 },
        new() { Region = "South", Amount = 40 },
    ];
}

public sealed class MappingOperatorParent
{
    public int Id { get; set; }

    public List<MappingOperatorChild> Children { get; set; } = [];
}

public sealed class MappingOperatorChild
{
    public int Id { get; set; }

    public string Name { get; set; } = "";
}

public sealed class MappingOperatorRecord
{
    public string Region { get; set; } = "";

    public int Amount { get; set; }
}

public sealed partial class MappingOperatorChildRowDto;

public sealed partial class MappingOperatorSummaryDto;
