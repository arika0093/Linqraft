using System.Collections.Generic;
using System.Linq;

namespace Linqraft.Tests;

public partial class SelectExprGenerateTest
{
    private readonly List<SelectExprGenerateSource> TestData =
    [
        new()
        {
            Id = 1,
            Foo = new SelectExprGenerateFoo
            {
                Bar = new SelectExprGenerateBar
                {
                    Buz = new SelectExprGenerateBuz { A = "Ada", B = 42 },
                },
            },
        },
    ];

    [Test]
    public void SelectExpr_CanUseGenerate_ForNestedObjectProjection()
    {
        var result = TestData
            .AsTestQueryable()
            .SelectExpr<SelectExprGenerateSource, SelectExprGenerateSourceDto>(x => new
            {
                x.Id,
                Buz = x.Foo.Bar.Buz.Generate(b => new { b.A, b.B }),
            })
            .ToList();

        result.Count.ShouldBe(1);
        result[0].Id.ShouldBe(1);
        result[0].Buz.A.ShouldBe("Ada");
        result[0].Buz.B.ShouldBe(42);

        var buzProperty = typeof(SelectExprGenerateSourceDto).GetProperty("Buz");
        buzProperty.ShouldNotBeNull();
        buzProperty!.PropertyType.Namespace!.ShouldContain("LinqraftGenerated");
    }

    internal sealed class SelectExprGenerateSource
    {
        public int Id { get; set; }
        public SelectExprGenerateFoo Foo { get; set; } = new();
    }

    internal sealed class SelectExprGenerateFoo
    {
        public SelectExprGenerateBar Bar { get; set; } = new();
    }

    internal sealed class SelectExprGenerateBar
    {
        public SelectExprGenerateBuz Buz { get; set; } = new();
    }

    internal sealed class SelectExprGenerateBuz
    {
        public string A { get; set; } = string.Empty;
        public int B { get; set; }
    }

    public partial class SelectExprGenerateSourceDto;
}
