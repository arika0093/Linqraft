// Verify that the generated Child3Datas is non-nullable
// To do so, we want to ensure that no CS8604 warning occurs
// For verification, this project is configured to treat CS8604 as an error instead of a warning

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

        // CS8604 should not occur here when accessing Child3Datas
        var rst = result[0].Child3Datas.First().Id;
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

        // CS8604 should not occur here when accessing Child3Datas
        var rst = result[0].Child3Datas.First().Id;
    }

    [Fact]
    public void NullableParent_WithSelectSimple_ShouldGenerateNonNullableCollection()
    {
        // When Select is used with simple type projection (not anonymous type),
        // the collection should now be non-nullable (List<int>, not List<int>?)
        // because we generate empty collection fallback for all Select/SelectMany expressions
        var result = _testData
            .AsQueryable()
            .SelectExpr<NullableParentTestData, NullableParentSimpleDto>(d => new
            {
                Child3Ids = d.InnerData?.Childs.Select(c => c.Child3.Id).ToList(),
            })
            .ToList();

        result.Count.ShouldBe(2);

        // CS8604 should not occur here when accessing Child3Ids - collection is non-nullable
        var rst = result[0].Child3Ids.First();
        rst.ShouldBe(1);

        // When source is null, the collection should be empty (not null)
        result[1].Child3Ids.ShouldNotBeNull();
        result[1].Child3Ids.Count.ShouldBe(0);
    }
}
