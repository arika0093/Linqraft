using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace Linqraft.Tests;

public sealed class GeneratedProjectionRuntimeTests
{
    private static readonly List<ProjectionOrder> Orders =
    [
        new()
        {
            Id = 1,
            Customer = new ProjectionCustomer { Name = "Ada" },
            Items = [new ProjectionOrderItem { Name = "Keyboard" }],
        },
        new()
        {
            Id = 2,
            Customer = new ProjectionCustomer { Name = "Grace" },
            Items =
            [
                new ProjectionOrderItem { Name = "Mouse" },
                new ProjectionOrderItem { Name = "Trackpad" },
            ],
        },
    ];

    private static readonly List<ProjectionProduct> Products =
    [
        new() { Id = 1, Name = "Laptop" },
        new() { Id = 2, Name = "Phone" },
    ];

    private static readonly List<ProjectionPerson> People =
    [
        new() { Id = 1, FirstName = "Ada", LastName = "Lovelace" },
        new() { Id = 2, FirstName = "Grace", LastName = "Hopper" },
    ];

    private static readonly List<ProjectionInvoice> Invoices =
    [
        new() { Id = 1, Total = 49m },
        new() { Id = 2, Total = 150m },
    ];

    [Test]
    public void Explicit_dto_projection_runs()
    {
        var result = Orders
            .AsQueryable()
            .SelectExpr<ProjectionOrder, ProjectionOrderDto>(order => new
            {
                order.Id,
                CustomerName = order.Customer?.Name,
                ItemCount = order.Items.Count,
            })
            .ToList();

        result.Count.ShouldBe(2);
        result[0].Id.ShouldBe(1);
        result[0].CustomerName.ShouldBe("Ada");
        result[0].ItemCount.ShouldBe(1);
        result[1].CustomerName.ShouldBe("Grace");
        result[1].ItemCount.ShouldBe(2);
    }

    [Test]
    public void Predefined_dto_projection_runs()
    {
        var result = Products
            .AsQueryable()
            .SelectExpr(product => new ProjectionProductRow
            {
                Id = product.Id,
                DisplayName = product.Name + "!",
            })
            .ToList();

        result.Count.ShouldBe(2);
        result[0].GetType().ShouldBe(typeof(ProjectionProductRow));
        result[0].DisplayName.ShouldBe("Laptop!");
        result[1].DisplayName.ShouldBe("Phone!");
    }

    [Test]
    public void Anonymous_projection_runs()
    {
        var result = People
            .AsQueryable()
            .SelectExpr(person => new
            {
                person.Id,
                DisplayName = person.FirstName + " " + person.LastName,
            })
            .ToList();

        result.Count.ShouldBe(2);
        result[0].DisplayName.ShouldBe("Ada Lovelace");
        result[1].DisplayName.ShouldBe("Grace Hopper");
    }

    [Test]
    public void IEnumerable_projection_runs()
    {
        var result = People
            .SelectExpr<ProjectionPerson, ProjectionPersonListRow>(person => new
            {
                person.Id,
                DisplayName = person.LastName + ", " + person.FirstName,
            })
            .ToList();

        result.Count.ShouldBe(2);
        result[0].DisplayName.ShouldBe("Lovelace, Ada");
        result[1].DisplayName.ShouldBe("Hopper, Grace");
    }

    [Test]
    public void Captured_value_projection_runs()
    {
        const decimal threshold = 50m;

        var result = Invoices
            .AsQueryable()
            .SelectExpr<ProjectionInvoice, ProjectionDecisionDto>(
                invoice => new
                {
                    invoice.Id,
                    IsLarge = invoice.Total >= threshold,
                },
                new { threshold }
            )
            .ToList();

        result.Count.ShouldBe(2);
        result[0].IsLarge.ShouldBeFalse();
        result[1].IsLarge.ShouldBeTrue();
    }

    [Test]
    public void IQueryable_projection_preserves_query_provider()
    {
        const decimal threshold = 50m;
        var source = TrackingQueryable.Create<ProjectionInvoice>();

        var result = source.SelectExpr<ProjectionInvoice, ProjectionDecisionDto>(
            invoice => new
            {
                invoice.Id,
                IsLarge = invoice.Total >= threshold,
            },
            new { threshold }
        );

        result.Provider.ShouldBe(source.Provider);
        result.Expression.ToString().ShouldContain("Select");
    }

    [Test]
    public void Predeclared_property_is_populated()
    {
        var result = Orders
            .AsQueryable()
            .SelectExpr<ProjectionOrder, ProjectionDeclaredOrderDto>(order => new
            {
                order.Id,
                CustomerName = order.Customer?.Name,
            })
            .ToList();

        result.Count.ShouldBe(2);
        result[0].Id.ShouldBe(1);
        result[0].CustomerName.ShouldBe("Ada");
        typeof(ProjectionDeclaredOrderDto).GetProperty(nameof(ProjectionDeclaredOrderDto.Id))!.SetMethod!.IsPrivate.ShouldBeTrue();
    }
}

public sealed class ProjectionOrder
{
    public int Id { get; set; }
    public ProjectionCustomer? Customer { get; set; }
    public List<ProjectionOrderItem> Items { get; set; } = [];
}

public sealed class ProjectionCustomer
{
    public string Name { get; set; } = string.Empty;
}

public sealed class ProjectionOrderItem
{
    public string Name { get; set; } = string.Empty;
}

public sealed class ProjectionProduct
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public sealed class ProjectionProductRow
{
    public int Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;
}

public sealed class ProjectionPerson
{
    public int Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
}

public sealed class ProjectionInvoice
{
    public int Id { get; set; }
    public decimal Total { get; set; }
}

public partial class ProjectionDeclaredOrderDto
{
    public int Id { get; private set; }
}

internal static class TrackingQueryable
{
    public static TrackingQueryable<T> Create<T>()
    {
        var provider = new TrackingQueryProvider();
        return new TrackingQueryable<T>(provider);
    }
}

internal sealed class TrackingQueryable<T> : IQueryable<T>
{
    public TrackingQueryable(TrackingQueryProvider provider)
        : this(provider, null)
    {
    }

    public TrackingQueryable(TrackingQueryProvider provider, Expression? expression)
    {
        Provider = provider;
        Expression = expression ?? Expression.Constant(this);
    }

    public Type ElementType => typeof(T);

    public Expression Expression { get; }

    public IQueryProvider Provider { get; }

    public IEnumerator<T> GetEnumerator() => throw new NotSupportedException();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

internal sealed class TrackingQueryProvider : IQueryProvider
{
    public IQueryable CreateQuery(Expression expression)
    {
        var elementType = expression.Type.GetGenericArguments().Last();
        return (IQueryable)Activator.CreateInstance(
            typeof(TrackingQueryable<>).MakeGenericType(elementType),
            this,
            expression
        )!;
    }

    public IQueryable<TElement> CreateQuery<TElement>(Expression expression)
    {
        return new TrackingQueryable<TElement>(this, expression);
    }

    public object? Execute(Expression expression) => throw new NotSupportedException();

    public TResult Execute<TResult>(Expression expression) => throw new NotSupportedException();
}
