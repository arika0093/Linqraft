using System.Collections.Generic;
using System.Linq;

namespace Linqraft.Tests.TestNamespace;

public class CrossNamespaceTest
{
    [Fact]
    public void GeneratedDtoShouldBeInCallerNamespace()
    {
        var rst = SampleData
            .AsQueryable()
            .SelectExpr<Linqraft.Tests.SourceNamespace.TestClass, TestSampleDto>(s => new
            {
                Bar = s.Id,
            })
            .First();

        rst.Bar.ShouldBe(1);
        // Verify that the generated DTO type is in the correct namespace
        rst.GetType().FullName.ShouldBe("Linqraft.Tests.TestNamespace.TestSampleDto");
    }

    [Fact]
    public void GeneratedDtoWithNestedPropertiesShouldBeInCallerNamespace()
    {
        var rst = SampleDataWithChild
            .AsQueryable()
            .SelectExpr<Linqraft.Tests.SourceNamespace.ParentClass, ParentDto>(s => new
            {
                ParentId = s.Id,
                ChildName = s.Child.Name,
            })
            .First();

        rst.ParentId.ShouldBe(1);
        rst.ChildName.ShouldBe("TestChild");
        rst.GetType().FullName.ShouldBe("Linqraft.Tests.TestNamespace.ParentDto");
    }

    [Fact]
    public void ManuallyDefinedDtoShouldUseDefinedNamespace()
    {
        var rst = SampleData
            .AsQueryable()
            .SelectExpr(s => new PredefinedDto { Name = s.Name, Value = s.Id })
            .First();

        rst.Name.ShouldBe("Test");
        rst.Value.ShouldBe(1);
        // This should use the predefined DTO's namespace
        rst.GetType().FullName.ShouldBe("Linqraft.Tests.TestNamespace.PredefinedDto");
    }

    private readonly List<Linqraft.Tests.SourceNamespace.TestClass> SampleData =
    [
        new() { Id = 1, Name = "Test" },
    ];

    private readonly List<Linqraft.Tests.SourceNamespace.ParentClass> SampleDataWithChild =
    [
        new()
        {
            Id = 1,
            Child = new Linqraft.Tests.SourceNamespace.ChildClass { Name = "TestChild" },
        },
    ];
}

public partial class TestSampleDto;

public partial class ParentDto;

public class PredefinedDto
{
    public string Name { get; set; } = "";
    public int Value { get; set; }
}
