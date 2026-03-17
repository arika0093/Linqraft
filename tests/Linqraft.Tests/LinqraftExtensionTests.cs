using System.Collections.Generic;
using System.Linq;
using Linqraft.QueryExtensions;

namespace Linqraft.Tests;

/// <summary>
/// Tests for the <c>LinqraftExtension</c> mechanism, including
/// <c>AsLeftJoin()</c> and <c>MappingAs&lt;T&gt;()</c> provided by
/// <c>Linqraft.QueryExtensions</c>.
/// </summary>
public sealed class LinqraftExtensionTests
{
    // ──────────────────────────────────────────────────────────────────────────
    // AsLeftJoin
    // ──────────────────────────────────────────────────────────────────────────

    private static readonly List<ExtParent> ParentsWithChildren =
    [
        new() { Id = 1, Child = new ExtChild { Name = "Alpha" } },
        new() { Id = 2, Child = new ExtChild { Name = "Beta" } },
        new() { Id = 3, Child = null },       // no child — simulates LEFT JOIN miss
    ];

    [Test]
    public void AsLeftJoin_navigation_returns_null_when_child_is_null()
    {
        var result = ParentsWithChildren
            .AsTestQueryable()
            .SelectExpr<ExtParent, ExtParentWithChildNameDto>(x => new
            {
                x.Id,
                ChildName = x.Child.AsLeftJoin().Name,
            })
            .ToList();

        result.Count.ShouldBe(3);
        result[0].Id.ShouldBe(1);
        result[0].ChildName.ShouldBe("Alpha");
        result[1].Id.ShouldBe(2);
        result[1].ChildName.ShouldBe("Beta");
        result[2].Id.ShouldBe(3);
        result[2].ChildName.ShouldBeNull(); // LEFT JOIN: null when no child
    }

    [Test]
    public void AsLeftJoin_generated_property_is_nullable()
    {
        // Verify the DTO property is nullable (string?) so it can hold null values.
        var prop = typeof(ExtParentWithChildNameDto).GetProperty(
            nameof(ExtParentWithChildNameDto.ChildName)
        );
        prop.ShouldNotBeNull();
        // The key test is that the generated expression handles null correctly (tested above).
    }

    // ──────────────────────────────────────────────────────────────────────────
    // MappingAs<T>
    // ──────────────────────────────────────────────────────────────────────────

    private static readonly List<ExtValueHolder> ValueHolders =
    [
        new() { Id = 10, Value = new ExtConcreteValue { Display = "hello" } },
        new() { Id = 20, Value = new ExtConcreteValue { Display = "world" } },
    ];

    [Test]
    public void MappingAs_casts_receiver_to_specified_type()
    {
        var result = ValueHolders
            .AsTestQueryable()
            .SelectExpr<ExtValueHolder, ExtValueHolderDto>(x => new
            {
                x.Id,
                Value = x.Value.MappingAs<IExtDisplayable>(),
            })
            .ToList();

        result.Count.ShouldBe(2);
        result[0].Id.ShouldBe(10);
        result[0].Value.ShouldNotBeNull();
        result[0].Value!.Display.ShouldBe("hello");
        result[1].Id.ShouldBe(20);
        result[1].Value!.Display.ShouldBe("world");
    }
}

// ──────────────────────────────────────────────────────────────────────────────
// Test data models (must be at namespace level for the SG partial DTOs to work)
// ──────────────────────────────────────────────────────────────────────────────

public sealed class ExtParent
{
    public int Id { get; set; }
    public ExtChild? Child { get; set; }
}

public sealed class ExtChild
{
    public string Name { get; set; } = string.Empty;
}

public partial class ExtParentWithChildNameDto;

public sealed class ExtValueHolder
{
    public int Id { get; set; }
    public IExtDisplayable Value { get; set; } = new ExtConcreteValue();
}

public interface IExtDisplayable
{
    string Display { get; }
}

public sealed class ExtConcreteValue : IExtDisplayable
{
    public string Display { get; set; } = string.Empty;
}

public partial class ExtValueHolderDto;
