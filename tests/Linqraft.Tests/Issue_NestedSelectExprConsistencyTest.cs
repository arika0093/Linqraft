using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Linqraft.Tests;

/// <summary>
/// Test case to verify the fix for the nested SelectExpr issue.
/// This test reproduces the exact scenario from the bug report and verifies:
/// 1. Only one interceptor is generated (outer SelectExpr)
/// 2. The ItemTitles property type is IEnumerable&lt;ItemDto&gt; (not just ItemDto)
/// 3. The generated code matches expectations
/// 
/// Related to issue: "When there is a nested SelectExpr, the output in Playground does not match the actual generated result"
/// </summary>
public class Issue_NestedSelectExprConsistencyTest
{
    private readonly List<MinReproEntity> TestData =
    [
        new MinReproEntity
        {
            Id = 1,
            Name = "Entity1",
            Child = new MinReproChild { Description = "Child1" },
            Items =
            [
                new MinReproItem { Title = "Item1" },
                new MinReproItem { Title = "Item2" },
                new MinReproItem { Title = "Item3" },
            ],
        },
        new MinReproEntity
        {
            Id = 2,
            Name = "Entity2",
            Child = null,
            Items = [],
        },
    ];

    /// <summary>
    /// This test reproduces the exact scenario from the bug report.
    /// It verifies that:
    /// 1. The code compiles and runs correctly
    /// 2. Only ONE interceptor method is generated (for the outer SelectExpr)
    /// 3. The ItemTitles property has the correct type: IEnumerable&lt;MinReproItemDto&gt;
    /// </summary>
    [Fact]
    public void MinimalRepro_FromIssue_ShouldGenerateCorrectType()
    {
        var query = TestData.AsQueryable();
        
        // This is the EXACT code from the bug report
        var result = query
            .SelectExpr<MinReproEntity, MinReproEntityDto>(x => new
            {
                x.Id,
                x.Name,
                ChildDescription = x.Child?.Description,
                ItemTitles = x.Items.SelectExpr<MinReproItem, MinReproItemDto>(i => new { i.Title }),
            })
            .ToList();

        // Verify the functionality works correctly
        result.Count.ShouldBe(2);
        
        var first = result[0];
        first.Id.ShouldBe(1);
        first.Name.ShouldBe("Entity1");
        first.ChildDescription.ShouldBe("Child1");
        
        // The ItemTitles should be a collection with 3 items
        var itemTitles = first.ItemTitles.ToList();
        itemTitles.Count.ShouldBe(3);
        itemTitles[0].Title.ShouldBe("Item1");
        itemTitles[1].Title.ShouldBe("Item2");
        itemTitles[2].Title.ShouldBe("Item3");

        var second = result[1];
        second.Id.ShouldBe(2);
        second.ChildDescription.ShouldBeNull();
        second.ItemTitles.Count().ShouldBe(0);

        // CRITICAL: Verify the type structure is correct
        var entityDtoType = typeof(MinReproEntityDto);
        var itemTitlesProperty = entityDtoType.GetProperty("ItemTitles");
        itemTitlesProperty.ShouldNotBeNull();

        // The property type should be IEnumerable<MinReproItemDto>, NOT MinReproItemDto
        var propertyType = itemTitlesProperty!.PropertyType;
        
        // Verify it's a collection type (IEnumerable<T>)
        var isEnumerable = propertyType.IsGenericType && 
                          propertyType.GetGenericTypeDefinition() == typeof(IEnumerable<>);
        
        if (!isEnumerable)
        {
            // Check if it implements IEnumerable<T>
            isEnumerable = propertyType.GetInterfaces()
                .Any(t => t.IsGenericType && 
                         t.GetGenericTypeDefinition() == typeof(IEnumerable<>));
        }
        
        isEnumerable.ShouldBeTrue("ItemTitles should be IEnumerable<MinReproItemDto>, not MinReproItemDto");
        
        // Get the element type
        Type elementType;
        if (propertyType.IsGenericType && propertyType.GetGenericTypeDefinition() == typeof(IEnumerable<>))
        {
            elementType = propertyType.GetGenericArguments()[0];
        }
        else
        {
            var enumerableInterface = propertyType.GetInterfaces()
                .First(t => t.IsGenericType && 
                           t.GetGenericTypeDefinition() == typeof(IEnumerable<>));
            elementType = enumerableInterface.GetGenericArguments()[0];
        }
        
        elementType.Name.ShouldBe("MinReproItemDto");
    }

    /// <summary>
    /// Verify that only ONE generated method exists (for the outer SelectExpr).
    /// The inner SelectExpr should NOT generate an interceptor - it should be converted to Select.
    /// </summary>
    [Fact]
    public void MinimalRepro_ShouldGenerateOnlyOneInterceptor()
    {
        // We verify this indirectly by checking that:
        // 1. The code compiles successfully (no duplicate interceptors)
        // 2. The ItemTitles property exists and has the correct collection type
        // If multiple interceptors were generated, we would get compilation errors
        
        var entityDtoType = typeof(MinReproEntityDto);
        entityDtoType.ShouldNotBeNull();
        
        var itemTitlesProperty = entityDtoType.GetProperty("ItemTitles");
        itemTitlesProperty.ShouldNotBeNull();
        
        // The property should be a collection type, not a single object
        var propertyType = itemTitlesProperty!.PropertyType;
        
        // Verify it implements IEnumerable
        var isEnumerable = typeof(System.Collections.IEnumerable).IsAssignableFrom(propertyType);
        isEnumerable.ShouldBeTrue(
            "ItemTitles should be a collection type. " +
            "If it was just MinReproItemDto, this would indicate that the nested SelectExpr " +
            "was incorrectly analyzed.");
        
        // Verify it's IEnumerable<T> (generic)
        var isGenericEnumerable = propertyType.IsGenericType && 
                                 propertyType.GetGenericTypeDefinition() == typeof(IEnumerable<>);
        
        if (!isGenericEnumerable)
        {
            // Check if it implements IEnumerable<T>
            isGenericEnumerable = propertyType.GetInterfaces()
                .Any(t => t.IsGenericType && 
                         t.GetGenericTypeDefinition() == typeof(IEnumerable<>));
        }
        
        isGenericEnumerable.ShouldBeTrue(
            "ItemTitles should be IEnumerable<MinReproItemDto>, not just IEnumerable");
    }

    // Test data classes matching the bug report
    public class MinReproEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public MinReproChild? Child { get; set; }
        public List<MinReproItem> Items { get; set; } = [];
    }

    public class MinReproChild
    {
        public string Description { get; set; } = "";
    }

    public class MinReproItem
    {
        public string Title { get; set; } = "";
    }
}
