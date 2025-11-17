using System.Collections.Generic;
using System.Linq;

namespace Linqraft.Tests;

/// <summary>
/// Test for Issue #40: Record generation
/// Tests the LinqraftRecordGenerate MSBuild property
/// </summary>
public class Issue40_RecordGenerateTest
{
    [Fact]
    public void Test_ClassGeneration()
    {
        // Default behavior: generate classes
        var rst = SampleData
            .AsQueryable()
            .SelectExpr<RecordTestClass, RecordTestDto>(s => new { s.Id, s.Name })
            .ToList();

        rst.Count.ShouldBe(2);
        var first = rst[0];
        first.Id.ShouldBe(1);
        first.Name.ShouldBe("Alice");
        
        // Verify it's a class by checking the type
        // The generated DTO should be a class by default
        var type = first.GetType();
        type.Name.ShouldBe("RecordTestDto");
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
