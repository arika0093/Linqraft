using System.Collections.Generic;
using System.Linq;

namespace Linqraft.Tests;

public static partial class MappingHelperTestQueries
{
    [LinqraftMappingGenerate("ProjectToHelperLeftJoinDto")]
    internal static IQueryable<MappingHelperLeftJoinOrderDto> DummyLeftJoin(
        this IQueryable<MappingHelperOrder> source
    ) =>
        source.SelectExpr<MappingHelperOrder, MappingHelperLeftJoinOrderDto>(
            (order, helper) =>
                new { order.Id, CustomerName = helper.AsLeftJoin(order.Customer!).Name }
        );

    [LinqraftMappingGenerate("ProjectToHelperInnerJoinDto")]
    internal static IQueryable<MappingHelperInnerJoinOrderDto> DummyInnerJoin(
        this IQueryable<MappingHelperOrder> source
    ) =>
        source.SelectExpr<MappingHelperOrder, MappingHelperInnerJoinOrderDto>(
            (order, helper) =>
                new { order.Id, CustomerName = helper.AsInnerJoin(order.Customer!).Name }
        );
}

public sealed class HelperWithMappingGenerateTest
{
    private static readonly List<MappingHelperOrder> Orders =
    [
        new()
        {
            Id = 1,
            Customer = new MappingHelperCustomer { Name = "Ada" },
        },
        new() { Id = 2, Customer = null },
    ];

    [Test]
    public void MappingGenerate_with_AsLeftJoin_helper_preserves_all_rows()
    {
        var result = MappingHelperTestQueries
            .ProjectToHelperLeftJoinDto(Orders.AsTestQueryable())
            .OrderBy(row => row.Id)
            .ToList();

        result.Count.ShouldBe(2);
        result[0].CustomerName.ShouldBe("Ada");
        result[1].CustomerName.ShouldBeNull();
    }

    [Test]
    public void MappingGenerate_with_AsInnerJoin_helper_filters_null_navigation_rows()
    {
        var result = MappingHelperTestQueries
            .ProjectToHelperInnerJoinDto(Orders.AsTestQueryable())
            .OrderBy(row => row.Id)
            .ToList();

        result.Count.ShouldBe(1);
        result[0].CustomerName.ShouldBe("Ada");
    }
}

public sealed class MappingHelperOrder
{
    public int Id { get; set; }

    public MappingHelperCustomer? Customer { get; set; }
}

public sealed class MappingHelperCustomer
{
    public string Name { get; set; } = string.Empty;
}

public partial class MappingHelperLeftJoinOrderDto;

public partial class MappingHelperInnerJoinOrderDto;
