using System.Collections.Generic;
using System.Linq;

namespace Linqraft.Tests;

internal static partial class MappingMethodQueries
{
    [LinqraftMapping]
    internal static IQueryable<MappingDeclareBasicDto> ProjectToMappingDeclareBasicDto(
        this LinqraftMapper<MappingDeclareSourceClass> source
    ) =>
        source.Select<MappingDeclareBasicDto>(x => new
        {
            x.Id,
            x.Name,
            ChildName = x.Child?.ChildName,
        });

    [LinqraftMapping]
    internal static IQueryable<MappingDeclareCustomDto> CustomProjection(
        this LinqraftMapper<MappingDeclareSourceClass> source
    ) => source.Select<MappingDeclareCustomDto>(x => new { x.Id, x.Description });

    [LinqraftMapping]
    internal static IQueryable<MappingDeclareCaptureDto> ProjectToMappingDeclareWithCapture(
        this LinqraftMapper<MappingDeclareSourceClass> source,
        int offset,
        string suffix
    ) =>
        source.Select<MappingDeclareCaptureDto>(x => new
        {
            x.Id,
            AdjustedValue = x.Value + offset,
            Description = x.Name + suffix,
        });

#if NET9_0_OR_GREATER
    [LinqraftMapping]
    internal static IQueryable<MappingDeclareParentDto> ProjectToMappingDeclareParentDto(
        this LinqraftMapper<MappingDeclareParentClass> source
    ) =>
        source.Select<MappingDeclareParentDto>(x => new
        {
            x.Id,
            x.Title,
            Children = x
                .Children.UseLinqraft()
                .Select<MappingDeclareChildDto>(c => new { c.ChildId, c.ChildName }),
        });
#endif
}

public class LinqraftMappingDeclareTest
{
    [Test]
    public void MappingDeclare_BasicTest()
    {
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
        }.AsTestQueryable();

        var result = data.ProjectToMappingDeclareBasicDto().ToList();

        result.Count.ShouldBe(2);
        result[0].Id.ShouldBe(1);
        result[0].Name.ShouldBe("Test1");
        result[0].ChildName.ShouldBe("Child1");

        result[1].Id.ShouldBe(2);
        result[1].Name.ShouldBe("Test2");
        result[1].ChildName.ShouldBeNull();
    }

    [Test]
    public void MappingDeclare_CustomMethodName_Test()
    {
        var data = new[]
        {
            new MappingDeclareSourceClass
            {
                Id = 1,
                Name = "Test1",
                Description = "Description1",
            },
            new MappingDeclareSourceClass
            {
                Id = 2,
                Name = "Test2",
                Description = "Description2",
            },
        }.AsTestQueryable();

        var result = data.CustomProjection().ToList();

        result.Count.ShouldBe(2);
        result[0].Id.ShouldBe(1);
        result[0].Description.ShouldBe("Description1");
        result[1].Id.ShouldBe(2);
        result[1].Description.ShouldBe("Description2");
    }

    [Test]
    public void MappingDeclare_WithCaptureParameters_Test()
    {
        var data = new[]
        {
            new MappingDeclareSourceClass
            {
                Id = 1,
                Value = 10,
                Name = "Test1",
            },
            new MappingDeclareSourceClass
            {
                Id = 2,
                Value = 20,
                Name = "Test2",
            },
        }.AsTestQueryable();

        var result = data.ProjectToMappingDeclareWithCapture(100, " units").ToList();

        result.Count.ShouldBe(2);
        result[0].AdjustedValue.ShouldBe(110);
        result[0].Description.ShouldBe("Test1 units");
        result[1].AdjustedValue.ShouldBe(120);
        result[1].Description.ShouldBe("Test2 units");
    }

    [Test]
    public void MappingDeclare_Generates_IEnumerable_Overload()
    {
        IEnumerable<MappingDeclareSourceClass> data =
        [
            new()
            {
                Id = 1,
                Name = "Test1",
                Description = "Description1",
            },
            new()
            {
                Id = 2,
                Name = "Test2",
                Description = "Description2",
            },
        ];

        var result = data.CustomProjection().ToList();

        result.Count.ShouldBe(2);
        result[0].Description.ShouldBe("Description1");
        result[1].Description.ShouldBe("Description2");
    }

#if NET9_0_OR_GREATER
    [Test]
    public void MappingDeclare_NestedCollection_Test()
    {
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
        }.AsTestQueryable();

        var result = data.ProjectToMappingDeclareParentDto().ToList();

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

public class MappingDeclareSourceClass
{
    public int Id { get; set; }
    public int Value { get; set; }
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

public partial class MappingDeclareBasicDto;

public partial class MappingDeclareCustomDto;

public partial class MappingDeclareCaptureDto;

#if NET9_0_OR_GREATER
internal partial class MappingDeclareParentDto;

internal partial class MappingDeclareChildDto;
#endif
