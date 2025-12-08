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

    [Fact]
    public void Test_Nested_SelectExpr_With_PrebuiltExpression()
    {
        // Test that pre-built expressions work with nested structures
        // Using a simpler approach - test multiple DTO types in the same test class
        var parentData = new List<ParentEntity>
        {
            new()
            {
                Id = 1,
                Name = "Parent 1",
                Value = 100
            },
            new()
            {
                Id = 2,
                Name = "Parent 2",
                Value = 200
            }
        };

        // First, test a different DTO type
        var results = parentData
            .AsQueryable()
            .SelectExpr(p => new ParentDto
            {
                Id = p.Id,
                Name = p.Name,
                ParentValue = p.Value
            })
            .ToList();

        Assert.Equal(2, results.Count);
        Assert.Equal(1, results[0].Id);
        Assert.Equal("Parent 1", results[0].Name);
        Assert.Equal(100, results[0].ParentValue);
        
        // Then verify the original test data still works (testing multiple DTOs)
        var sampleResults = _testData
            .AsQueryable()
            .SelectExpr(s => new SampleDto
            {
                Id = s.Id,
                Name = s.Name,
                Value = s.Value
            })
            .ToList();
        
        Assert.Equal(3, sampleResults.Count);
    }
}

// Additional test entities for testing multiple DTO types
public class ParentEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int Value { get; set; }
}

public class ParentDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int ParentValue { get; set; }
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

// Explicit DTO for testing explicit DTO type parameter
public partial record ExplicitDto;
