using System.Collections.Generic;
using System.Linq;

namespace Linqraft.Tests;

// Static partial class with mapping methods
public static partial class MappingTestQueries
{
    [LinqraftMapping]
    internal static IQueryable<MappingTestSampleDto> ProjectToDto(
        this LinqraftMapper<MappingTestSampleClass> source
    ) =>
        source.Select<MappingTestSampleDto>(x => new
        {
            x.Id,
            x.Name,
            x.Description,
            ChildId = x.Child?.ChildId,
            ChildName = x.Child?.ChildName,
        });

    [LinqraftMapping]
    internal static IQueryable<MappingTestCapturedDto> ProjectToDtoWithCapture(
        this LinqraftMapper<MappingTestSampleClass> source,
        int offset,
        string suffix
    ) =>
        source.Select<MappingTestCapturedDto>(x => new
        {
            x.Id,
            AdjustedValue = x.Value + offset,
            Description = x.Name + suffix,
        });

#if NET9_0_OR_GREATER
    [LinqraftMapping]
    internal static IQueryable<MappingTestParentDto> ProjectToDtoWithChildren(
        this LinqraftMapper<MappingTestParentClass> source
    ) =>
        source.Select<MappingTestParentDto>(x => new
        {
            x.Id,
            x.Title,
            Children = x
                .Children.UseLinqraft()
                .Select<MappingTestChildDto>(c => new { c.ChildId, c.ChildName }),
        });
#endif
}

public class LinqraftMappingGenerateTest
{
    [Test]
    public void MappingGenerate_BasicTest()
    {
        // Arrange
        var data = new[]
        {
            new MappingTestSampleClass
            {
                Id = 1,
                Value = 10,
                Name = "Test1",
                Description = "Description1",
                Child = new MappingTestChildClass { ChildId = 10, ChildName = "Child1" },
            },
            new MappingTestSampleClass
            {
                Id = 2,
                Value = 20,
                Name = "Test2",
                Description = null,
                Child = null,
            },
        }.AsTestQueryable();

        // Act
        var result = MappingTestQueries.ProjectToDto(data).ToList();

        // Assert
        result.Count.ShouldBe(2);
        result[0].Id.ShouldBe(1);
        result[0].Name.ShouldBe("Test1");
        result[0].Description.ShouldBe("Description1");
        result[0].ChildId.ShouldBe(10);
        result[0].ChildName.ShouldBe("Child1");

        result[1].Id.ShouldBe(2);
        result[1].Name.ShouldBe("Test2");
        result[1].Description.ShouldBeNull();
        result[1].ChildId.ShouldBeNull();
        result[1].ChildName.ShouldBeNull();
    }

    [Test]
    public void MappingGenerate_WithCaptureParameters_Test()
    {
        var data = new[]
        {
            new MappingTestSampleClass
            {
                Id = 1,
                Value = 10,
                Name = "Test1",
            },
            new MappingTestSampleClass
            {
                Id = 2,
                Value = 20,
                Name = "Test2",
            },
        }.AsTestQueryable();

        var result = MappingTestQueries
            .ProjectToDtoWithCapture(data, offset: 100, suffix: " units")
            .ToList();

        result.Count.ShouldBe(2);
        result[0].AdjustedValue.ShouldBe(110);
        result[0].Description.ShouldBe("Test1 units");
        result[1].AdjustedValue.ShouldBe(120);
        result[1].Description.ShouldBe("Test2 units");
    }

#if NET9_0_OR_GREATER
    [Test]
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
                },
            },
            new MappingTestParentClass
            {
                Id = 2,
                Title = "Parent2",
                Children = new List<MappingTestChildClass>(),
            },
        }.AsTestQueryable();

        // Act
        var result = MappingTestQueries.ProjectToDtoWithChildren(data).ToList();

        // Assert
        result.Count.ShouldBe(2);
        result[0].Id.ShouldBe(1);
        result[0].Title.ShouldBe("Parent1");

        var children0 = result[0].Children.ToList();
        children0.Count.ShouldBe(2);
        children0[0].ChildId.ShouldBe(10);
        children0[0].ChildName.ShouldBe("Child1-1");

        result[1].Id.ShouldBe(2);
        result[1].Title.ShouldBe("Parent2");
        result[1].Children.ShouldBeEmpty();
    }
#endif
}

// Test source classes - consolidated at the end of the file
public class MappingTestSampleClass
{
    public int Id { get; set; }
    public int Value { get; set; }
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

public partial class MappingTestSampleDto;

public partial class MappingTestChildDto;

public partial class MappingTestParentDto;

public partial class MappingTestCapturedDto;
