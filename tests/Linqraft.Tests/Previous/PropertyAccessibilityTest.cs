using System.Collections.Generic;
using System.Linq;

namespace Linqraft.Tests;

/// <summary>
/// Tests for property-level accessibility control in explicit DTO pattern.
/// Validates that predefined partial class properties with specific accessibility modifiers
/// are respected by the source generator.
///
/// Feature: Users can control property accessibility by predefining properties in a partial class.
/// The generator will:
/// 1. Detect existing properties from the TResult type parameter
/// 2. Extract their accessibility modifiers (public, internal, protected internal, etc.)
/// 3. Skip generating those properties (avoiding CS0102 duplicate definition errors)
/// 4. Only generate properties that are not predefined
/// 5. Respect the required keyword visibility constraints (CS9032)
///
/// Example:
/// public partial class SampleDto
/// {
///     internal string InternalProperty { get; set; }
/// }
///
/// var result = query.SelectExpr&lt;Entity, SampleDto&gt;(x => new
/// {
///     InternalProperty = x.InternalProperty,  // Uses predefined internal property
///     PublicProperty = x.PublicProperty,      // Generated as public
/// });
///
/// Generated code:
/// public partial class SampleDto
/// {
///     // InternalProperty is NOT generated (already defined by user)
///     public required string PublicProperty { get; set; }
/// }
/// </summary>
public class PropertyAccessibilityTest
{
    [Fact]
    public void ExplicitDto_WithInternalProperty_ShouldRespectAccessibility()
    {
        var data = new List<TestEntity>
        {
            new()
            {
                Id = 1,
                PublicName = "Public",
                InternalValue = "Internal",
            },
        };

        var result = data.AsQueryable()
            .SelectExpr<TestEntity, MixedAccessibilityDto>(x => new
            {
                x.Id,
                x.PublicName,
                x.InternalValue,
            })
            .ToList();

        Assert.Single(result);
        Assert.Equal(1, result[0].Id);
        Assert.Equal("Public", result[0].PublicName);
        // InternalValue should be accessible since we're in the same assembly
        Assert.Equal("Internal", result[0].InternalValue);
    }

    [Fact]
    public void ExplicitDto_WithMultipleAccessibilityLevels_ShouldRespectAll()
    {
        var data = new List<ComplexEntity>
        {
            new()
            {
                PublicField = "public",
                InternalField = "internal",
                ProtectedInternalField = "protected internal",
            },
        };

        var result = data.AsQueryable()
            .SelectExpr<ComplexEntity, MultiAccessibilityDto>(x => new
            {
                x.PublicField,
                x.InternalField,
                x.ProtectedInternalField,
            })
            .ToList();

        Assert.Single(result);
        Assert.Equal("public", result[0].PublicField);
        Assert.Equal("internal", result[0].InternalField);
        Assert.Equal("protected internal", result[0].ProtectedInternalField);
    }

    [Fact]
    public void ExplicitDto_OnlyInternalProperties_ShouldWork()
    {
        var data = new List<TestEntity>
        {
            new()
            {
                Id = 1,
                PublicName = "Name1",
                InternalValue = "Value1",
            },
        };

        var result = data.AsQueryable()
            .SelectExpr<TestEntity, AllInternalPropertiesDto>(x => new { x.Id, x.InternalValue })
            .ToList();

        Assert.Single(result);
        Assert.Equal(1, result[0].Id);
        Assert.Equal("Value1", result[0].InternalValue);
    }

    [Fact]
    public void ExplicitDto_WithNestedSelect_ShouldRespectAccessibility()
    {
        var data = new List<ParentEntity>
        {
            new()
            {
                Id = 1,
                Children =
                [
                    new() { Name = "Child1", InternalValue = 10 },
                    new() { Name = "Child2", InternalValue = 20 },
                ],
            },
        };

        var result = data.AsQueryable()
            .SelectExpr<ParentEntity, ParentWithInternalChildrenDto>(x => new
            {
                x.Id,
                Children = x.Children.Select(c => new { c.Name, c.InternalValue }).ToList(),
            })
            .ToList();

        Assert.Single(result);
        Assert.Equal(1, result[0].Id);
        Assert.Equal(2, result[0].Children.Count);
        Assert.Equal("Child1", result[0].Children[0].Name);
        Assert.Equal(10, result[0].Children[0].InternalValue);
    }

    [Fact]
    public void ExplicitDto_PartiallyPredefinedProperties_ShouldUseDefaultPublicForOthers()
    {
        var data = new List<TestEntity>
        {
            new()
            {
                Id = 1,
                PublicName = "Test",
                InternalValue = "Internal",
            },
        };

        var result = data.AsQueryable()
            .SelectExpr<TestEntity, PartiallyPredefinedDto>(x => new
            {
                x.Id,
                x.PublicName,
                x.InternalValue,
            })
            .ToList();

        Assert.Single(result);
        Assert.Equal(1, result[0].Id);
        // PublicName is not predefined, should be public by default
        Assert.Equal("Test", result[0].PublicName);
        // InternalValue is predefined as internal
        Assert.Equal("Internal", result[0].InternalValue);
    }

    // Test entities
    public class TestEntity
    {
        public int Id { get; set; }
        public string PublicName { get; set; } = "";
        public string InternalValue { get; set; } = "";
    }

    public class ComplexEntity
    {
        public string PublicField { get; set; } = "";
        public string InternalField { get; set; } = "";
        public string ProtectedInternalField { get; set; } = "";
    }

    public class ParentEntity
    {
        public int Id { get; set; }
        public List<ChildEntity> Children { get; set; } = [];
    }

    public class ChildEntity
    {
        public string Name { get; set; } = "";
        public int InternalValue { get; set; }
    }
}

// DTO with mixed accessibility - some properties are internal
public partial class MixedAccessibilityDto
{
    internal string InternalValue { get; set; } = "";
}

// DTO with multiple accessibility levels
public partial class MultiAccessibilityDto
{
    internal string InternalField { get; set; } = "";
    protected internal string ProtectedInternalField { get; set; } = "";
}

// DTO with all internal properties
public partial class AllInternalPropertiesDto
{
    internal int Id { get; set; }
    internal string InternalValue { get; set; } = "";
}

// DTO with nested select and internal properties
public partial class ParentWithInternalChildrenDto;

// DTO with only some properties predefined
public partial class PartiallyPredefinedDto
{
    internal string InternalValue { get; set; } = "";
}
