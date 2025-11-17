using System.Collections.Generic;
using System.Linq;

namespace Linqraft.Tests.Configuration;

/// <summary>
/// Test for Issue #39: Custom global namespace configuration
/// This test verifies default behavior. To test custom configurations,
/// create additional test projects with LinqraftGlobalNamespace set in .csproj
/// </summary>
public class Issue39_CustomGlobalNamespaceTest
{
    [Fact]
    public void Test_DefaultGlobalNamespace()
    {
        // Default behavior: DTOs are placed in the same namespace as the calling code
        var rst = SampleData
            .AsQueryable()
            .SelectExpr<GlobalTestClass, GlobalTestDto>(s => new { s.Id, s.Name })
            .ToList();

        rst.Count.ShouldBe(2);
        var first = rst[0];
        first.Id.ShouldBe(1);
        first.Name.ShouldBe("Alice");
        
        // Verify the DTO is in the expected namespace
        var type = first.GetType();
        type.Namespace.ShouldBe("Linqraft.Tests.Configuration");
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
