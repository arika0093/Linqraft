using System.Collections.Generic;
using System.Linq;

namespace Linqraft.Tests;

/// <summary>
/// Test for Issue #39: Custom global namespace configuration
/// Tests the LinqraftGlobalNamespace MSBuild property
/// </summary>
public class Issue39_CustomGlobalNamespaceTest
{
    [Fact]
    public void Test_DefaultGlobalNamespace()
    {
        // Default behavior: global namespace DTOs go to "Linqraft" namespace
        // This test verifies existing behavior is preserved
        var rst = SampleData
            .AsQueryable()
            .SelectExpr<GlobalTestClass, GlobalTestDto>(s => new { s.Id, s.Name })
            .ToList();

        rst.Count.ShouldBe(2);
        var first = rst[0];
        first.Id.ShouldBe(1);
        first.Name.ShouldBe("Alice");
    }

    private List<GlobalTestClass> SampleData =
    [
        new GlobalTestClass { Id = 1, Name = "Alice" },
        new GlobalTestClass { Id = 2, Name = "Bob" },
    ];
}

internal class GlobalTestClass
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}
