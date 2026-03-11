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

    private static readonly List<FormattingRoot> FormattingRoots =
    [
        new()
        {
            Id = 1,
            Child2 =
            [
                new()
                {
                    Summary = "First",
                    GrandChilds =
                    [
                        new() { Notes = "Alpha", Value = 1 },
                        new() { Notes = "Beta", Value = 2 },
                    ],
                },
            ],
        },
    ];

    private static readonly List<CaptureFormattingItem> CaptureFormattingItems =
    [
        new() { Id = 1, Value = 3 },
        new() { Id = 2, Value = 5 },
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
        var projectionSource = GetGeneratedProjectionSourceContaining(
            "Items = order.Items",
            "ConfiguredOrderDto"
        );
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
        CountLeadingSpaces(lines[itemsLineIndex]).ShouldBe(12);
        CountLeadingSpaces(lines[itemsLineIndex + 1]).ShouldBe(16);
        CountLeadingSpaces(lines[itemsLineIndex + 2]).ShouldBe(20);
        CountLeadingSpaces(lines[itemsLineIndex + 3]).ShouldBe(24);
        CountLeadingSpaces(lines[itemsLineIndex + 4]).ShouldBe(24);
        CountLeadingSpaces(lines[itemsLineIndex + 5]).ShouldBe(20);
        CountLeadingSpaces(lines[itemsLineIndex + 6]).ShouldBe(20);
        CountLeadingSpaces(lines[itemsLineIndex + 7]).ShouldBe(16);
        projectionSource
            .Contains("Items = order.Items != null ? order.Items.Select(", StringComparison.Ordinal)
            .ShouldBeFalse();
    }

    [Test]
    public void Generated_projection_source_indents_nested_object_initializers()
    {
        var result = FormattingRoots
            .AsQueryable()
            .SelectExpr<FormattingRoot, ConfiguredNestedFormattingDto>(root => new
            {
                root.Id,
                Child2Summaries = root
                    .Child2.Select(child => new
                    {
                        child.Summary,
                        GrandChild2Notes = child.GrandChilds.Select(grandChild => grandChild.Notes),
                        GrandChild2Values = child.GrandChilds.Select(grandChild =>
                            grandChild.Value
                        ),
                    })
                    .ToList(),
            })
            .ToList();

        result.Count.ShouldBe(1);
        result[0].Child2Summaries.Count.ShouldBe(1);

        var projectionSource = GetGeneratedProjectionSourceContaining(
            "ConfiguredNestedFormattingDto",
            "Child2Summaries = root.Child2"
        );
        var lines = projectionSource.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var childLineIndex = Array.FindIndex(
            lines,
            line => line.Contains("Child2Summaries = root.Child2", StringComparison.Ordinal)
        );

        childLineIndex.ShouldBeGreaterThanOrEqualTo(0);
        lines[childLineIndex + 1]
            .Trim()
            .ShouldStartWith(".Select(child => new global::GlobalGenerated.Child2SummariesDto_");
        lines[childLineIndex + 2].Trim().ShouldBe("Summary = child.Summary,");
        lines[childLineIndex + 3]
            .Trim()
            .ShouldBe(
                "GrandChild2Notes = child.GrandChilds.Select(grandChild => grandChild.Notes),"
            );
        lines[childLineIndex + 4]
            .Trim()
            .ShouldBe(
                "GrandChild2Values = child.GrandChilds.Select(grandChild => grandChild.Value),"
            );
        lines[childLineIndex + 5].Trim().ShouldBe("})");
        CountLeadingSpaces(lines[childLineIndex]).ShouldBe(12);
        CountLeadingSpaces(lines[childLineIndex + 1]).ShouldBe(16);
        CountLeadingSpaces(lines[childLineIndex + 2]).ShouldBe(20);
        CountLeadingSpaces(lines[childLineIndex + 3]).ShouldBe(20);
        CountLeadingSpaces(lines[childLineIndex + 4]).ShouldBe(20);
        CountLeadingSpaces(lines[childLineIndex + 5]).ShouldBe(16);
        projectionSource.Contains("new {", StringComparison.Ordinal).ShouldBeFalse();
    }

    [Test]
    public void Generated_prebuilt_expression_source_dedents_after_nested_select_projection()
    {
        var result = FormattingRoots
            .AsQueryable()
            .SelectExpr<FormattingRoot, ConfiguredSelectChainFormattingDto>(root => new
            {
                root.Id,
                Child2Summaries = root.Child2.Select(child => new
                {
                    child.Summary,
                    GrandChildCount = child.GrandChilds.Count,
                    FirstGrandChildNote = child.GrandChilds[0].Notes,
                }),
                Child2Count = root.Child2.Count,
            })
            .ToList();

        result.Count.ShouldBe(1);
        result[0].Child2Summaries.Count().ShouldBe(1);
        result[0].Child2Count.ShouldBe(1);
        result[0].Child2Summaries.First().GrandChildCount.ShouldBe(2);
        result[0].Child2Summaries.First().FirstGrandChildNote.ShouldBe("Alpha");

        var projectionSource = GetGeneratedProjectionSourceContaining(
            "ConfiguredSelectChainFormattingDto",
            "Child2Summaries = root.Child2"
        );
        var lines = projectionSource.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var childLineIndex = Array.FindIndex(
            lines,
            line => line.Contains("Child2Summaries = root.Child2", StringComparison.Ordinal)
        );

        childLineIndex.ShouldBeGreaterThanOrEqualTo(0);
        lines[childLineIndex + 1]
            .Trim()
            .ShouldStartWith(".Select(child => new global::GlobalGenerated.Child2SummariesDto_");
        lines[childLineIndex + 2].Trim().ShouldBe("Summary = child.Summary,");
        lines[childLineIndex + 3].Trim().ShouldBe("GrandChildCount = child.GrandChilds.Count,");
        lines[childLineIndex + 4]
            .Trim()
            .ShouldBe("FirstGrandChildNote = child.GrandChilds[0].Notes,");
        lines[childLineIndex + 5].Trim().ShouldBe("}),");
        lines[childLineIndex + 6].Trim().ShouldBe("Child2Count = root.Child2.Count,");
        CountLeadingSpaces(lines[childLineIndex]).ShouldBe(12);
        CountLeadingSpaces(lines[childLineIndex + 1]).ShouldBe(16);
        CountLeadingSpaces(lines[childLineIndex + 2]).ShouldBe(20);
        CountLeadingSpaces(lines[childLineIndex + 3]).ShouldBe(20);
        CountLeadingSpaces(lines[childLineIndex + 4]).ShouldBe(20);
        CountLeadingSpaces(lines[childLineIndex + 5]).ShouldBe(16);
        CountLeadingSpaces(lines[childLineIndex + 6]).ShouldBe(12);
        projectionSource
            .Contains("Child2Summaries = root.Child2.Select(", StringComparison.Ordinal)
            .ShouldBeFalse();
    }

    [Test]
    public void Generated_projection_source_simplifies_null_coalescing_collection_fallbacks()
    {
        var result = Orders
            .AsQueryable()
            .SelectExpr<Order, ConfiguredOrderWithFallbackDto>(order => new
            {
                order.Id,
                Items = order.Items?.Select(item => new { item.ProductName, item.Quantity }) ?? [],
            })
            .ToList();

        result.Count.ShouldBe(2);
        result[0].Items.Count().ShouldBe(1);
        result[1].Items.ShouldBeEmpty();

        var projectionSource = GetGeneratedProjectionSourceContaining(
            "ConfiguredOrderWithFallbackDto",
            "Items = order.Items != null"
        );
        projectionSource.Contains(": null) ??", StringComparison.Ordinal).ShouldBeFalse();
        projectionSource
            .Contains(
                ": global::System.Linq.Enumerable.Empty<global::GlobalGenerated.ItemsDto_",
                StringComparison.Ordinal
            )
            .ShouldBeTrue();
    }

    [Test]
    public void Generated_projection_source_omits_runtime_query_guards()
    {
        var result = Orders
            .AsQueryable()
            .SelectExpr<Order, ConfiguredOrderDto>(order => new
            {
                order.Id,
                CustomerName = order.Customer == null ? "missing" : order.Customer.Name,
                Items = order
                    .Items?.Select(item => new { item.ProductName, item.Quantity })
                    .ToList(),
            })
            .ToList();

        result.Count.ShouldBe(2);

        var projectionSource = GetGeneratedProjectionSourceContaining(
            "ConfiguredOrderDto",
            "CustomerName = order.Customer == null ? \"missing\" : order.Customer.Name"
        );
        projectionSource.Contains("ThrowIfNull(", StringComparison.Ordinal).ShouldBeFalse();
        projectionSource.Contains("var matchedQuery =", StringComparison.Ordinal).ShouldBeFalse();
        projectionSource.Contains("matchedQuery is null", StringComparison.Ordinal).ShouldBeFalse();
        projectionSource
            .Contains("unexpected result type", StringComparison.Ordinal)
            .ShouldBeFalse();
        projectionSource
            .Contains(
                "var converted = ((global::System.Linq.IQueryable<global::Order>)(object)query).Select(",
                StringComparison.Ordinal
            )
            .ShouldBeTrue();
        projectionSource
            .Contains(
                "return (global::System.Linq.IQueryable<TResult>)(object)converted;",
                StringComparison.Ordinal
            )
            .ShouldBeTrue();

        var lines = projectionSource.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var interceptAttributeIndex = Array.FindIndex(
            lines,
            line =>
                line.Contains(
                    "[global::System.Runtime.CompilerServices.InterceptsLocationAttribute",
                    StringComparison.Ordinal
                )
        );
        var openBraceIndex = interceptAttributeIndex + 2;

        CountLeadingSpaces(lines[interceptAttributeIndex]).ShouldBe(8);
        CountLeadingSpaces(lines[interceptAttributeIndex + 1]).ShouldBe(8);
        lines[openBraceIndex].Trim().ShouldBe("{");
        lines[openBraceIndex + 1]
            .Trim()
            .ShouldStartWith(
                "var converted = ((global::System.Linq.IQueryable<global::Order>)(object)query).Select("
            );
        CountLeadingSpaces(lines[openBraceIndex + 1]).ShouldBe(12);
    }

    [Test]
    public void Generated_projection_source_indents_capture_extraction_without_extra_blank_lines()
    {
        var val = 2;
        var multiplier = 4;
        var result = CaptureFormattingItems
            .AsQueryable()
            .SelectExpr<CaptureFormattingItem, ConfiguredCaptureFormattingDto>(
                item => new
                {
                    item.Id,
                    NewValue = item.Value + val,
                    DoubledValue = item.Value * multiplier,
                },
                new { val, multiplier }
            )
            .ToList();

        result.Count.ShouldBe(2);
        result[0].NewValue.ShouldBe(5);
        result[0].DoubledValue.ShouldBe(12);
        result[1].NewValue.ShouldBe(7);
        result[1].DoubledValue.ShouldBe(20);

        var projectionSource = GetGeneratedProjectionSourceContaining(
            "ConfiguredCaptureFormattingDto",
            "DoubledValue = item.Value * __linqraft_capture_1_multiplier"
        );
        var lines = projectionSource.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var captureTypeIndex = Array.FindIndex(
            lines,
            line => line.Contains("var captureType = capture.GetType();", StringComparison.Ordinal)
        );
        var firstValueIndex = Array.FindIndex(
            lines,
            line =>
                line.Contains(
                    "var __linqraft_capture_0_valValue = __linqraft_capture_0_valProperty.GetValue(capture);",
                    StringComparison.Ordinal
                )
        );
        var secondPropertyIndex = Array.FindIndex(
            lines,
            line =>
                line.Contains(
                    "var __linqraft_capture_1_multiplierProperty = captureType.GetProperty(\"multiplier\"",
                    StringComparison.Ordinal
                )
        );
        var convertedIndex = Array.FindIndex(
            lines,
            line =>
                line.Contains(
                    "var converted = ((global::System.Linq.IQueryable<global::CaptureFormattingItem>)(object)query).Select(",
                    StringComparison.Ordinal
                )
        );
        var returnIndex = Array.FindIndex(
            lines,
            line =>
                line.Contains(
                    "return (global::System.Linq.IQueryable<TResult>)(object)converted;",
                    StringComparison.Ordinal
                )
        );

        captureTypeIndex.ShouldBeGreaterThanOrEqualTo(0);
        lines[captureTypeIndex - 1].Trim().ShouldBe("{");
        CountLeadingSpaces(lines[captureTypeIndex]).ShouldBe(12);
        CountLeadingSpaces(lines[captureTypeIndex + 1]).ShouldBe(12);
        CountLeadingSpaces(lines[captureTypeIndex + 2]).ShouldBe(12);
        CountLeadingSpaces(lines[captureTypeIndex + 3]).ShouldBe(12);
        CountLeadingSpaces(lines[captureTypeIndex + 4]).ShouldBe(16);
        CountLeadingSpaces(lines[captureTypeIndex + 5]).ShouldBe(12);
        lines[firstValueIndex - 1].Trim().ShouldBe("}");
        CountLeadingSpaces(lines[firstValueIndex]).ShouldBe(12);
        CountLeadingSpaces(lines[firstValueIndex + 1]).ShouldBe(12);
        CountLeadingSpaces(lines[secondPropertyIndex]).ShouldBe(12);
        lines[secondPropertyIndex - 1].Trim().ShouldStartWith("var __linqraft_capture_0_val =");
        lines[convertedIndex - 1].Trim().ShouldStartWith("var __linqraft_capture_1_multiplier =");
        CountLeadingSpaces(lines[convertedIndex]).ShouldBe(12);
        CountLeadingSpaces(lines[returnIndex]).ShouldBe(12);
    }

    private static string GetGeneratedProjectionSourceContaining(
        string requiredContent,
        string requiredTypeMarker
    )
    {
        return Directory
            .GetFiles(GetGeneratedRoot(), "SelectExpr_*.g.cs", SearchOption.AllDirectories)
            .OrderByDescending(path => File.GetLastWriteTimeUtc(path))
            .Select(File.ReadAllText)
            .First(source =>
                source.Contains(requiredContent, StringComparison.Ordinal)
                && source.Contains(requiredTypeMarker, StringComparison.Ordinal)
            );
    }

    private static string GetGeneratedRoot()
    {
        return Path.Combine(
            GetRepositoryRoot(),
            "tests",
            "Linqraft.Tests.Configuration",
            ".generated"
        );
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

    private static int CountLeadingSpaces(string value)
    {
        return value.TakeWhile(character => character == ' ').Count();
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

public sealed class FormattingRoot
{
    public int Id { get; set; }

    public List<FormattingChild2> Child2 { get; set; } = [];
}

public sealed class FormattingChild2
{
    public string Summary { get; set; } = string.Empty;

    public List<FormattingGrandChild2> GrandChilds { get; set; } = [];
}

public sealed class FormattingGrandChild2
{
    public string Notes { get; set; } = string.Empty;

    public int Value { get; set; }
}

public sealed class CaptureFormattingItem
{
    public int Id { get; set; }

    public int Value { get; set; }
}
