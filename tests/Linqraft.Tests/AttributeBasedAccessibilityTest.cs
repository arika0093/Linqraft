using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Linqraft.Tests;

/// <summary>
/// Tests for the attribute-based property accessibility control in explicit DTO pattern.
/// Demonstrates using LinqraftAccessibilityAttribute as an alternative to predefining properties.
/// </summary>
public class AttributeBasedAccessibilityTest
{
    [Fact]
    public void ExplicitDto_WithAccessibilityAttribute_ShouldRespectAttribute()
    {
        var data = new List<TestEntity>
        {
            new() { Id = 1, Name = "Test", Value = "InternalValue" },
        };

        var result = data.AsQueryable()
            .SelectExpr<TestEntity, AttributeMarkedDto>(x => new
            {
                x.Id,
                x.Name,
                x.Value,
            })
            .ToList();

        Assert.Single(result);
        Assert.Equal(1, result[0].Id);
        Assert.Equal("Test", result[0].Name);
        Assert.Equal("InternalValue", result[0].Value);
    }

    [Fact]
    public void ExplicitDto_WithMixedAttributeAndActual_ShouldPreferAttribute()
    {
        var data = new List<TestEntity>
        {
            new() { Id = 1, Name = "Test", Value = "Value" },
        };

        var result = data.AsQueryable()
            .SelectExpr<TestEntity, MixedAttributeDto>(x => new
            {
                x.Id,
                x.Name,
                x.Value,
            })
            .ToList();

        Assert.Single(result);
        Assert.Equal(1, result[0].Id);
        Assert.Equal("Test", result[0].Name);
        Assert.Equal("Value", result[0].Value);
    }

    [Fact]
    public void ExplicitDto_WithProtectedInternalAttribute_ShouldWork()
    {
        var data = new List<TestEntity>
        {
            new() { Id = 1, Name = "Test", Value = "Value" },
        };

        var result = data.AsQueryable()
            .SelectExpr<TestEntity, ProtectedInternalAttributeDto>(x => new
            {
                x.Id,
                x.Name,
            })
            .ToList();

        Assert.Single(result);
        Assert.Equal(1, result[0].Id);
        Assert.Equal("Test", result[0].Name);
    }

    // Test entities
    public class TestEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string Value { get; set; } = "";
    }
}

// DTO using LinqraftAccessibility attribute to mark internal property
public partial class AttributeMarkedDto
{
    [LinqraftAccessibility("internal")]
    public int Id { get; set; }
    
    [LinqraftAccessibility("internal")]
    public string Value { get; set; } = "";
}

// DTO mixing attribute-based and actual accessibility
public partial class MixedAttributeDto
{
    // Use attribute to override the actual public accessibility
    [LinqraftAccessibility("internal")]
    public int Id { get; set; }
    
    // This property is actually internal, no attribute needed
    internal string Value { get; set; } = "";
}

// DTO with protected internal via attribute
public partial class ProtectedInternalAttributeDto
{
    [LinqraftAccessibility("protected internal")]
    public int Id { get; set; }
}
