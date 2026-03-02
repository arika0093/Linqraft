using System.Collections.Generic;
using System.Linq;
using Linqraft.Tests.OfTypeTestNamespace;

namespace Linqraft.Tests;

/// <summary>
/// Tests for OfType&lt;T&gt;() usage inside SelectExpr.
/// The bug: generic argument &lt;T&gt; was not being converted to its full name,
/// which prevented the generated expression tree from compiling.
/// </summary>
public class Issue_OfTypeInSelectExprTest
{
    private readonly List<OfTypeParent> _testData =
    [
        new OfTypeParent
        {
            Id = 1,
            Items =
            [
                new OfTypeChildA { Name = "A1", AValue = 10 },
                new OfTypeChildB { Name = "B1", BValue = 20 },
                new OfTypeChildA { Name = "A2", AValue = 30 },
            ],
        },
        new OfTypeParent
        {
            Id = 2,
            Items =
            [
                new OfTypeChildB { Name = "B2", BValue = 40 },
            ],
        },
    ];

    [Fact]
    public void OfType_InSelectExpr_ShouldFilterCorrectly()
    {
        // OfType<OfTypeChildA> should only include OfTypeChildA instances
        var result = _testData
            .AsQueryable()
            .SelectExpr(x => new
            {
                x.Id,
                AItems = x.Items.OfType<OfTypeChildA>().ToList(),
            })
            .ToList();

        result.Count.ShouldBe(2);
        result[0].AItems.Count.ShouldBe(2);
        result[0].AItems[0].Name.ShouldBe("A1");
        result[0].AItems[1].Name.ShouldBe("A2");
        result[1].AItems.Count.ShouldBe(0);
    }

    [Fact]
    public void OfType_InSelectExprWithExplicitDto_ShouldFilterCorrectly()
    {
        var result = _testData
            .AsQueryable()
            .SelectExpr<OfTypeParent, OfTypeResultDto>(x => new
            {
                x.Id,
                AItems = x.Items.OfType<OfTypeChildA>().ToList(),
            })
            .ToList();

        result.Count.ShouldBe(2);
        result[0].AItems.Count.ShouldBe(2);
        result[1].AItems.Count.ShouldBe(0);
    }
}

public partial class OfTypeResultDto;
