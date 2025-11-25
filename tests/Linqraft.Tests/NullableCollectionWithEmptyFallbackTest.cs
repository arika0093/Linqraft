using System.Collections.Generic;
using System.Linq;

namespace Linqraft.Tests;

/// <summary>
/// Tests for nullable collection constraint removal when using Enumerable.Empty as fallback.
/// This addresses the issue where IEnumerable&lt;T&gt;? was generated instead of IEnumerable&lt;T&gt;
/// when the expression uses null-conditional before Select with anonymous type
/// (e.g., d.InnerData?.Childs.Select(c => new { ... }))
/// </summary>
public class NullableCollectionWithEmptyFallbackTest
{
    // Test data entities
    internal class NullableParentTestData
    {
        public required NullableParentChildData? InnerData { get; set; }
    }

    internal class NullableParentChildData
    {
        public required List<NullableParentChild2> Childs { get; set; }
    }

    internal class NullableParentChild2
    {
        public required NullableParentChild3 Child3 { get; set; }
    }

    internal class NullableParentChild3
    {
        public required int Id { get; set; }
    }

    private readonly List<NullableParentTestData> _testData =
    [
        new NullableParentTestData
        {
            InnerData = new NullableParentChildData
            {
                Childs =
                [
                    new NullableParentChild2 { Child3 = new NullableParentChild3 { Id = 1 } },
                    new NullableParentChild2 { Child3 = new NullableParentChild3 { Id = 2 } },
                ],
            },
        },
        new NullableParentTestData
        {
            InnerData = null, // Null InnerData
        },
    ];

    [Fact]
    public void NullableParent_WithSelectAnonymous_ShouldGenerateNonNullableCollection()
    {
        // This is the exact scenario from the issue
        // When InnerData is null, Child3Datas should be an empty collection, not null
        var result = _testData
            .AsQueryable()
            .SelectExpr<NullableParentTestData, NullableParentDto>(d => new
            {
                Child3Datas = d.InnerData?.Childs.Select(c => new { c.Child3.Id }),
            })
            .ToList();

        result.Count.ShouldBe(2);

        // First TestData has valid InnerData
        var first = result[0];
        first.Child3Datas.ShouldNotBeNull();
        first.Child3Datas.Count().ShouldBe(2);

        // Second TestData has null InnerData
        // Child3Datas should be an empty collection, not null
        var second = result[1];
        second.Child3Datas.ShouldNotBeNull();
        second.Child3Datas.Count().ShouldBe(0);
    }

    [Fact]
    public void NullableParent_WithSelectAnonymous_ToList_ShouldGenerateNonNullableCollection()
    {
        // Same scenario with ToList() at the end
        var result = _testData
            .AsQueryable()
            .SelectExpr<NullableParentTestData, NullableParentWithListDto>(d => new
            {
                Child3Datas = d.InnerData?.Childs.Select(c => new { c.Child3.Id }).ToList(),
            })
            .ToList();

        result.Count.ShouldBe(2);

        // First TestData has valid InnerData
        var first = result[0];
        first.Child3Datas.ShouldNotBeNull();
        first.Child3Datas.Count.ShouldBe(2);

        // Second TestData has null InnerData
        // Child3Datas should be an empty list, not null
        var second = result[1];
        second.Child3Datas.ShouldNotBeNull();
        second.Child3Datas.Count.ShouldBe(0);
    }

    [Fact]
    public void NullableParent_WithSelectSimple_ShouldRemainNullableCollection()
    {
        // When Select doesn't create an anonymous type, the collection should still be nullable
        // because there's no nested structure that would use Enumerable.Empty<T>() as fallback
        var result = _testData
            .AsQueryable()
            .SelectExpr<NullableParentTestData, NullableParentSimpleDto>(d => new
            {
                Child3Ids = d.InnerData?.Childs.Select(c => c.Child3.Id).ToList(),
            })
            .ToList();

        result.Count.ShouldBe(2);

        // First TestData has valid InnerData
        var first = result[0];
        first.Child3Ids.ShouldNotBeNull();
        first.Child3Ids!.Count.ShouldBe(2);

        // Second TestData has null InnerData
        // Child3Ids should be null because the Select doesn't create anonymous type
        var second = result[1];
        second.Child3Ids.ShouldBeNull();
    }
}
