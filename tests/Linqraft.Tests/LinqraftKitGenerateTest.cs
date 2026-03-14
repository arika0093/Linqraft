using System.Collections.Generic;
using System.Linq;
using Linqraft;

namespace Linqraft.Tests;

public sealed class LinqraftKitGenerateTest
{
    [Test]
    public void Generate_projects_nested_anonymous_object_into_generated_dto()
    {
        var dto = LinqraftKit.Generate<GenerateAnonymousOrderDto>(
            new
            {
                Id = 42,
                Customer = new { Name = "Ada" },
                ItemNames = new[] { "Keyboard", "Mouse" },
            }
        );

        dto.Id.ShouldBe(42);
        dto.Customer.Name.ShouldBe("Ada");
        dto.ItemNames.ShouldBe(["Keyboard", "Mouse"]);
    }

    [Test, SkipOnNativeAot]
    public void Generate_can_combine_multiple_selectexpr_results_into_one_generated_dto()
    {
        var dto = LinqraftKit.Generate<GenerateProjectionBundleDto>(
            new
            {
                Orders = GenerateProjectionData
                    .Orders.AsQueryable()
                    .SelectExpr<GenerateSourceOrder, GenerateProjectionOrderRowDto>(order => new
                    {
                        order.Id,
                        order.CustomerName,
                    })
                    .ToList(),
                Decisions = GenerateProjectionData
                    .Invoices.AsQueryable()
                    .Select(invoice => new { invoice.Id, IsLarge = invoice.Total >= 100m })
                    .ToList(),
            }
        );

        dto.Orders.Select(x => x.CustomerName).ShouldBe(["Ada", "Grace"]);
        dto.Decisions.Select(x => x.IsLarge).ShouldBe([false, true]);
    }

    [Test, SkipOnNativeAot]
    public void Generate_can_read_captured_values_from_capture_argument()
    {
        var id = 42;
        var prefix = "Order-";
        var dto = LinqraftKit.Generate<GenerateCapturedOrderDto>(
            new
            {
                Id = id,
                Label = prefix + id,
            },
            capture: new { id, prefix }
        );

        dto.Id.ShouldBe(42);
        dto.Label.ShouldBe("Order-42");
    }
}

public static class GenerateProjectionData
{
    public static readonly List<GenerateSourceOrder> Orders =
    [
        new() { Id = 1, CustomerName = "Ada" },
        new() { Id = 2, CustomerName = "Grace" },
    ];

    public static readonly List<GenerateSourceInvoice> Invoices =
    [
        new() { Id = 10, Total = 49m },
        new() { Id = 20, Total = 150m },
    ];
}

public sealed class GenerateSourceOrder
{
    public int Id { get; set; }
    public string CustomerName { get; set; } = string.Empty;
}

public sealed class GenerateSourceInvoice
{
    public int Id { get; set; }
    public decimal Total { get; set; }
}

public partial class GenerateAnonymousOrderDto;

public partial class GenerateProjectionBundleDto;

public partial class GenerateProjectionOrderRowDto;

public partial class GenerateCapturedOrderDto;
