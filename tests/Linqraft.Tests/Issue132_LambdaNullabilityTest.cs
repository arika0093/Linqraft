using System;
using System.Linq;

namespace Linqraft.Tests;

public class Issue132_LambdaNullabilityTest
{
    /// <summary>
    /// Test for issue #132: Nullability not correctly generated from usage in minimal API lambdas
    /// When SelectExpr is called inside a lambda expression (like in minimal API), 
    /// the following phenomena occur:
    /// - A type that should be string? is converted to string
    /// - A type that should be List<ChildDto> (s.Select(...)) is converted to List<ChildDto>?
    /// </summary>
    [Fact]
    public void NullableTypeInLambda_ShouldPreserveNullability()
    {
        // Simulate minimal API lambda pattern
        Func<object> handler = () =>
        {
            Person[] people = [];
            var result = people.AsQueryable().SelectExpr<Person, PersonDto>(s => new
            {
                Id = s.Id,
                Name = s.Name,
            }).ToList();
            
            return result;
        };

        var data = handler() as System.Collections.IList;
        data.ShouldNotBeNull();
    }

    [Fact]
    public void CollectionSelectInLambda_ShouldNotBeNullable()
    {
        // Simulate minimal API lambda pattern
        Func<object> handler = () =>
        {
            PersonWithChildren[] people = [
                new PersonWithChildren { Id = 1, Name = "John", Children = [
                    new Child { Id = 1, Name = "Alice" },
                    new Child { Id = 2, Name = "Bob" }
                ]},
            ];
            var result = people.AsQueryable().SelectExpr<PersonWithChildren, PersonWithChildrenDto>(s => new
            {
                Id = s.Id,
                Name = s.Name,
                ChildNames = s.Children.Select(c => c.Name).ToList(),
            }).ToList();
            
            return result;
        };

        var data = handler() as System.Collections.IList;
        data.ShouldNotBeNull();
        data.Count.ShouldBe(1);
    }

    [Fact]
    public void DirectCall_NullableType_ShouldPreserveNullability()
    {
        // Direct call (not in lambda) - this works correctly
        Person[] people = [];
        var result = people.AsQueryable().SelectExpr<Person, PersonDtoDirectCall>(s => new
        {
            Id = s.Id,
            Name = s.Name,
        }).ToList();
        
        result.ShouldNotBeNull();
    }

    [Fact]
    public void DirectCall_CollectionSelect_ShouldNotBeNullable()
    {
        // Direct call (not in lambda) - this works correctly
        PersonWithChildren[] people = [
            new PersonWithChildren { Id = 1, Name = "John", Children = [
                new Child { Id = 1, Name = "Alice" },
                new Child { Id = 2, Name = "Bob" }
            ]},
        ];
        var result = people.AsQueryable().SelectExpr<PersonWithChildren, PersonWithChildrenDtoDirectCall>(s => new
        {
            Id = s.Id,
            Name = s.Name,
            ChildNames = s.Children.Select(c => c.Name).ToList(),
        }).ToList();
        
        result.ShouldNotBeNull();
        result.Count.ShouldBe(1);
        var first = result[0];
        first.ChildNames.ShouldNotBeNull();
        first.ChildNames.Count.ShouldBe(2);
    }
}

public class Person
{
    public required int Id { get; set; }
    public required string? Name { get; set; }
}

public class PersonWithChildren
{
    public required int Id { get; set; }
    public required string? Name { get; set; }
    public required System.Collections.Generic.List<Child> Children { get; set; }
}

public class Child
{
    public required int Id { get; set; }
    public required string Name { get; set; }
}
