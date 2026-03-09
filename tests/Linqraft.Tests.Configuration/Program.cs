using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using GlobalGenerated;

public sealed class GlobalPropertyConfigurationTests
{
    private static readonly List<Order> Orders =
    [
        new()
        {
            Id = 1,
            Customer = new Customer { Name = "Ada" },
            Items = [new OrderItem { ProductName = "Keyboard", Quantity = 2 }],
        },
        new()
        {
            Id = 2,
            Customer = new Customer { Name = "Grace" },
            Items = null,
        },
    ];

    [Test]
    public void Project_wide_properties_are_applied_to_generated_types()
    {
        var results = Orders
            .AsQueryable()
            .SelectExpr<Order, ConfiguredOrderDto>(order => new
            {
                order.Id,
                CustomerName = order.Customer?.Name,
                Items = order
                    .Items?.Select(item => new { item.ProductName, item.Quantity })
                    .ToList(),
            })
            .ToList();

        results.Count.ShouldBe(2);
        results[0].GetType().Namespace.ShouldBe("GlobalGenerated");
        results[0].CustomerName.ShouldBe("Ada");
        var items = results[0].Items.ShouldNotBeNull();
        var nestedItem = items.ShouldHaveSingleItem();
        nestedItem.ProductName.ShouldBe("Keyboard");
        nestedItem.Quantity.ShouldBe(2);
        nestedItem.GetType().Namespace.ShouldBe("GlobalGenerated");
        nestedItem.GetType().Name.ShouldStartWith("ItemsDto_");

        var clone = results[0] with { CustomerName = "Updated" };
        clone.CustomerName.ShouldBe("Updated");
        clone.Id.ShouldBe(1);

        var idProperty = typeof(ConfiguredOrderDto).GetProperty(nameof(ConfiguredOrderDto.Id))!;
        idProperty.SetMethod.ShouldNotBeNull();
        idProperty.SetMethod!.IsAssembly.ShouldBeTrue();
        typeof(ConfiguredOrderDto)
            .GetCustomAttributes(typeof(RequiredMemberAttribute), inherit: false)
            .ShouldBeEmpty();
        idProperty
            .GetCustomAttributes(typeof(RequiredMemberAttribute), inherit: false)
            .ShouldBeEmpty();

        results[1].Items.ShouldBeNull();
    }

    [Test]
    public void Generated_files_reflect_non_runtime_property_settings()
    {
        var generatedRoot = Path.Combine(
            GetRepositoryRoot(),
            "tests",
            "Linqraft.Tests.Configuration",
            ".generated"
        );
        Directory.Exists(generatedRoot).ShouldBeTrue();

        var generatedSources = Directory
            .GetFiles(generatedRoot, "*.g.cs", SearchOption.AllDirectories)
            .Select(File.ReadAllText)
            .ToList();

        generatedSources.ShouldNotBeEmpty();

        var dtoSource = generatedSources.First(source =>
            source.Contains("partial record ConfiguredOrderDto")
        );
        dtoSource.Contains("/// <summary>").ShouldBeFalse();

        generatedSources
            .Any(source =>
                source.Contains("ConfiguredOrderDto")
                && source.Contains(
                    "private static readonly global::System.Linq.Expressions.Expression"
                )
            )
            .ShouldBeTrue();
    }

    [Test]
    public void Generated_support_file_uses_declarations_name()
    {
        var generatedRoot = Path.Combine(
            GetRepositoryRoot(),
            "tests",
            "Linqraft.Tests.Configuration",
            ".generated"
        );

        Directory
            .GetFiles(generatedRoot, "Linqraft.Declarations.g.cs", SearchOption.AllDirectories)
            .ShouldNotBeEmpty();
    }

