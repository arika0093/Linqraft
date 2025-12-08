using System.Collections.Generic;
using System.Linq;

namespace Linqraft.Tests.Configuration;

public class PrebuildExpressionTest
{
    private readonly List<SampleEntity> _testData =
    [
        new()
        {
            Id = 1,
            Name = "Entity 1",
            Value = 100,
            Child = new() { ChildId = 10, ChildName = "Child 1" }
        },
        new()
        {
            Id = 2,
            Name = "Entity 2",
            Value = 200,
            Child = new() { ChildId = 20, ChildName = "Child 2" }
        },
        new()
        {
            Id = 3,
            Name = "Entity 3",
            Value = 300,
            Child = null
        },
    ];

    [Fact]
    public void Test_IQueryable_With_Anonymous_Type()
    {
        // Test that pre-built expressions work with IQueryable and anonymous types
        var results = _testData
            .AsQueryable()
            .SelectExpr(s => new
            {
                s.Id,
                s.Name,
                ChildName = s.Child?.ChildName
            })
            .ToList();

        Assert.Equal(3, results.Count);
        Assert.Equal(1, results[0].Id);
        Assert.Equal("Entity 1", results[0].Name);
        Assert.Equal("Child 1", results[0].ChildName);
        Assert.Null(results[2].ChildName);
    }

    [Fact]
    public void Test_IEnumerable_With_Anonymous_Type()
    {
        // Test that pre-built expressions are NOT used with IEnumerable
        var results = _testData
            .AsEnumerable()
            .SelectExpr(s => new
            {
                s.Id,
                s.Name,
            })
            .ToList();

        Assert.Equal(3, results.Count);
        Assert.Equal(1, results[0].Id);
        Assert.Equal("Entity 1", results[0].Name);
    }

    [Fact]
    public void Test_IQueryable_With_Named_Type()
    {
        // Test that pre-built expressions work with named types
        var results = _testData
            .AsQueryable()
            .SelectExpr(s => new SampleDto
            {
                Id = s.Id,
                Name = s.Name,
                Value = s.Value
            })
            .ToList();

        Assert.Equal(3, results.Count);
        Assert.Equal(1, results[0].Id);
        Assert.Equal("Entity 1", results[0].Name);
        Assert.Equal(100, results[0].Value);
    }

    [Fact]
    public void Test_IQueryable_With_Explicit_Dto()
    {
        // Test that pre-built expressions work with explicit DTO types
        var results = _testData
            .AsQueryable()
            .SelectExpr<SampleEntity, ExplicitDto>(s => new
            {
                s.Id,
                s.Name,
                ChildName = s.Child?.ChildName
            })
            .ToList();

        Assert.Equal(3, results.Count);
        Assert.Equal(1, results[0].Id);
        Assert.Equal("Entity 1", results[0].Name);
        Assert.Equal("Child 1", results[0].ChildName);
    }

    [Fact]
    public void Test_Multiple_Invocations_Use_Same_Cache()
    {
        // This test verifies that multiple invocations with the same structure
        // should use the same cached expression (this is implicit in the generated code)
        var results1 = _testData
            .AsQueryable()
            .SelectExpr(s => new { s.Id, s.Name })
            .ToList();

        var results2 = _testData
            .AsQueryable()
            .SelectExpr(s => new { s.Id, s.Name })
            .ToList();

        Assert.Equal(results1.Count, results2.Count);
        Assert.Equal(results1[0].Id, results2[0].Id);
    }
}

// Test entities
public class SampleEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int Value { get; set; }
    public ChildEntity? Child { get; set; }
}

public class ChildEntity
{
    public int ChildId { get; set; }
    public string ChildName { get; set; } = "";
}

// Test DTOs
public class SampleDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int Value { get; set; }
}
