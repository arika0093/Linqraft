using System.Collections.Generic;
using System.Linq;

namespace Linqraft.Tests;

/// <summary>
/// Test class for Issue 193: null-conditional in object creation initializer
/// and empty collection fallback patterns.
/// </summary>
public class Issue193_NullConditionalInInitializerTest
{
    private readonly List<Issue193_Entity> _data =
    [
        new Issue193_Entity
        {
            Id = 1,
            Name = "Entity1",
            Items = [new Issue193_Item { Title = "Item1" }, new Issue193_Item { Title = null }],
        },
    ];

    /// <summary>
    /// Tests that when using null-conditional operator inside an object initializer,
    /// the null check is applied to the property value, not wrapped around the object creation.
    /// Input:  new ItemChildDto { Test = i?.Title }
    /// Output: new global::...ItemChildDto { Test = i != null ? i.Title : null }
    /// NOT:    new ItemChildDto { Test = i != null ? (Type?)new ItemChildDto { Test = i.Title } : null }
    /// </summary>
    [Fact]
    public void SelectExpr_PredefinedDto_NullConditionalInInitializer_ShouldOnlyAffectProperty()
    {
        var result = _data
            .AsQueryable()
            .SelectExpr(x => new Issue193_EntityDto
            {
                Id = x.Id,
                Name = x.Name,
                Items = x.Items.Select(i => new Issue193_ItemDto
                {
                    Title = i.Title,
                    // The null-conditional should only apply to the Test property
                    // NOT wrap the entire object creation
                    Child2Another = new Issue193_ItemChildDto { Test = i?.Title },
                }),
            })
            .ToList();

        result.ShouldNotBeNull();
        result.Count.ShouldBe(1);
        var items = result[0].Items.ToList();
        items.Count.ShouldBe(2);

        // First item has Title = "Item1"
        items[0].Child2Another.ShouldNotBeNull();
        items[0].Child2Another.Test.ShouldBe("Item1");

        // Second item has Title = null
        items[1].Child2Another.ShouldNotBeNull();
        items[1].Child2Another.Test.ShouldBeNull();
    }

    /// <summary>
    /// Tests that when using ternary operator with empty array literal fallback,
    /// the generated code uses fully qualified empty collection expression.
    /// Input:  x != null ? x.Select(y => new Xxx { ... }) : []
    /// Output: ... : global::System.Linq.Enumerable.Empty&lt;global::Namespace.Xxx&gt;()
    /// </summary>
    [Fact]
    public void SelectExpr_PredefinedDto_TernaryWithEmptyArrayLiteral_ShouldUseFullyQualifiedEmpty()
    {
        var data = new List<Issue193_Entity>
        {
            new()
            {
                Id = 1,
                Name = "Entity1",
                NullableItems = null,
            },
            new()
            {
                Id = 2,
                Name = "Entity2",
                NullableItems = [new Issue193_Item { Title = "Item1" }],
            },
        };

        var result = data.AsQueryable()
            .SelectExpr(x => new Issue193_EntityWithNullableItemsDto
            {
                Id = x.Id,
                Name = x.Name,
                // Empty array literal fallback - should become global::System.Linq.Enumerable.Empty<...>()
                Items =
                    x.NullableItems != null
                        ? x.NullableItems.Select(i => new Issue193_ItemChildDto { Test = i.Title })
                        : [],
            })
            .ToList();

        result.ShouldNotBeNull();
        result.Count.ShouldBe(2);

        // First entity has null NullableItems, so Items should be empty
        result[0].Items.ShouldNotBeNull();
        result[0].Items.Count().ShouldBe(0);

        // Second entity has NullableItems, so Items should have one item
        result[1].Items.ShouldNotBeNull();
        result[1].Items.Count().ShouldBe(1);
    }

    /// <summary>
    /// Tests that when using ternary operator with Enumerable.Empty&lt;T&gt;() fallback,
    /// the generated code uses fully qualified names for both Enumerable and the type parameter.
    /// Input:  x != null ? x.Select(y => new Xxx { ... }) : Enumerable.Empty&lt;Xxx&gt;()
    /// Output: ... : global::System.Linq.Enumerable.Empty&lt;global::Namespace.Xxx&gt;()
    /// </summary>
    [Fact]
    public void SelectExpr_PredefinedDto_TernaryWithEnumerableEmpty_ShouldUseFullyQualifiedEmpty()
    {
        var data = new List<Issue193_Entity>
        {
            new()
            {
                Id = 1,
                Name = "Entity1",
                NullableItems = null,
            },
            new()
            {
                Id = 2,
                Name = "Entity2",
                NullableItems = [new Issue193_Item { Title = "Item1" }],
            },
        };

        var result = data.AsQueryable()
            .SelectExpr(x => new Issue193_EntityWithNullableItemsDto
            {
                Id = x.Id,
                Name = x.Name,
                // Enumerable.Empty<T>() fallback - should become global::System.Linq.Enumerable.Empty<global::...>()
                Items =
                    x.NullableItems != null
                        ? x.NullableItems.Select(i => new Issue193_ItemChildDto { Test = i.Title })
                        : Enumerable.Empty<Issue193_ItemChildDto>(),
            })
            .ToList();

        result.ShouldNotBeNull();
        result.Count.ShouldBe(2);

        // First entity has null NullableItems, so Items should be empty
        result[0].Items.ShouldNotBeNull();
        result[0].Items.Count().ShouldBe(0);

        // Second entity has NullableItems, so Items should have one item
        result[1].Items.ShouldNotBeNull();
        result[1].Items.Count().ShouldBe(1);
    }
}

// Source entity classes for Issue 193
public class Issue193_Entity
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public List<Issue193_Item> Items { get; set; } = [];
    public List<Issue193_Item>? NullableItems { get; set; }
}

public class Issue193_Item
{
    public string? Title { get; set; }
}

// Predefined DTO classes for Issue 193
public class Issue193_EntityDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public IEnumerable<Issue193_ItemDto> Items { get; set; } = [];
}

public class Issue193_EntityWithNullableItemsDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public IEnumerable<Issue193_ItemChildDto> Items { get; set; } = [];
}

public class Issue193_ItemDto
{
    public string? Title { get; set; }
    public Issue193_ItemChildDto Child2Another { get; set; } = new();
}

public class Issue193_ItemChildDto
{
    public string? Test { get; set; }
}
