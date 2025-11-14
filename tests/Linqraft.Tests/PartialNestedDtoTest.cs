using System.Collections.Generic;
using System.Linq;

namespace Linqraft.Tests;

public partial class PartialNestedDtoTest
{
    [Fact]
    public void PartialNestedDto_ShouldGenerateInSameNestingLevel()
    {
        var testData = new List<Entity>
        {
            new Entity
            {
                Id = 1,
                Name = "Entity1",
                CreatedAt = new System.DateTime(2025, 1, 1),
            },
            new Entity
            {
                Id = 2,
                Name = "Entity2",
                CreatedAt = new System.DateTime(2025, 1, 2),
            },
        };

        var converted = testData
            .AsQueryable()
            .SelectExpr<Entity, SampleService.EntityDto>(e => new
            {
                e.Id,
                e.Name,
                e.CreatedAt,
            })
            .ToList();

        converted.Count.ShouldBe(2);
        var first = converted[0];
        first.Id.ShouldBe(1);
        first.Name.ShouldBe("Entity1");
        first.CreatedAt.ShouldBe(new System.DateTime(2025, 1, 1));

        var second = converted[1];
        second.Id.ShouldBe(2);
        second.Name.ShouldBe("Entity2");
        second.CreatedAt.ShouldBe(new System.DateTime(2025, 1, 2));
    }

    [Fact]
    public void PartialNestedDto_MultiLevel_ShouldGenerateInSameNestingLevel()
    {
        var testData = new List<Entity>
        {
            new Entity
            {
                Id = 1,
                Name = "Entity1",
                CreatedAt = new System.DateTime(2025, 1, 1),
            },
        };

        var converted = testData
            .AsQueryable()
            .SelectExpr<Entity, OuterClass.InnerClass.DeepDto>(e => new { e.Id, e.Name })
            .ToList();

        converted.Count.ShouldBe(1);
        var first = converted[0];
        first.Id.ShouldBe(1);
        first.Name.ShouldBe("Entity1");
    }

    [Fact]
    public void PartialNestedDto_ShouldGenerateInSameClass()
    {
        var testData = new List<Entity>
        {
            new Entity
            {
                Id = 1,
                Name = "Entity1",
                CreatedAt = new System.DateTime(2025, 1, 1),
            },
            new Entity
            {
                Id = 2,
                Name = "Entity2",
                CreatedAt = new System.DateTime(2025, 1, 2),
            },
        };

        var converted = testData
            .AsQueryable()
            .SelectExpr<Entity, EntityDtoInClass>(e => new
            {
                e.Id,
                e.Name,
                e.CreatedAt,
            })
            .ToList();

        converted.Count.ShouldBe(2);
        var first = converted[0];
        first.Id.ShouldBe(1);
        first.Name.ShouldBe("Entity1");
        first.CreatedAt.ShouldBe(new System.DateTime(2025, 1, 1));

        var second = converted[1];
        second.Id.ShouldBe(2);
        second.Name.ShouldBe("Entity2");
        second.CreatedAt.ShouldBe(new System.DateTime(2025, 1, 2));
    }

    public partial class EntityDtoInClass;
}

internal class Entity
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public System.DateTime CreatedAt { get; set; }
}

public partial class SampleService
{
    public partial class EntityDto;
}

public partial class OuterClass
{
    public partial class InnerClass
    {
        public partial class DeepDto;
    }
}
