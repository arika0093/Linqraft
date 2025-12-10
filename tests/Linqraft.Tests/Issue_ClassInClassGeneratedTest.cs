using System.Collections.Generic;
using System.Linq;

namespace Linqraft.Tests;

/// <summary>
/// Test case for issue: Minor issues when generating multi-layer DTO classes for classes nested within other classes
/// When NestedDtoUseHashNamespace is enabled (default), implicit DTOs should be placed in LinqraftGenerated_{hash} namespace
/// WITHOUT being nested inside the parent class
/// </summary>
public partial class ClassInClassGeneratedExpr
{
    private readonly List<NestedEntity> TestData = [];

    [Fact]
    public void NestedSelectExpr_WithExplicitDtoTypes_ShouldWork()
    {
        var query = TestData.AsQueryable();

        var result = query
            .SelectExpr<NestedEntity, NestedEntityDto>(x => new
            {
                x.Id,
                x.Name,
                Items = x.Items.Select(i => new { i.Id }),
            })
            .ToList();

        result.Count.ShouldBe(0);

        // Verify that NestedEntityDto is NOT in the LinqraftGenerated_ namespace
        var nestedEntityDtoType = typeof(NestedEntityDto);
        nestedEntityDtoType.Namespace!.ShouldNotContain("LinqraftGenerated");
        nestedEntityDtoType.Namespace.ShouldBe("Linqraft.Tests");

        // Verify that the auto-generated ItemsDto IS in the LinqraftGenerated_ namespace
        // and NOT nested inside ClassInClassGeneratedExpr
        var itemsProperty = nestedEntityDtoType.GetProperty("Items");
        itemsProperty.ShouldNotBeNull();
        var itemsElementType = itemsProperty!.PropertyType.GetGenericArguments().FirstOrDefault();
        itemsElementType.ShouldNotBeNull();
        itemsElementType!.Namespace!.ShouldContain("LinqraftGenerated");

        // The key assertion: ItemsDto should NOT be nested inside ClassInClassGeneratedExpr
        // It should be directly in the LinqraftGenerated_{hash} namespace
        var itemsDtoFullName = itemsElementType.FullName!;
        itemsDtoFullName.ShouldNotContain("ClassInClassGeneratedExpr");
    }

    internal class NestedEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = null!;
        public List<NestedItem> Items { get; set; } = [];
    }

    internal class NestedItem
    {
        public int Id { get; set; }
        public string Title { get; set; } = null!;
    }

    internal partial class NestedEntityDto;
}
