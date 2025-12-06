using System.Collections.Generic;
using System.Linq;

namespace Linqraft.Tests;

/// <summary>
/// Test case for verifying correct type generation for nested SelectExpr.
/// This test ensures that when SelectExpr is nested inside another SelectExpr,
/// the property type is correctly identified as IEnumerable&lt;TDto&gt; rather than just TDto.
/// 
/// Related to the issue: https://github.com/arika0093/Linqraft/issues/XXX
/// "When there is a nested SelectExpr, the output in Playground does not match the actual generated result"
/// </summary>
public class NestedSelectExprTypeTest
{
    private readonly List<TestEntity> TestData =
    [
        new TestEntity
        {
            Id = 1,
            Name = "Entity1",
            Child = new TestChild { Description = "Child1" },
            Items =
            [
                new TestItem { Title = "Item1" },
                new TestItem { Title = "Item2" },
            ],
        },
        new TestEntity
        {
            Id = 2,
            Name = "Entity2",
            Child = null,
            Items = [],
        },
    ];

    /// <summary>
    /// Test that verifies the property type is correctly generated as IEnumerable&lt;TestItemDto&gt;
    /// when using nested SelectExpr with explicit DTO types.
    /// </summary>
    [Fact]
    public void NestedSelectExpr_PropertyType_ShouldBeIEnumerableOfDto()
    {
        var query = TestData.AsQueryable();
        
        // This is the exact repro case from the issue
        var result = query
            .SelectExpr<TestEntity, TestEntityDto>(x => new
            {
                x.Id,
                x.Name,
                ChildDescription = x.Child?.Description,
                ItemTitles = x.Items.SelectExpr<TestItem, TestItemDto>(i => new { i.Title }),
            })
            .ToList();

        // Verify the data is correct
        result.Count.ShouldBe(2);
        
        var first = result[0];
        first.Id.ShouldBe(1);
        first.Name.ShouldBe("Entity1");
        first.ChildDescription.ShouldBe("Child1");
        first.ItemTitles.Count().ShouldBe(2);
        first.ItemTitles.First().Title.ShouldBe("Item1");

        var second = result[1];
        second.Id.ShouldBe(2);
        second.ChildDescription.ShouldBeNull();
        second.ItemTitles.Count().ShouldBe(0);

        // Verify the type is correct - ItemTitles should be IEnumerable<TestItemDto>, not just TestItemDto
        var entityDtoType = typeof(TestEntityDto);
        var itemTitlesProperty = entityDtoType.GetProperty("ItemTitles");
        itemTitlesProperty.ShouldNotBeNull();
        
        var propertyType = itemTitlesProperty!.PropertyType;
        
        // The property type should be IEnumerable<TestItemDto> or implement it
        // Check if the property type is IEnumerable<T> or implements it
        var isEnumerableInterface = propertyType.IsGenericType && 
                                   propertyType.GetGenericTypeDefinition() == typeof(System.Collections.Generic.IEnumerable<>);
        
        if (!isEnumerableInterface)
        {
            // If not directly IEnumerable<T>, check if it implements it
            var enumerableInterface = propertyType.GetInterfaces()
                .FirstOrDefault(t => t.IsGenericType && 
                                    t.GetGenericTypeDefinition() == typeof(System.Collections.Generic.IEnumerable<>));
            enumerableInterface.ShouldNotBeNull();
            propertyType = enumerableInterface!;
        }
        
        // Get the element type from IEnumerable<T>
        var elementType = propertyType.GetGenericArguments()[0];
        elementType.Name.ShouldBe("TestItemDto");
        
        // Verify this is NOT just TestItemDto (not a single object)
        itemTitlesProperty.PropertyType.Name.ShouldNotBe("TestItemDto");
    }

    // Test data classes for the nested SelectExpr test
    public class TestEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public TestChild? Child { get; set; }
        public List<TestItem> Items { get; set; } = [];
    }

    public class TestChild
    {
        public string Description { get; set; } = "";
    }

    public class TestItem
    {
        public string Title { get; set; } = "";
    }
}
