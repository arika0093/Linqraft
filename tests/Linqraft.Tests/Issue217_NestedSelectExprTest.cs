using System.Collections.Generic;
using System.Linq;

namespace MinRepro;

public partial class Issue_NestedSelectExprTest
{
    private readonly List<NestedEntity> TestData = [];

    public void NestedSelectExpr_WithExplicitDtoTypes_ShouldWork()
    {
        var query = TestData.AsQueryable();

        var result = query
            .SelectExpr<NestedEntity, NestedEntityDto>(x => new
            {
                x.Id,
                x.Name,
                // Test without qualifier - expecting DTO generation in same place as calling class
                ItemsEnumerable = x.Items.SelectExpr<NestedItem, NestedItemDtoEnumerable>(i => new
                {
                    i.Id,
                }),
                ItemsList = x
                    .Items.SelectExpr<NestedItem, NestedItemDtoList>(i => new { i.Id })
                    .ToList(),
                ItemsArray = x
                    .Items.SelectExpr<NestedItem, NestedItemDtoArray>(i => new
                    {
                        i.Id,
                        i.Title,
                        SubItem = i.SubItems.Select(si => new { si.Id, si.Value }),
                        SubItemWithExpr = i.SubItems.SelectExpr<NestedSubItem, NestedSubItemDto>(
                            si => new { si.Id, si.Value }
                        ),
                    })
                    .ToArray(),
            })
            .ToList();
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
        public List<NestedSubItem> SubItems { get; set; } = [];
    }

    internal class NestedSubItem
    {
        public int Id { get; set; }
        public string Value { get; set; } = null!;
    }

    internal partial class NestedEntityDto;

    internal partial class NestedItemDtoEnumerable;

    internal partial class NestedItemDtoList;

    internal partial class NestedItemDtoArray;

    internal partial class NestedSubItemDto;
}
