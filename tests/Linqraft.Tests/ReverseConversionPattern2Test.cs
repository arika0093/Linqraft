using System.Collections.Generic;
using System.Linq;
using Linqraft;

namespace Linqraft.Tests;

public class ReverseConversionPattern2Test
{
    [Fact]
    public void Should_generate_static_reverse_mapping()
    {
        var source = new List<ReverseUser>
        {
            new() { Id = 1, Name = "Alice" },
            new() { Id = 2, Name = "Bob" },
        };

        var dtos = source
            .AsQueryable()
            .SelectExpr<ReverseUser, ReverseUserDto>(u => new { u.Id, u.Name })
            .ToList();

        var entities = ReverseUserReverseConverter.FromDtoProjection(dtos).ToList();

        entities.Count.ShouldBe(2);
        entities[0].Id.ShouldBe(1);
        entities[0].Name.ShouldBe("Alice");
        entities[1].Id.ShouldBe(2);
        entities[1].Name.ShouldBe("Bob");
    }

    [Fact]
    public void Should_generate_single_reverse_mapping()
    {
        var dto = new ReverseUserDto { Id = 5, Name = "Charlie" };

        var entity = ReverseUserReverseConverter.FromDto(dto);

        entity.Id.ShouldBe(5);
        entity.Name.ShouldBe("Charlie");
    }

    [Fact]
    public void Should_handle_nested_selectexpr_and_collections()
    {
        var parents = new List<NestedParent>
        {
            new()
            {
                Id = 1,
                Children =
                [
                    new()
                    {
                        Id = 10,
                        GrandChildren = [new() { Name = "g1" }, new() { Name = "g2" }],
                    },
                ],
            },
        };

        var dtos = parents
            .AsQueryable()
            .SelectExpr<NestedParent, NestedParentDto>(p => new
            {
                p.Id,
                Children = p.Children
                    .SelectExpr<NestedChild, NestedChildDto>(c => new
                    {
                        c.Id,
                        GrandChildren = c.GrandChildren.Select(g => new { g.Name }).ToList(),
                    })
                    .ToList(),
            })
            .ToList();

        var entities = NestedParentReverseConverter.FromDtoProjection(dtos).ToList();

        entities.Count.ShouldBe(1);
        entities[0].Id.ShouldBe(1);
        entities[0].Children.Count.ShouldBe(1);
        entities[0].Children[0].Id.ShouldBe(10);
        entities[0].Children[0].GrandChildren.Select(g => g.Name).ShouldBe(["g1", "g2"]);
    }

    [Fact]
    public void Should_map_nested_source_class()
    {
        var sources = new List<OuterContainer.InnerSource>
        {
            new() { Id = 3, Label = "inner" },
        };

        var dtos = sources
            .AsQueryable()
            .SelectExpr<OuterContainer.InnerSource, InnerSourceDto>(i => new
            {
                i.Id,
                i.Label,
            })
            .ToList();

        var entities = InnerSourceReverseConverter.FromDtoProjection(dtos).ToList();
        entities.Count.ShouldBe(1);
        entities[0].Id.ShouldBe(3);
        entities[0].Label.ShouldBe("inner");
    }

    [Fact]
    public void Should_handle_where_first_and_toarray_tolist_cases()
    {
        var sources = new List<ComplexSource>
        {
            new()
            {
                Filtered = new[] { 1, 2, 3 },
                Flattened = [1, 2, 3, 4],
                FirstValue = 9,
            },
        };

        var dtos = sources
            .AsQueryable()
            .SelectExpr<ComplexSource, ComplexSourceDto>(s => new
            {
                Filtered = s.Filtered.Where(v => v > 1).ToArray(),
                Flattened = s.Flattened.Where(v => v % 2 == 0).ToList(),
                FirstValue = s.Flattened.FirstOrDefault(),
            })
            .ToList();

        var entities = ComplexSourceReverseConverter.FromDtoProjection(dtos).ToList();
        entities.Count.ShouldBe(1);
        entities[0].Filtered.ShouldBe(new[] { 2, 3 });
        entities[0].Flattened.ShouldBe(new List<int> { 2, 4 });
        entities[0].FirstValue.ShouldBe(1); // from FirstOrDefault on original Flattened
    }

    [Fact]
    public void Should_support_predefined_dto()
    {
        var data = new List<PredefinedSource> { new() { Id = 7, Name = "pre" } };
        var dtos = data
            .AsQueryable()
            .SelectExpr(s => new PredefinedDto { Id = s.Id, Name = s.Name })
            .ToList();

        var entities = PredefinedDtoReverseConverter.FromDtoProjection(dtos).ToList();
        entities.Count.ShouldBe(1);
        entities[0].Id.ShouldBe(7);
        entities[0].Name.ShouldBe("pre");
    }
}

public class ReverseUser
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

[LinqraftReverseConvertion<ReverseUserDto>(IsStatic = true)]
public partial class ReverseUserReverseConverter;

public class NestedParent
{
    public int Id { get; set; }
    public List<NestedChild> Children { get; set; } = [];
}

public class NestedChild
{
    public int Id { get; set; }
    public List<NestedGrandChild> GrandChildren { get; set; } = [];
}

public class NestedGrandChild
{
    public string Name { get; set; } = "";
}

[LinqraftReverseConvertion<NestedParentDto>(IsStatic = true)]
public partial class NestedParentReverseConverter;

public static class OuterContainer
{
    public class InnerSource
    {
        public int Id { get; set; }
        public string Label { get; set; } = "";
    }
}

[LinqraftReverseConvertion<InnerSourceDto>(IsStatic = true)]
public partial class InnerSourceReverseConverter;

public class ComplexSource
{
    public int[] Filtered { get; set; } = [];
    public List<int> Flattened { get; set; } = [];
    public int FirstValue { get; set; }
}

[LinqraftReverseConvertion<ComplexSourceDto>(IsStatic = true)]
public partial class ComplexSourceReverseConverter;

public class PredefinedSource
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public class PredefinedDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

[LinqraftReverseConvertion<PredefinedDto>(IsStatic = true)]
public partial class PredefinedDtoReverseConverter;
