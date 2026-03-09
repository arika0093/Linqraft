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

    [Fact]
    public void Project_wide_properties_are_applied_to_generated_types()
    {
        var results = Orders
            .AsQueryable()
            .SelectExpr<Order, ConfiguredOrderDto>(order => new
            {
                order.Id,
                CustomerName = order.Customer?.Name,
                Items = order.Items?.Select(item => new
                {
                    item.ProductName,
                    item.Quantity,
                }).ToList(),
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
        typeof(ConfiguredOrderDto).GetCustomAttributes(typeof(RequiredMemberAttribute), inherit: false).ShouldBeEmpty();
        idProperty.GetCustomAttributes(typeof(RequiredMemberAttribute), inherit: false).ShouldBeEmpty();

        results[1].Items.ShouldBeNull();
    }

    [Fact]
    public void Generated_files_reflect_non_runtime_property_settings()
    {
        var generatedRoot = Path.Combine(GetRepositoryRoot(), "tests", "Linqraft.Tests.Configuration", ".generated");
        Directory.Exists(generatedRoot).ShouldBeTrue();

        var generatedSources = Directory
            .GetFiles(generatedRoot, "*.g.cs", SearchOption.AllDirectories)
            .Select(File.ReadAllText)
            .ToList();

        generatedSources.ShouldNotBeEmpty();

        var dtoSource = generatedSources.First(source => source.Contains("partial record ConfiguredOrderDto"));
        dtoSource.Contains("/// <summary>").ShouldBeFalse();

        generatedSources
            .Any(source => source.Contains("ConfiguredOrderDto") && source.Contains("private static readonly global::System.Linq.Expressions.Expression"))
            .ShouldBeTrue();
    }

    private static string GetRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "README.md"))
                && Directory.Exists(Path.Combine(directory.FullName, "src")))
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
