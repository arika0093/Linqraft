using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;

namespace Linqraft.Tests;

/// <summary>
/// Tests for the comment generation feature in generated DTOs.
/// Verifies that XML documentation comments are generated based on source properties.
/// </summary>
public class CommentGenerationTest
{
    [Fact]
    public void Test_SimplePropertyWithXmlComment_GeneratesComment()
    {
        List<TestDataWithComments> datas =
        [
            new()
            {
                Id = 1,
                Name = "Test",
            },
        ];

        var result = datas
            .AsQueryable()
            .SelectExpr<TestDataWithComments, TestDataWithCommentsDto>(d => new
            {
                d.Id,
                d.Name,
            })
            .ToList();

        result.Count.ShouldBe(1);
        result[0].Id.ShouldBe(1);
        result[0].Name.ShouldBe("Test");
    }

    [Fact]
    public void Test_PropertyWithCommentAttribute_GeneratesComment()
    {
        List<TestDataWithCommentAttribute> datas =
        [
            new()
            {
                Id = 1,
                Description = "Test description",
            },
        ];

        var result = datas
            .AsQueryable()
            .SelectExpr<TestDataWithCommentAttribute, TestDataWithCommentAttributeDto>(d => new
            {
                d.Id,
                d.Description,
            })
            .ToList();

        result.Count.ShouldBe(1);
        result[0].Id.ShouldBe(1);
        result[0].Description.ShouldBe("Test description");
    }

    [Fact]
    public void Test_PropertyWithDataAnnotations_GeneratesAttributeInfo()
    {
        List<TestDataWithDataAnnotations> datas =
        [
            new()
            {
                Id = 1,
                Name = "Test",
                Email = "test@example.com",
            },
        ];

        var result = datas
            .AsQueryable()
            .SelectExpr<TestDataWithDataAnnotations, TestDataWithDataAnnotationsDto>(d => new
            {
                d.Id,
                d.Name,
                d.Email,
            })
            .ToList();

        result.Count.ShouldBe(1);
        result[0].Id.ShouldBe(1);
        result[0].Name.ShouldBe("Test");
        result[0].Email.ShouldBe("test@example.com");
    }

    [Fact]
    public void Test_ComplexExpression_GeneratesSourceReference()
    {
        List<TestDataWithCollections> datas =
        [
            new()
            {
                Id = 1,
                Items = ["a", "b", "c"],
            },
        ];

        var result = datas
            .AsQueryable()
            .SelectExpr<TestDataWithCollections, TestDataWithCollectionsDto>(d => new
            {
                d.Id,
                ItemCount = d.Items.Count,
                ItemsSummary = d.Items.FirstOrDefault(),
            })
            .ToList();

        result.Count.ShouldBe(1);
        result[0].Id.ShouldBe(1);
        result[0].ItemCount.ShouldBe(3);
        result[0].ItemsSummary.ShouldBe("a");
    }

    [Fact]
    public void Test_NavigationProperty_GeneratesSourceReference()
    {
        List<TestDataWithNavigation> datas =
        [
            new()
            {
                Id = 1,
                Child = new TestChildData { ChildId = 10, ChildName = "Child1" },
            },
        ];

        var result = datas
            .AsQueryable()
            .SelectExpr<TestDataWithNavigation, TestDataWithNavigationDto>(d => new
            {
                d.Id,
                ChildId = d.Child!.ChildId,
                ChildName = d.Child!.ChildName,
            })
            .ToList();

        result.Count.ShouldBe(1);
        result[0].Id.ShouldBe(1);
        result[0].ChildId.ShouldBe(10);
        result[0].ChildName.ShouldBe("Child1");
    }
}

// Custom Comment attribute for testing (same as EF Core's Comment attribute)
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Property | AttributeTargets.Field)]
internal class CommentAttribute : Attribute
{
    public CommentAttribute(string comment)
    {
        Comment = comment;
    }

    public string Comment { get; }
}

// Test data classes with XML documentation comments

/// <summary>
/// Test data class with XML documentation
/// </summary>
internal class TestDataWithComments
{
    /// <summary>
    /// The unique identifier
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// The name of the entity
    /// </summary>
    public required string Name { get; set; }
}

// Test data classes with Comment attribute

[Comment("This is a class with Comment attribute")]
internal class TestDataWithCommentAttribute
{
    public int Id { get; set; }

    [Comment("This is a description field")]
    public required string Description { get; set; }
}

// Test data classes with Data Annotations

internal class TestDataWithDataAnnotations
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Required]
    [StringLength(100)]
    public required string Name { get; set; }

    [EmailAddress]
    public required string Email { get; set; }
}

// Test data classes with collections

internal class TestDataWithCollections
{
    public int Id { get; set; }
    public required List<string> Items { get; set; }
}

// Test data classes with navigation properties

internal class TestDataWithNavigation
{
    public int Id { get; set; }

    /// <summary>
    /// Navigation property to child data
    /// </summary>
    public TestChildData? Child { get; set; }
}

internal class TestChildData
{
    public int ChildId { get; set; }
    public required string ChildName { get; set; }
}

// Partial DTO classes for test

internal partial class TestDataWithCommentsDto { }
internal partial class TestDataWithCommentAttributeDto { }
internal partial class TestDataWithDataAnnotationsDto { }
internal partial class TestDataWithCollectionsDto { }
internal partial class TestDataWithNavigationDto { }
