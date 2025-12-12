using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Linqraft.Tests;

// Test classes inheriting from LinqraftMappingDeclare<T>
internal class BasicMappingDeclare : LinqraftMappingDeclare<MappingDeclareSourceClass>
{
    protected override void DefineMapping()
    {
        Source.SelectExpr<MappingDeclareSourceClass, MappingDeclareBasicDto>(x => new
        {
            x.Id,
            x.Name,
            ChildName = x.Child?.ChildName,
        });
    }
}

// Test with custom method name using class-level attribute
[LinqraftMappingGenerate("CustomProjection")]
internal class CustomMethodNameMappingDeclare : LinqraftMappingDeclare<MappingDeclareSourceClass>
{
    protected override void DefineMapping()
    {
        Source.SelectExpr<MappingDeclareSourceClass, MappingDeclareCustomDto>(x => new
        {
            x.Id,
            x.Description,
        });
    }
}

// Test with nested collections
public class NestedCollectionMappingDeclare : LinqraftMappingDeclare<MappingDeclareParentClass>
{
    protected override void DefineMapping()
    {
        Source.SelectExpr<MappingDeclareParentClass, MappingDeclareParentDto>(x => new
        {
            x.Id,
            x.Title,
            Children = x.Children.SelectExpr<MappingDeclareChildClass, MappingDeclareChildDto>(
                c => new { c.ChildId, c.ChildName }
            ),
        });
    }

    internal partial class MappingDeclareParentDto;

    internal partial class MappingDeclareChildDto;
}

public class LinqraftMappingDeclareTest
{
    [Fact]
    public void MappingDeclare_BasicTest()
    {
        // Arrange
        var data = new[]
        {
            new MappingDeclareSourceClass
            {
                Id = 1,
                Name = "Test1",
                Description = "Desc1",
                Child = new MappingDeclareChildClass { ChildId = 10, ChildName = "Child1" },
            },
            new MappingDeclareSourceClass
            {
                Id = 2,
                Name = "Test2",
                Description = "Desc2",
                Child = null,
            },
        }.AsQueryable();

        // Act - the generated extension method should be available
        var result = data.ProjectToMappingDeclareSourceClass().ToList();

        // Assert
        result.Count.ShouldBe(2);
        result[0].Id.ShouldBe(1);
        result[0].Name.ShouldBe("Test1");
        result[0].ChildName.ShouldBe("Child1");

        result[1].Id.ShouldBe(2);
        result[1].Name.ShouldBe("Test2");
        result[1].ChildName.ShouldBeNull();
    }

    [Fact]
    public void MappingDeclare_CustomMethodName_Test()
    {
        // Arrange
        var data = new[]
        {
            new MappingDeclareSourceClass
            {
                Id = 1,
                Name = "Test1",
                Description = "Description1",
                Child = null,
            },
            new MappingDeclareSourceClass
            {
                Id = 2,
                Name = "Test2",
                Description = "Description2",
                Child = null,
            },
        }.AsQueryable();

        // Act - the custom method name should be available
        var result = data.CustomProjection().ToList();

        // Assert
        result.Count.ShouldBe(2);
        result[0].Id.ShouldBe(1);
        result[0].Description.ShouldBe("Description1");

        result[1].Id.ShouldBe(2);
        result[1].Description.ShouldBe("Description2");
    }

    [Fact]
    public void MappingDeclare_NestedCollection_Test()
    {
        // Arrange
        var data = new[]
        {
            new MappingDeclareParentClass
            {
                Id = 1,
                Title = "Parent1",
                Children = new List<MappingDeclareChildClass>
                {
                    new() { ChildId = 10, ChildName = "Child1-1" },
                    new() { ChildId = 11, ChildName = "Child1-2" },
                },
            },
            new MappingDeclareParentClass
            {
                Id = 2,
                Title = "Parent2",
                Children = new List<MappingDeclareChildClass>(),
            },
        }.AsQueryable();

        // Act - the generated method should be available
        var result = data.ProjectToMappingDeclareParentClass().ToList();

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
}

// Test source classes
public class MappingDeclareSourceClass
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public MappingDeclareChildClass? Child { get; set; }
}

public class MappingDeclareChildClass
{
    public int ChildId { get; set; }
    public string ChildName { get; set; } = "";
}

public class MappingDeclareParentClass
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public List<MappingDeclareChildClass> Children { get; set; } = new();
}
