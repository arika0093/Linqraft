using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Linqraft.Tests;

// Static partial class with mapping methods
public static partial class MappingTestQueries
{
    [LinqraftMappingGenerate("ProjectToDto")]
    internal static IQueryable<MappingTestSampleDto> DummyQuery(this IQueryable<MappingTestSampleClass> source) => source
        .SelectExpr<MappingTestSampleClass, MappingTestSampleDto>(x => new
        {
            x.Id,
            x.Name,
            x.Description,
            ChildId = x.Child?.ChildId,
            ChildName = x.Child?.ChildName,
        });

    [LinqraftMappingGenerate("ProjectToDtoWithChildren")]
    internal static IQueryable<MappingTestParentDto> DummyWithChildren(this IQueryable<MappingTestParentClass> source) => source
        .SelectExpr<MappingTestParentClass, MappingTestParentDto>(x => new
        {
            x.Id,
            x.Title,
            Children = x.Children.SelectExpr<MappingTestChildClass, MappingTestChildClassDto>(c => new
            {
                c.ChildId,
                c.ChildName,
            }),
        });
}

public class LinqraftMappingGenerateTest
{

    [Fact]
    public void MappingGenerate_BasicTest()
    {
        // Arrange
        var data = new[]
        {
            new MappingTestSampleClass
            {
                Id = 1,
                Name = "Test1",
                Description = "Description1",
                Child = new MappingTestChildClass { ChildId = 10, ChildName = "Child1" }
            },
            new MappingTestSampleClass
            {
                Id = 2,
                Name = "Test2",
                Description = null,
                Child = null
            }
        }.AsQueryable();

        // Act
        var result = MappingTestQueries.ProjectToDto(data).ToList();

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Equal(1, result[0].Id);
        Assert.Equal("Test1", result[0].Name);
        Assert.Equal("Description1", result[0].Description);
        Assert.Equal(10, result[0].ChildId);
        Assert.Equal("Child1", result[0].ChildName);

        Assert.Equal(2, result[1].Id);
        Assert.Equal("Test2", result[1].Name);
        Assert.Null(result[1].Description);
        Assert.Null(result[1].ChildId);
        Assert.Null(result[1].ChildName);
    }

    [Fact]
    public void MappingGenerate_WithNestedCollection_Test()
    {
        // Arrange
        var data = new[]
        {
            new MappingTestParentClass
            {
                Id = 1,
                Title = "Parent1",
                Children = new List<MappingTestChildClass>
                {
                    new() { ChildId = 10, ChildName = "Child1-1" },
                    new() { ChildId = 11, ChildName = "Child1-2" },
                }
            },
            new MappingTestParentClass
            {
                Id = 2,
                Title = "Parent2",
                Children = new List<MappingTestChildClass>()
            }
        }.AsQueryable();

        // Act
        var result = MappingTestQueries.ProjectToDtoWithChildren(data).ToList();

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Equal(1, result[0].Id);
        Assert.Equal("Parent1", result[0].Title);
        
        var children0 = result[0].Children.ToList();
        Assert.Equal(2, children0.Count);
        Assert.Equal(10, children0[0].ChildId);
        Assert.Equal("Child1-1", children0[0].ChildName);

        Assert.Equal(2, result[1].Id);
        Assert.Equal("Parent2", result[1].Title);
        Assert.Empty(result[1].Children);
    }
}

// Test source classes - consolidated at the end of the file
public class MappingTestSampleClass
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public MappingTestChildClass? Child { get; set; }
}

public class MappingTestChildClass
{
    public int ChildId { get; set; }
    public string ChildName { get; set; } = "";
}

public class MappingTestParentClass
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public List<MappingTestChildClass> Children { get; set; } = new();
}
