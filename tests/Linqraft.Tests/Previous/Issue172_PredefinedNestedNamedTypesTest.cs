using System.Collections.Generic;
using System.Linq;

namespace Linqraft.Tests;

/// <summary>
/// Test for issue 172: When using Predefined, conversion of complex objects does not work correctly.
/// Named types in nested Select or ternary expressions should use fully qualified names.
/// </summary>
public class Issue172_PredefinedNestedNamedTypesTest
{
    private readonly List<Issue172_Entity> _data =
    [
        new Issue172_Entity
        {
            Id = 1,
            Name = "Entity1",
            Child = new Issue172_Child { Description = "Child Description" },
            Items =
            [
                new Issue172_Item
                {
                    Title = "Item1",
                    Childs =
                    [
                        new Issue172_ItemChild { Test = "Test1" },
                        new Issue172_ItemChild { Test = "Test2" },
                    ],
                },
            ],
        },
    ];

    /// <summary>
    /// Tests that named types in nested Select expressions are fully qualified.
    /// Issue: i.Childs.Select(c => new ItemChildDto { Test = c.Test }) was generated without full qualification
    /// </summary>
    [Fact]
    public void SelectExpr_PredefinedDto_NestedSelectWithNamedType_ShouldBeFullyQualified()
    {
        var result = _data
            .AsQueryable()
            .SelectExpr(x => new Issue172_EntityDto
            {
                Id = x.Id,
                Name = x.Name,
                ChildDescription = x.Child?.Description,
                Items = x.Items.Select(i => new Issue172_ItemDto
                {
                    Title = i.Title,
                    // This nested Select with named type should use fully qualified names
                    Childs =
                        i.Childs != null
                            ? i.Childs.Select(c => new Issue172_ItemChildDto { Test = c.Test })
                            : null,
                }),
            })
            .ToList();

        result.ShouldNotBeNull();
        result.Count.ShouldBe(1);
        var first = result[0];
        first.Id.ShouldBe(1);
        first.Name.ShouldBe("Entity1");
        first.ChildDescription.ShouldBe("Child Description");
        first.Items.ShouldNotBeNull();
        first.Items.Count().ShouldBe(1);
        var firstItem = first.Items.First();
        firstItem.Title.ShouldBe("Item1");
        firstItem.Childs.ShouldNotBeNull();
        firstItem.Childs!.Count().ShouldBe(2);
    }

    /// <summary>
    /// Tests that named types in ternary expressions are fully qualified.
    /// Issue: i.Childs != null ? new ItemChildDto { Test = "" } : null was generated without full qualification
    /// </summary>
    [Fact]
    public void SelectExpr_PredefinedDto_TernaryWithNamedType_ShouldBeFullyQualified()
    {
        var result = _data
            .AsQueryable()
            .SelectExpr(x => new Issue172_EntityDto
            {
                Id = x.Id,
                Name = x.Name,
                ChildDescription = x.Child?.Description,
                Items = x.Items.Select(i => new Issue172_ItemDto
                {
                    Title = i.Title,
                    // These ternary expressions with named types should use fully qualified names
                    Child2 = i.Childs != null ? new Issue172_ItemChildDto { Test = "" } : null,
                }),
            })
            .ToList();

        result.ShouldNotBeNull();
        result.Count.ShouldBe(1);
    }

    /// <summary>
    /// Tests that named types with direct object creation are fully qualified.
    /// Issue: Child2Another = new ItemChildDto { Test = i.Title } should use fully qualified names
    /// </summary>
    [Fact]
    public void SelectExpr_PredefinedDto_DirectObjectCreation_ShouldBeFullyQualified()
    {
        var result = _data
            .AsQueryable()
            .SelectExpr(x => new Issue172_EntityDto
            {
                Id = x.Id,
                Name = x.Name,
                ChildDescription = x.Child?.Description,
                Items = x.Items.Select(i => new Issue172_ItemDto
                {
                    Title = i.Title,
                    // Direct object creation with named type should use fully qualified names
                    Child2Another = new Issue172_ItemChildDto { Test = i.Title },
                }),
            })
            .ToList();

        result.ShouldNotBeNull();
        result.Count.ShouldBe(1);
    }
}

// Source entity classes for Issue 172
public class Issue172_Entity
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public Issue172_Child? Child { get; set; }
    public List<Issue172_Item> Items { get; set; } = [];
}

public class Issue172_Child
{
    public string Description { get; set; } = "";
}

public class Issue172_Item
{
    public string Title { get; set; } = "";
    public List<Issue172_ItemChild>? Childs { get; set; }
}

public class Issue172_ItemChild
{
    public string Test { get; set; } = "";
}

// Predefined DTO classes for Issue 172 (in same namespace as source - typical use case)
public class Issue172_EntityDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? ChildDescription { get; set; }
    public IEnumerable<Issue172_ItemDto> Items { get; set; } = [];
}

public class Issue172_ItemDto
{
    public string Title { get; set; } = "";
    public IEnumerable<Issue172_ItemChildDto>? Childs { get; set; }
    public Issue172_ItemChildDto? Child2 { get; set; }
    public Issue172_ItemChildDto? Child2Another { get; set; }
}

public class Issue172_ItemChildDto
{
    public string Test { get; set; } = "";
}
