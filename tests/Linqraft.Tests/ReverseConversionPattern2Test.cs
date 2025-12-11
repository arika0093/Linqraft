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
}

public class ReverseUser
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

[LinqraftReverseConvertion<ReverseUserDto>(IsStatic = true)]
public partial class ReverseUserReverseConverter;
