using System.Collections.Generic;
using System.Linq;

namespace Linqraft.Tests;

/// <summary>
/// Test for Issue #41: Property accessor configuration
/// Tests the LinqraftPropertyAccessor MSBuild property
/// </summary>
public class Issue41_PropertyAccessorTest
{
    [Fact]
    public void Test_DefaultPropertyAccessor()
    {
        // Default behavior for classes: get; set;
        var rst = SampleData
            .AsQueryable()
            .SelectExpr<PropertyAccessorTestClass, PropertyAccessorTestDto>(s => new { s.Id, s.Name })
            .ToList();

        rst.Count.ShouldBe(2);
        var first = rst[0];
        first.Id.ShouldBe(1);
        first.Name.ShouldBe("Alice");

        // Verify property is settable
        first.Id = 100;
        first.Id.ShouldBe(100);
        first.Name = "Updated";
        first.Name.ShouldBe("Updated");
    }

    private List<PropertyAccessorTestClass> SampleData =
    [
        new PropertyAccessorTestClass { Id = 1, Name = "Alice" },
        new PropertyAccessorTestClass { Id = 2, Name = "Bob" },
    ];
}

internal class PropertyAccessorTestClass
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}
