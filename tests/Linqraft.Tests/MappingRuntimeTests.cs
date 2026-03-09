using System.Linq;

namespace Linqraft.Tests;

internal static partial class MappingProjectionQueries
{
    [LinqraftMappingGenerate("ProjectToMappingOrderRow")]
    internal static IQueryable<MappingOrderRow> Template(this IQueryable<MappingOrder> source)
    {
        return source.SelectExpr<MappingOrder, MappingOrderRow>(order => new
        {
            order.Id,
            CustomerName = order.Customer?.Name,
        });
    }
}

[LinqraftMappingGenerate]
internal sealed class DeclaredMappingProjection : LinqraftMappingDeclare<DeclaredMappingOrder>
{
    protected override void DefineMapping()
    {
        Source.SelectExpr<DeclaredMappingOrder, DeclaredMappingOrderRow>(order => new
        {
            order.Id,
            Total = order.Quantity * order.UnitPrice,
        });
    }
}

public partial class MappingOrderRow;

public partial class DeclaredMappingOrderRow;

public sealed class MappingRuntimeTests
{
    [Test]
    public void Mapping_generate_attribute_creates_extension_method()
    {
        var data = new[]
        {
            new MappingOrder { Id = 1, Customer = new MappingCustomer { Name = "Ada" } },
            new MappingOrder { Id = 2, Customer = null },
        }.AsQueryable();

        var result = MappingProjectionQueries.ProjectToMappingOrderRow(data).ToList();

        result.Count.ShouldBe(2);
        result[0].Id.ShouldBe(1);
        result[0].CustomerName.ShouldBe("Ada");
        result[1].CustomerName.ShouldBeNull();
    }

    [Test]
    public void Mapping_declare_attribute_creates_extension_method()
    {
        var data = new[]
        {
            new DeclaredMappingOrder { Id = 1, Quantity = 2, UnitPrice = 12.5m },
            new DeclaredMappingOrder { Id = 2, Quantity = 1, UnitPrice = 99m },
        }.AsQueryable();

        var result = data.ProjectToDeclaredMappingOrder().ToList();

        result.Count.ShouldBe(2);
        result[0].Id.ShouldBe(1);
        result[0].Total.ShouldBe(25m);
        result[1].Id.ShouldBe(2);
        result[1].Total.ShouldBe(99m);
    }
}

public sealed class MappingOrder
{
    public int Id { get; set; }
    public MappingCustomer? Customer { get; set; }
}

public sealed class MappingCustomer
{
    public string Name { get; set; } = string.Empty;
}

public sealed class DeclaredMappingOrder
{
    public int Id { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}
