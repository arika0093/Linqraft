using System.Collections.Generic;
using System.Linq;

namespace Linqraft.Tests;

public class IEnumerableCaseTest
{
    private static List<Simple1> EnumerableData => new()
    {
        new Simple1 { Id = 1, FirstName = "John", LastName = "Doe" },
        new Simple1 { Id = 2, FirstName = "Jane", LastName = "Smith" },
    };

    [Fact]
    public void BasicEnumerableCase()
    {
        var converted = EnumerableData
            .SelectExpr<Simple1, EnumerableAutoGenDto>(s => new
            {
                s.Id,
                FullName = s.FirstName + " " + s.LastName,
            })
            .ToList();
        converted.Count.ShouldBe(2);
        var first = converted[0];
        first.GetType().Name.ShouldBe("EnumerableAutoGenDto");
        first.Id.ShouldBe(1);
        first.FullName.ShouldBe("John Doe");
    }

    [Fact]
    public void EnumerableWithWhereClause()
    {
        var converted = EnumerableData
            .Where(s => s.Id > 1)
            .SelectExpr<Simple1, EnumerableAutoGenDto>(s => new
            {
                s.Id,
                FullName = s.FirstName + " " + s.LastName,
            })
            .ToList();
        converted.Count.ShouldBe(1);
        var first = converted[0];
        first.GetType().Name.ShouldBe("EnumerableAutoGenDto");
        first.Id.ShouldBe(2);
        first.FullName.ShouldBe("Jane Smith");
    }

    [Fact]
    public void EnumerableWithExplicitDto()
    {
        var converted = EnumerableData
            .SelectExpr(s => new EnumerableSimpleDto
            {
                Id = s.Id,
                FullName = s.FirstName + " " + s.LastName,
            })
            .ToList();
        converted.Count.ShouldBe(2);
        var first = converted[0];
        first.GetType().ShouldBe(typeof(EnumerableSimpleDto));
        first.Id.ShouldBe(1);
        first.FullName.ShouldBe("John Doe");
    }

    [Fact]
    public void EnumerableWithAnonymous()
    {
        var converted = EnumerableData
            .OrderByDescending(s => s.Id)
            .SelectExpr(s => new
            {
                s.Id,
                FullName = s.FirstName + " " + s.LastName,
            })
            .ToList();
        converted.Count.ShouldBe(2);
        var first = converted[0];
        first.Id.ShouldBe(2);
        first.FullName.ShouldBe("Jane Smith");
    }
}


public class EnumerableSimpleDto
{
    public int Id { get; set; }
    public string FullName { get; set; }
}
