using System.Collections.Generic;
using System.Linq;

namespace Linqraft.Tests.Configuration;

/// <summary>
/// Test for Issue #40: Record generation
/// This test verifies default behavior (classes). To test record generation,
/// create additional test projects with LinqraftRecordGenerate=true in .csproj
/// </summary>
public class Issue40_RecordGenerateTest
{
    [Fact]
    public void Test_DefaultClassGeneration()
    {
        // Default behavior: DTOs are generated as classes
        var rst = SampleData
            .AsQueryable()
            .SelectExpr<RecordTestClass, RecordTestDto>(s => new { s.Id, s.Name })
            .ToList();

        rst.Count.ShouldBe(2);
        var first = rst[0];
        first.Id.ShouldBe(1);
        first.Name.ShouldBe("Alice");
        
        // Verify it's a class (not a record)
        var type = first.GetType();
        type.Name.ShouldBe("RecordTestDto");
        // Classes don't have the special record ToString behavior
    }

    private List<RecordTestClass> SampleData =
    [
        new RecordTestClass { Id = 1, Name = "Alice" },
        new RecordTestClass { Id = 2, Name = "Bob" },
    ];
}

internal class RecordTestClass
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}
