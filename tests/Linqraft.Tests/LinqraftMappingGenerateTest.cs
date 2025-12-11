using System.Linq;
using Xunit;

namespace Linqraft.Tests;

// Test source classes  
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
}
