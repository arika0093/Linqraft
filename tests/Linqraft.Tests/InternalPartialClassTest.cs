using System.Collections.Generic;
using System.Linq;

namespace Linqraft.Tests;

/// <summary>
/// Tests for Issue #26 and #27: Internal partial class support
/// </summary>
public class InternalPartialClassTest
{
    // Issue #27: Internal partial class should generate DTOs with internal accessibility
    [Fact]
    public void InternalPartialClass_ShouldGenerateInternalDto()
    {
        var data = new List<SampleEntity>
        {
            new() { Id = 1, Name = "Test" },
        };

        var result = data.AsQueryable()
            .SelectExpr<SampleEntity, InternalTestDto>(x => new { x.Id, x.Name })
            .ToList();

        Assert.Single(result);
        Assert.Equal(1, result[0].Id);
        Assert.Equal("Test", result[0].Name);
    }

    // Issue #26: Nested DTOs within internal partial class should be properly referenced
    [Fact]
    public void InternalPartialClass_WithNestedSelect_ShouldReferenceNestedDtoProperly()
    {
        var data = new List<ParentEntity>
        {
            new()
            {
                Id = 1,
                Children =
                [
                    new() { Name = "Child1", Value = 10 },
                    new() { Name = "Child2", Value = 20 },
                ],
            },
        };

        var result = data.AsQueryable()
            .SelectExpr<ParentEntity, InternalParentDto>(x => new
            {
                x.Id,
                Children = x.Children.Select(c => new { c.Name, c.Value }).ToList(),
            })
            .ToList();

        Assert.Single(result);
        Assert.Equal(1, result[0].Id);
        Assert.Equal(2, result[0].Children.Count);
        Assert.Equal("Child1", result[0].Children[0].Name);
        Assert.Equal(10, result[0].Children[0].Value);
    }

    // Test entities
    public class SampleEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }

    public class ParentEntity
    {
        public int Id { get; set; }
        public List<ChildEntity> Children { get; set; } = [];
    }

    public class ChildEntity
    {
        public string Name { get; set; } = "";
        public int Value { get; set; }
    }
}

// Internal test DTOs for Issue #27
internal partial class InternalTestDto;

internal partial class InternalParentDto;
