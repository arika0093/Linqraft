using System.Collections.Generic;
using System.Linq;
using Linqraft.Tests.ProjectReferenceDependency;

namespace Linqraft.Tests;

public sealed class SupportSourceProjectReferenceTest
{
    private static readonly List<ReferencedOrder> Orders =
    [
        new()
        {
            Id = 1,
            Customer = new ReferencedCustomer { Name = "Ada" },
            Items =
            [
                new ReferencedOrderItem { Quantity = 2 },
                new ReferencedOrderItem { Quantity = 3 },
            ],
        },
    ];

    [Test]
    public void Project_reference_dependency_can_use_linqraft_without_duplicate_support_sources()
    {
        var dependencyProjection = Orders.AsQueryable().ProjectFromDependency().ToList();

        dependencyProjection.Count.ShouldBe(1);
        dependencyProjection[0].Id.ShouldBe(1);
        dependencyProjection[0].CustomerName.ShouldBe("Ada");
        dependencyProjection[0].LineCount.ShouldBe(2);

        var localProjection = Orders
            .AsQueryable()
            .SelectExpr<ReferencedOrder, ReferencedOrderFromCurrentProjectDto>(order => new
            {
                order.Id,
                CustomerName = order.Customer.Name,
                QuantityTotal = order.Items.Sum(item => item.Quantity),
            })
            .ToList();

        localProjection.Count.ShouldBe(1);
        localProjection[0].Id.ShouldBe(1);
        localProjection[0].CustomerName.ShouldBe("Ada");
        localProjection[0].QuantityTotal.ShouldBe(5);
    }
}

public partial class ReferencedOrderFromCurrentProjectDto;
