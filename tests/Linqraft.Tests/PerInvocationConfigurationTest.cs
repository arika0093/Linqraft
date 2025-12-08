using System.Collections.Generic;
using System.Linq;
using Linqraft;

namespace Linqraft.Tests;

public class PerInvocationConfigurationTest
{
    [Fact]
    public void AnonymousPattern_WithConfiguration()
    {
        var converted = TestData
            .AsQueryable()
            .SelectExpr(x => new { x.Id, x.Name, x.Value }, new LinqraftConfiguration
            {
                RecordGenerate = true,
                HasRequired = false
            })
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
        var converted = TestData
            .AsQueryable()
            .SelectExpr<TestItem, ConfigTestDto>(
                x => new { x.Id, x.Name },
                new LinqraftConfiguration
                {
                    PropertyAccessor = PropertyAccessor.GetAndInit
                }
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

        var converted = TestData
            .AsQueryable()
            .SelectExpr(
                x => new { x.Id, ModifiedName = x.Name + suffix },
                new { suffix },
                new LinqraftConfiguration
                {
                    CommentOutput = CommentOutputMode.None
                }
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

        var converted = TestData
            .AsQueryable()
            .SelectExpr<TestItem, ConfigTestDto2>(
                x => new
                {
                    x.Id,
                    TripleValue = x.Value * multiplier
                },
                new { multiplier },
                new LinqraftConfiguration
                {
                    ArrayNullabilityRemoval = false
                }
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
        var converted = TestData
            .AsQueryable()
            .SelectExpr(
                x => new ConfigTestDto3 { Id = x.Id, Value = x.Value },
                new LinqraftConfiguration
                {
                    HasRequired = false
                }
            )
            .ToList();

        converted.Count.ShouldBe(2);
        var first = converted[0];
        first.GetType().Name.ShouldBe("ConfigTestDto3");
        first.Id.ShouldBe(1);
        first.Value.ShouldBe(10);
    }

    [Fact]
    public void NestedSelectExpr_WithConfiguration()
    {
        var parentData = new List<ParentItem>
        {
            new ParentItem
            {
                Id = 1,
                Name = "Parent1",
                Children = new List<ChildItem>
                {
                    new ChildItem { Id = 10, Value = 100 },
                    new ChildItem { Id = 11, Value = 110 }
                }
            },
            new ParentItem
            {
                Id = 2,
                Name = "Parent2",
                Children = new List<ChildItem>
                {
                    new ChildItem { Id = 20, Value = 200 }
                }
            }
        };

        var converted = parentData
            .AsQueryable()
            .SelectExpr(
                x => new
                {
                    x.Id,
                    x.Name,
                    ChildValues = x.Children.AsQueryable().SelectExpr(
                        c => new { c.Id, c.Value },
                        new LinqraftConfiguration { RecordGenerate = true }
                    ).ToList()
                },
                new LinqraftConfiguration { HasRequired = false }
            )
            .ToList();

        converted.Count.ShouldBe(2);
        var first = converted[0];
        first.Id.ShouldBe(1);
        first.Name.ShouldBe("Parent1");
        first.ChildValues.Count.ShouldBe(2);
        first.ChildValues[0].Id.ShouldBe(10);
        first.ChildValues[0].Value.ShouldBe(100);
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

    public class ParentItem
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public List<ChildItem> Children { get; set; } = new();
    }

    public class ChildItem
    {
        public int Id { get; set; }
        public int Value { get; set; }
    }
}

public class ConfigTestDto3
{
    public int Id { get; set; }
    public int Value { get; set; }
}
