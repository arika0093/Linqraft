using System.Collections.Generic;
using System.Linq;

namespace Linqraft.Tests;

public class PerInvocationConfigurationTest
{
    [Fact]
    public void AnonymousPattern_WithConfiguration()
    {
        var config = new Linqraft.LinqraftConfiguration
        {
            RecordGenerate = true,
            HasRequired = false
        };

        var converted = TestData
            .AsQueryable()
            .SelectExpr(x => new { x.Id, x.Name, x.Value }, config)
            .ToList();

        converted.Count.ShouldBe(2);
        var first = converted[0];
        first.Id.ShouldBe(1);
        first.Name.ShouldBe("Item1");
        first.Value.ShouldBe(10);
    }

    [Fact]
    public void ExplicitPattern_WithConfiguration()
    {
        var config = new Linqraft.LinqraftConfiguration
        {
            PropertyAccessor = Linqraft.PropertyAccessor.GetAndInit
        };

        var converted = TestData
            .AsQueryable()
            .SelectExpr<TestItem, ConfigTestDto>(
                x => new { x.Id, x.Name },
                config
            )
            .ToList();

        converted.Count.ShouldBe(2);
        var first = converted[0];
        first.GetType().Name.ShouldBe("ConfigTestDto");
        first.Id.ShouldBe(1);
        first.Name.ShouldBe("Item1");
    }

    [Fact]
    public void AnonymousPattern_WithCaptureAndConfiguration()
    {
        var suffix = " (modified)";
        var config = new Linqraft.LinqraftConfiguration
        {
            CommentOutput = Linqraft.CommentOutputMode.None
        };

        var converted = TestData
            .AsQueryable()
            .SelectExpr(
                x => new { x.Id, ModifiedName = x.Name + suffix },
                new { suffix },
                config
            )
            .ToList();

        converted.Count.ShouldBe(2);
        var first = converted[0];
        first.Id.ShouldBe(1);
        first.ModifiedName.ShouldBe("Item1 (modified)");
    }

    [Fact]
    public void ExplicitPattern_WithCaptureAndConfiguration()
    {
        var multiplier = 3;
        var config = new Linqraft.LinqraftConfiguration
        {
            ArrayNullabilityRemoval = false
        };

        var converted = TestData
            .AsQueryable()
            .SelectExpr<TestItem, ConfigTestDto2>(
                x => new
                {
                    x.Id,
                    TripleValue = x.Value * multiplier
                },
                new { multiplier },
                config
            )
            .ToList();

        converted.Count.ShouldBe(2);
        var first = converted[0];
        first.GetType().Name.ShouldBe("ConfigTestDto2");
        first.Id.ShouldBe(1);
        first.TripleValue.ShouldBe(30); // 10 * 3
    }

    [Fact]
    public void NamedPattern_WithConfiguration()
    {
        var config = new Linqraft.LinqraftConfiguration
        {
            HasRequired = false
        };

        var converted = TestData
            .AsQueryable()
            .SelectExpr(
                x => new ConfigTestDto3 { Id = x.Id, Value = x.Value },
                config
            )
            .ToList();

        converted.Count.ShouldBe(2);
        var first = converted[0];
        first.GetType().Name.ShouldBe("ConfigTestDto3");
        first.Id.ShouldBe(1);
        first.Value.ShouldBe(10);
    }

    public static List<TestItem> TestData =>
    [
        new TestItem { Id = 1, Name = "Item1", Value = 10 },
        new TestItem { Id = 2, Name = "Item2", Value = 20 }
    ];

    public class TestItem
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int Value { get; set; }
    }
}

public class ConfigTestDto3
{
    public int Id { get; set; }
    public int Value { get; set; }
}
