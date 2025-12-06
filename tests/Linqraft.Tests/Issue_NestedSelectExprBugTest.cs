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
                // Use qualified name for nested partial classes
                ItemsEnumerable = x.Items.SelectExpr<NestedItem, Issue_NestedSelectExprTest.NestedItemDtoEnumerable>(i => new
                {
                    i.Id,
                }),
                ItemsList = x
                    .Items.SelectExpr<NestedItem, Issue_NestedSelectExprTest.NestedItemDtoList>(i => new { i.Id })
                    .ToList(),
                ItemsArray = x
                    .Items.SelectExpr<NestedItem, Issue_NestedSelectExprTest.NestedItemDtoArray>(i => new
                    {
                        i.Id,
                        i.Title,
                        SubItem = i.SubItems.Select(si => new { si.Id, si.Value }),
                        SubItemWithExpr = i.SubItems.SelectExpr<NestedSubItem, Issue_NestedSelectExprTest.NestedSubItemDto>(
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
}
