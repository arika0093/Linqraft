using System.Collections.Generic;
using System.Linq;
using Linqraft.Tests.CustomProjectionExtensions;

namespace Linqraft.Tests
{
    public sealed class ExtensionMethodResolutionTest
    {
        [Test]
        public void SelectExpr_resolves_user_extension_methods_without_relying_on_generated_usings()
        {
            var result = new[]
            {
                new ExtensionMethodSource
                {
                    Id = 1,
                    Items =
                    [
                        new ExtensionMethodItem { Name = "Ada" },
                        new ExtensionMethodItem { Name = "Grace" },
                    ],
                },
            }.AsTestQueryable().SelectExpr<ExtensionMethodSource, ExtensionMethodProjectionDto>(x => new { x.Id, Names = x.Items.Select(item => item.Name).ToCustomList() }).ToList();

            result.Count.ShouldBe(1);
            result[0].Id.ShouldBe(1);
            result[0].Names.ShouldBe(["Ada", "Grace"]);
        }
    }

    public sealed class ExtensionMethodSource
    {
        public int Id { get; set; }
        public List<ExtensionMethodItem> Items { get; set; } = [];
    }

    public sealed class ExtensionMethodItem
    {
        public string Name { get; set; } = string.Empty;
    }

    public partial class ExtensionMethodProjectionDto;
}

namespace Linqraft.Tests.CustomProjectionExtensions
{
    public static class CollectionProjectionExtensions
    {
        public static List<T> ToCustomList<T>(this IEnumerable<T> source) => source.ToList();
    }
}
