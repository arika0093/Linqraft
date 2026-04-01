using System.Collections.Generic;
using System.Linq;

namespace Linqraft.Tests;

/// <summary>
/// Regression tests for:
/// - Chained null-conditional type inference (entity?.Nav.Property → should be string?, not object)
/// - Inner lambda parameter type resolution (collection.Select(r => r.Prop) → correct element type)
/// </summary>
public sealed partial class Issue_ChainedNullConditionalAndInnerLambdaTypeTest
{
    #region Models

    internal class Order
    {
        public int OrderId { get; set; }
        public string Code { get; set; } = "";
        public OrderDetail OrderDetail { get; set; } = new();
        public List<OrderTag> Tags { get; set; } = [];
    }

    internal class OrderDetail
    {
        public string Description { get; set; } = "";
        public string? Note { get; set; }
    }

    internal enum TagCategory
    {
        Priority = 1,
        Standard = 2,
        Low = 3,
    }

    internal class OrderTag
    {
        public TagCategory Category { get; set; }
        public string Label { get; set; } = "";
    }

    internal class ProductLog
    {
        public int Id { get; set; }
        public Order? RelatedOrder { get; set; }
        public string Action { get; set; } = "";
    }

    #endregion

    #region DTOs

    internal partial class ProductLogViewDto { }

    internal partial class OrderManageDto { }

    internal partial class OrderWithTagsDto { }

    #endregion

    #region Test Data

    private static readonly List<ProductLog> ProductLogs =
    [
        new()
        {
            Id = 1,
            Action = "Created",
            RelatedOrder = new Order
            {
                OrderId = 100,
                Code = "ORD-100",
                OrderDetail = new OrderDetail
                {
                    Description = "Sample order",
                    Note = "note@example.com",
                },
            },
        },
        new()
        {
            Id = 2,
            Action = "Deleted",
            RelatedOrder = null,
        },
    ];

    private static readonly List<Order> Orders =
    [
        new()
        {
            OrderId = 1,
            Code = "ORD-1",
            OrderDetail = new OrderDetail { Description = "First" },
            Tags =
            [
                new OrderTag { Category = TagCategory.Priority, Label = "Priority" },
                new OrderTag { Category = TagCategory.Standard, Label = "Standard" },
            ],
        },
        new()
        {
            OrderId = 2,
            Code = "ORD-2",
            OrderDetail = new OrderDetail { Description = "Second" },
            Tags = [new OrderTag { Category = TagCategory.Low, Label = "Low" }],
        },
    ];

    #endregion

    [Test]
    public void ChainedNullConditional_InfersCorrectType()
    {
        // entity?.Navigation.Property should infer string?, not object
        var result = ProductLogs
            .AsTestQueryable()
            .SelectExpr<ProductLog, ProductLogViewDto>(l => new
            {
                l.Id,
                l.Action,
                OrderDescription = l.RelatedOrder?.OrderDetail.Description,
                OrderCode = l.RelatedOrder?.Code,
            })
            .ToList();

        result.Count.ShouldBe(2);
        result[0].OrderDescription.ShouldBe("Sample order");
        result[0].OrderCode.ShouldBe("ORD-100");
        result[1].OrderDescription.ShouldBeNull();
        result[1].OrderCode.ShouldBeNull();

        // Verify the generated DTO property types are correct (not object)
        var dtoType = typeof(ProductLogViewDto);
        dtoType
            .GetProperty(nameof(ProductLogViewDto.OrderDescription))!
            .PropertyType.ShouldBe(typeof(string));
        dtoType
            .GetProperty(nameof(ProductLogViewDto.OrderCode))!
            .PropertyType.ShouldBe(typeof(string));
    }

    [Test]
    public void InnerLambdaSelect_InfersCorrectEnumType()
    {
        // collection.Select(r => r.EnumProp) should infer IEnumerable<EnumType>, not IEnumerable<object>
        var result = Orders
            .AsTestQueryable()
            .SelectExpr<Order, OrderManageDto>(x => new
            {
                x.OrderId,
                x.Code,
                TagCategories = x.Tags.Select(t => t.Category),
                x.OrderDetail.Description,
            })
            .ToList();

        result.Count.ShouldBe(2);
        result[0].TagCategories.ShouldBe([TagCategory.Priority, TagCategory.Standard]);
        result[1].TagCategories.ShouldBe([TagCategory.Low]);

        // Verify the generated DTO property type is correct
        var prop = typeof(OrderManageDto).GetProperty(nameof(OrderManageDto.TagCategories))!;
        prop.PropertyType.ShouldNotBe(typeof(IEnumerable<object>));
    }

    [Test]
    public void InnerLambdaSelect_InfersCorrectStringType()
    {
        // collection.Select(r => r.StringProp).ToList() should infer List<string>, not List<object>
        var result = Orders
            .AsTestQueryable()
            .SelectExpr<Order, OrderWithTagsDto>(o => new
            {
                o.OrderId,
                TagLabels = o.Tags.Select(t => t.Label).ToList(),
            })
            .ToList();

        result.Count.ShouldBe(2);
        result[0].TagLabels.ShouldBe(["Priority", "Standard"]);
        result[1].TagLabels.ShouldBe(["Low"]);

        // Verify the generated DTO property type is List<string>, not List<object>
        var prop = typeof(OrderWithTagsDto).GetProperty(nameof(OrderWithTagsDto.TagLabels))!;
        prop.PropertyType.ShouldNotBe(typeof(List<object>));
    }
}
