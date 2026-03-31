using System;
using System.Collections.Generic;
using System.Linq;

namespace Linqraft.Tests;

public class Issue_NullConditionalProjectionEmissionTest
{
    private static readonly Guid AliceId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset AppliedAt = new(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly List<IssueConditionalProjectionOrder> _orders =
    [
        new()
        {
            OrderId = "ORD-001",
            Customer = new IssueConditionalProjectionCustomer
            {
                Name = "Alice",
                Id = AliceId,
                Address = new IssueConditionalProjectionAddress { City = "Tokyo" },
            },
            Discount = new IssueConditionalProjectionDiscount { AppliedAt = AppliedAt },
        },
        new() { OrderId = "ORD-002" },
    ];

    [Test]
    public void SelectExpr_AnonymousProjection_WithNullConditionalMembers_ShouldCompileAndProjectValues()
    {
        var result = _orders
            .AsTestQueryable()
            .SelectExpr<IssueConditionalProjectionOrder, IssueConditionalProjectionGeneratedDto>(o => new
            {
                o.OrderId,
                CustomerName = o.Customer?.Name,
                CustomerId = o.Customer?.Id,
                CustomerCity = o.Customer?.Address?.City,
                DiscountAppliedAt = o.Discount?.AppliedAt,
            })
            .ToList();

        AssertProjectedValues(result);
    }

    [Test]
    public void SelectExpr_TypedProjection_WithNullConditionalMembers_ShouldCompileAndProjectValues()
    {
        var result = _orders
            .AsTestQueryable()
            .SelectExpr(o => new IssueConditionalProjectionTypedDto
            {
                OrderId = o.OrderId,
                CustomerName = o.Customer?.Name,
                CustomerId = o.Customer?.Id,
                CustomerCity = o.Customer?.Address?.City,
                DiscountAppliedAt = o.Discount?.AppliedAt,
            })
            .ToList();

        AssertProjectedValues(result);
    }

    private static void AssertProjectedValues(
        IReadOnlyList<IssueConditionalProjectionExpectation> result
    )
    {
        result.Count.ShouldBe(2);

        var first = result[0];
        first.OrderId.ShouldBe("ORD-001");
        first.CustomerName.ShouldBe("Alice");
        first.CustomerId.ShouldBe(AliceId);
        first.CustomerCity.ShouldBe("Tokyo");
        first.DiscountAppliedAt.ShouldBe(AppliedAt);

        var second = result[1];
        second.OrderId.ShouldBe("ORD-002");
        second.CustomerName.ShouldBeNull();
        second.CustomerId.ShouldBeNull();
        second.CustomerCity.ShouldBeNull();
        second.DiscountAppliedAt.ShouldBeNull();
    }
}

public interface IssueConditionalProjectionExpectation
{
    string OrderId { get; }
    string? CustomerName { get; }
    Guid? CustomerId { get; }
    string? CustomerCity { get; }
    DateTimeOffset? DiscountAppliedAt { get; }
}

public class IssueConditionalProjectionOrder
{
    public string OrderId { get; set; } = "";
    public IssueConditionalProjectionCustomer? Customer { get; set; }
    public IssueConditionalProjectionDiscount? Discount { get; set; }
}

public class IssueConditionalProjectionCustomer
{
    public string Name { get; set; } = "";
    public Guid Id { get; set; }
    public IssueConditionalProjectionAddress? Address { get; set; }
}

public class IssueConditionalProjectionAddress
{
    public string City { get; set; } = "";
}

public class IssueConditionalProjectionDiscount
{
    public DateTimeOffset? AppliedAt { get; set; }
}

partial class IssueConditionalProjectionGeneratedDto : IssueConditionalProjectionExpectation { }

public class IssueConditionalProjectionTypedDto : IssueConditionalProjectionExpectation
{
    public string OrderId { get; set; } = "";
    public string? CustomerName { get; set; }
    public Guid? CustomerId { get; set; }
    public string? CustomerCity { get; set; }
    public DateTimeOffset? DiscountAppliedAt { get; set; }
}
