using System.Collections.Generic;
using System.Linq;

namespace Linqraft.Tests;

/// <summary>
/// Tests for issue: Insufficient transformation involving anonymous type creation.
/// This tests the following patterns:
/// 1. Ternary operator with Select and empty array literal []
/// 2. Null-conditional Select ?.Select(...) with null-coalescing ?? []
/// 3. Combined null-conditional with null-coalescing
///
/// Expected behavior:
/// - [] should be replaced with global::System.Linq.Enumerable.Empty{T}()
/// - ?. should be converted to explicit null check
/// </summary>
public class Issue_TernaryAndNullConditionalWithAnonymousTypeTest
{
    internal class Container
    {
        public List<Item>? Items { get; set; }
        public List<Item>? NullableItems { get; set; }
    }

    internal class Item
    {
        public required string Title { get; set; }
    }

    private readonly List<Container> _testData =
    [
        new Container
        {
            Items = [new Item { Title = "Item1" }],
            NullableItems = [new Item { Title = "NullableItem1" }],
        },
        new Container { Items = null, NullableItems = null },
    ];

    /// <summary>
    /// Pattern 1: Ternary with Select and empty array literal []
    /// x.Items != null ? x.Items.Select(i => new { i.Title }) : []
    /// Expected: Replace [] with Enumerable.Empty{T}()
    /// </summary>
    [Fact]
    public void TernaryWithSelect_EmptyArrayLiteral_ShouldGenerateEnumerableEmpty()
    {
        var result = _testData
            .AsQueryable()
            .SelectExpr<Container, TernaryTestDto1>(x => new
            {
                Items = x.Items != null ? x.Items.Select(i => new { i.Title }) : [],
            })
            .ToList();

        result.Count.ShouldBe(2);

        // First item has data
        var first = result[0];
        first.Items.ShouldNotBeNull();
        first.Items.Count().ShouldBe(1);
        first.Items.First().Title.ShouldBe("Item1");

        // Second item should have empty collection (not null)
        var second = result[1];
        second.Items.ShouldNotBeNull();
        second.Items.Count().ShouldBe(0);
    }

    /// <summary>
    /// Pattern 2: Null-conditional Select with null-coalescing []
    /// x.Items?.Select(i => new { i.Title }) ?? []
    /// Expected: Convert ?. to explicit null check, replace [] with Enumerable.Empty{T}()
    /// </summary>
    [Fact]
    public void NullConditionalSelect_WithNullCoalescing_ShouldGenerateExplicitNullCheck()
    {
        var result = _testData
            .AsQueryable()
            .SelectExpr<Container, TernaryTestDto2>(x => new
            {
                Items = x.Items?.Select(i => new { i.Title }) ?? [],
            })
            .ToList();

        result.Count.ShouldBe(2);

        // First item has data
        var first = result[0];
        first.Items.ShouldNotBeNull();
        first.Items.Count().ShouldBe(1);
        first.Items.First().Title.ShouldBe("Item1");

        // Second item should have empty collection (not null)
        var second = result[1];
        second.Items.ShouldNotBeNull();
        second.Items.Count().ShouldBe(0);
    }

    /// <summary>
    /// Pattern 3: Combined null-conditional with null-coalescing on nullable property
    /// x.NullableItems?.Select(i => new { i.Title }) ?? []
    /// Expected: Same handling as Pattern 2
    /// </summary>
    [Fact]
    public void NullableProperty_NullConditionalSelect_ShouldGenerateCorrectCode()
    {
        var result = _testData
            .AsQueryable()
            .SelectExpr<Container, TernaryTestDto3>(x => new
            {
                Items = x.NullableItems?.Select(i => new { i.Title }) ?? [],
            })
            .ToList();

        result.Count.ShouldBe(2);

        // First item has data
        var first = result[0];
        first.Items.ShouldNotBeNull();
        first.Items.Count().ShouldBe(1);
        first.Items.First().Title.ShouldBe("NullableItem1");

        // Second item should have empty collection (not null)
        var second = result[1];
        second.Items.ShouldNotBeNull();
        second.Items.Count().ShouldBe(0);
    }
}
