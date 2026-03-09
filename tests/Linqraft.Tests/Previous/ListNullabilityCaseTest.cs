using System.Collections.Generic;
using System.Linq;

namespace Linqraft.Tests;

public class ListNullabilityCaseTest
{
    private readonly List<ListNullEntity> TestData =
    [
        new ListNullEntity
        {
            Id = 1,
            Children =
            [
                new ListNullChildEntity
                {
                    Description = "Child1",
                    Foo = new ListNullFoo { Bar = "Bar1" },
                },
                new ListNullChildEntity { Description = "Child2", Foo = null },
            ],
        },
        new ListNullEntity
        {
            Id = 2,
            Children =
            [
                new ListNullChildEntity
                {
                    Description = "Child3",
                    Foo = new ListNullFoo { Bar = "Bar3" },
                },
            ],
        },
    ];

    [Fact]
    public void ListProperty_ShouldBeNonNullable_WhenGeneratedFromNestedSelect()
    {
        var converted = TestData
            .AsQueryable()
            .SelectExpr<ListNullEntity, EntityDto>(e => new
            {
                e.Id,
                ChildDescs = e.Children.Select(c => new { c.Description }).ToList(),
            })
            .ToList();

        converted.Count.ShouldBe(2);
        var first = converted[0];
        first.Id.ShouldBe(1);
        first.ChildDescs.Count.ShouldBe(2);
        first.ChildDescs[0].Description.ShouldBe("Child1");

        var second = converted[1];
        second.Id.ShouldBe(2);
        second.ChildDescs.Count.ShouldBe(1);
        second.ChildDescs[0].Description.ShouldBe("Child3");
    }

    [Fact]
    public void ListProperty_WithNullConditional_InNestedLambda_ShouldStillBeNonNullable()
    {
        // This is the exact scenario from issue #18
        // The List property should be non-nullable even though the nested lambda uses ?.
        var converted = TestData
            .AsQueryable()
            .SelectExpr<ListNullEntity, EntityWithNullConditionalDto>(e => new
            {
                e.Id,
                ChildDescs = e
                    .Children.Select(c => new { c.Description, FooBar = c.Foo?.Bar })
                    .ToList(),
            })
            .ToList();

        converted.Count.ShouldBe(2);
        var first = converted[0];
        first.Id.ShouldBe(1);
        first.ChildDescs.Count.ShouldBe(2);
        first.ChildDescs[0].Description.ShouldBe("Child1");
        first.ChildDescs[0].FooBar.ShouldBe("Bar1");
        first.ChildDescs[1].FooBar.ShouldBeNull();
    }

    [Fact]
    public void MultipleListProperties_ShouldBeNonNullable()
    {
        var converted = TestData
            .AsQueryable()
            .SelectExpr<ListNullEntity, EntityMultiListDto>(e => new
            {
                e.Id,
                Descriptions = e.Children.Select(c => c.Description).ToList(),
            })
            .ToList();

        converted.Count.ShouldBe(2);
        var first = converted[0];
        first.Id.ShouldBe(1);
        first.Descriptions.ShouldBe(["Child1", "Child2"]);
    }

    [Fact]
    public void ListOfComplexType_ShouldBeNonNullable()
    {
        var converted = TestData
            .AsQueryable()
            .SelectExpr<ListNullEntity, EntityComplexListDto>(e => new
            {
                e.Id,
                ChildData = e.Children.Select(c => new { c.Description }).ToList(),
            })
            .ToList();

        converted.Count.ShouldBe(2);
        var first = converted[0];
        first.Id.ShouldBe(1);
        first.ChildData.Count.ShouldBe(2);
        first.ChildData[0].Description.ShouldBe("Child1");
    }
}

internal class ListNullEntity
{
    public int Id { get; set; }
    public IEnumerable<ListNullChildEntity> Children { get; set; } = [];
}

internal class ListNullChildEntity
{
    public required string Description { get; set; }
    public ListNullFoo? Foo { get; set; }
}

internal class ListNullFoo
{
    public required string Bar { get; set; }
}