    [Test]
    public void Captured_values_are_inlined_without_runtime_helper()
    {
        if (!RuntimeFeature.IsDynamicCodeSupported)
        {
            global::TUnit.Core.Skip.Test(
                "Anonymous-object capture reflection is currently not NativeAOT-safe."
            );
        }

        var offset = 5;
        var results = Orders
            .AsQueryable()
            .SelectExpr(order => new { order.Id, OffsetId = order.Id + offset }, new { offset })
            .ToList();

        results.Select(result => result.OffsetId).ShouldBe([6, 7]);

        var generatedRoot = Path.Combine(
            GetRepositoryRoot(),
            "tests",
            "Linqraft.Tests.Configuration",
            ".generated"
        );
        var captureSource = Directory
            .GetFiles(generatedRoot, "SelectExpr_*.g.cs", SearchOption.AllDirectories)
            .OrderByDescending(path => File.GetLastWriteTimeUtc(path))
            .Select(File.ReadAllText)
            .First(source =>
                source.Contains("OffsetId", StringComparison.Ordinal)
                && source.Contains("GetProperty(\"offset\"", StringComparison.Ordinal)
            );
        var declarationsSource = Directory
            .GetFiles(generatedRoot, "Linqraft.Declarations.g.cs", SearchOption.AllDirectories)
            .Select(File.ReadAllText)
            .First();

        captureSource.Contains("GetProperty(\"offset\"", StringComparison.Ordinal).ShouldBeTrue();
        captureSource.Contains("LinqraftCaptureHelper", StringComparison.Ordinal).ShouldBeFalse();
        declarationsSource
            .Contains("LinqraftCaptureHelper", StringComparison.Ordinal)
            .ShouldBeFalse();
    }

    [Test]
    public void Generated_projection_source_formats_nested_linq_over_multiple_lines()
    {
        var generatedRoot = Path.Combine(
            GetRepositoryRoot(),
            "tests",
            "Linqraft.Tests.Configuration",
            ".generated"
        );
        var projectionFile = Directory
            .GetFiles(generatedRoot, "SelectExpr_*.g.cs", SearchOption.AllDirectories)
            .Where(path =>
                File.ReadAllText(path).Contains("Items = order.Items", StringComparison.Ordinal)
            )
            .OrderByDescending(path => File.GetLastWriteTimeUtc(path))
            .First();
        var projectionSource = File.ReadAllText(projectionFile);
        var lines = projectionSource.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

        var itemsLineIndex = Array.FindIndex(
            lines,
            line => line.Contains("Items = order.Items != null", StringComparison.Ordinal)
        );

        itemsLineIndex.ShouldBeGreaterThanOrEqualTo(0);
        lines[itemsLineIndex + 1].Trim().ShouldBe("? order.Items");
        lines[itemsLineIndex + 2]
            .Trim()
            .ShouldStartWith(".Select(item => new global::GlobalGenerated.ItemsDto_");
        lines[itemsLineIndex + 3].Trim().ShouldBe("ProductName = item.ProductName,");
        lines[itemsLineIndex + 4].Trim().ShouldBe("Quantity = item.Quantity,");
        lines[itemsLineIndex + 5].Trim().ShouldBe("})");
        lines[itemsLineIndex + 6].Trim().ShouldBe(".ToList()");
        lines[itemsLineIndex + 7].Trim().ShouldBe(": null,");
        projectionSource
            .Contains("Items = order.Items != null ? order.Items.Select(", StringComparison.Ordinal)
            .ShouldBeFalse();
    }

    private static string GetRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (
                File.Exists(Path.Combine(directory.FullName, "README.md"))
                && Directory.Exists(Path.Combine(directory.FullName, "src"))
            )
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find the repository root for Linqraft.");
    }
}

public sealed class Order
{
    public int Id { get; set; }
    public Customer? Customer { get; set; }
    public List<OrderItem>? Items { get; set; }
}

public sealed class Customer
{
    public string Name { get; set; } = string.Empty;
}

public sealed class OrderItem
{
    public string ProductName { get; set; } = string.Empty;
    public int Quantity { get; set; }
}
