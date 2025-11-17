using System.Collections.Generic;
using System.Linq;

namespace Linqraft.Tests;

/// <summary>
/// Test for LinqraftHasRequired configuration
/// Tests the LinqraftHasRequired MSBuild property
/// </summary>
public class LinqraftHasRequiredTest
{
    [Fact]
    public void Test_DefaultHasRequired()
    {
        // Default behavior: required keyword is used
        var rst = SampleData
            .AsQueryable()
            .SelectExpr<HasRequiredTestClass, HasRequiredTestDto>(s => new { s.Id, s.Name })
            .ToList();

        rst.Count.ShouldBe(2);
        var first = rst[0];
        first.Id.ShouldBe(1);
        first.Name.ShouldBe("Alice");

        // Verify the type is correct
        var type = first.GetType();
        type.Name.ShouldBe("HasRequiredTestDto");
    }

    private List<HasRequiredTestClass> SampleData =
    [
        new HasRequiredTestClass { Id = 1, Name = "Alice" },
        new HasRequiredTestClass { Id = 2, Name = "Bob" },
    ];
}

internal class HasRequiredTestClass
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}
