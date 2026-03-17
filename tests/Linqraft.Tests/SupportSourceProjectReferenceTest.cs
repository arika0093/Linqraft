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
        var dependencyProjection = Orders.AsTestQueryable().ProjectFromDependency().ToList();

        dependencyProjection.Count.ShouldBe(1);
        dependencyProjection[0].Id.ShouldBe(1);
        dependencyProjection[0].CustomerName.ShouldBe("Ada");
        dependencyProjection[0].LineCount.ShouldBe(2);

        var localProjection = Orders
            .AsTestQueryable()
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

    [Test]
    public void Project_reference_dependency_preserves_external_property_types()
    {
        var projection = Orders
            .AsTestQueryable()
            .SelectExpr<ReferencedOrder, ReferencedOrderWithExternalTypesDto>(order => new
            {
                order.Id,
                order.Customer,
                order.Items,
            })
            .ToList();

        projection.Count.ShouldBe(1);
        projection[0].Id.ShouldBe(1);
        projection[0].Customer.Name.ShouldBe("Ada");
        projection[0].Items.Count.ShouldBe(2);

        var dtoType = typeof(ReferencedOrderWithExternalTypesDto);
        dtoType.GetProperty(nameof(ReferencedOrderWithExternalTypesDto.Customer))!.PropertyType.ShouldBe(
            typeof(ReferencedCustomer)
        );
        dtoType.GetProperty(nameof(ReferencedOrderWithExternalTypesDto.Items))!.PropertyType.ShouldBe(
            typeof(List<ReferencedOrderItem>)
        );
    }

    [Test]
    public void Project_reference_dependency_preserves_external_object_creation_types()
    {
        var projection = Orders
            .AsTestQueryable()
            .SelectExpr<ReferencedOrder, ReferencedOrderWithConstructedExternalTypesDto>(order => new
            {
                ClonedCustomer = new ReferencedCustomer { Name = order.Customer.Name },
                ClonedItems = order.Items.Select(item => new ReferencedOrderItem
                {
                    Quantity = item.Quantity,
                }),
            })
            .ToList();

        projection.Count.ShouldBe(1);
        projection[0].ClonedCustomer.Name.ShouldBe("Ada");
        projection[0].ClonedItems.Select(item => item.Quantity).ShouldBe([2, 3]);

        var dtoType = typeof(ReferencedOrderWithConstructedExternalTypesDto);
        dtoType.GetProperty(nameof(ReferencedOrderWithConstructedExternalTypesDto.ClonedCustomer))!
            .PropertyType.ShouldBe(typeof(ReferencedCustomer));
        dtoType.GetProperty(nameof(ReferencedOrderWithConstructedExternalTypesDto.ClonedItems))!
            .PropertyType.ShouldBe(typeof(IEnumerable<ReferencedOrderItem>));
    }

}

public partial class ReferencedOrderFromCurrentProjectDto;

public partial class ReferencedOrderWithExternalTypesDto;

public partial class ReferencedOrderWithConstructedExternalTypesDto;
