using System;
using System.Collections.Generic;
using System.Linq;

namespace Linqraft.Tests;

public class LocalVariableCaptureTest
{
    [Fact]
    public void AnonymousPattern_WithSingleCapturedVariable()
    {
        var val = 100;
        var converted = TestData
            .AsQueryable()
            .SelectExpr((x, capture) => new
            {
                x.Id,
                NewValue = x.Value + capture.val,
            }, new { val })
            .ToList();

        converted.Count.ShouldBe(2);
        var first = converted[0];
        first.Id.ShouldBe(1);
        first.NewValue.ShouldBe(110); // 10 + 100
    }

    [Fact]
    public void AnonymousPattern_WithMultipleCapturedVariables()
    {
        var val = 100;
        var multiplier = 2;
        var suffix = " units";
        var converted = TestData
            .AsQueryable()
            .SelectExpr((x, c) => new
            {
                x.Id,
                NewValue = x.Value + c.val,
                DoubledValue = x.Value * c.multiplier,
                Description = x.Name + c.suffix,
            }, new { val, multiplier, suffix })
            .ToList();

        converted.Count.ShouldBe(2);
        var first = converted[0];
        first.Id.ShouldBe(1);
        first.NewValue.ShouldBe(110); // 10 + 100
        first.DoubledValue.ShouldBe(20); // 10 * 2
        first.Description.ShouldBe("Item1 units");
    }

    [Fact]
    public void ExplicitPattern_WithSingleCapturedVariable()
    {
        var val = 100;
        var converted = TestData
            .AsQueryable()
            .SelectExpr<TestItem, ExplicitDto1>((x, capture) => new
            {
                x.Id,
                NewValue = x.Value + capture.val,
            }, new { val })
            .ToList();

        converted.Count.ShouldBe(2);
        var first = converted[0];
        first.GetType().Name.ShouldBe("ExplicitDto1");
        first.Id.ShouldBe(1);
        first.NewValue.ShouldBe(110);
    }

    [Fact]
    public void ExplicitPattern_WithMultipleCapturedVariables()
    {
        var val = 100;
        var multiplier = 2;
        var converted = TestData
            .AsQueryable()
            .SelectExpr<TestItem, ExplicitDto2>((x, c) => new
            {
                x.Id,
                NewValue = x.Value + c.val,
                DoubledValue = x.Value * c.multiplier,
            }, new { val, multiplier })
            .ToList();

        converted.Count.ShouldBe(2);
        var first = converted[0];
        first.GetType().Name.ShouldBe("ExplicitDto2");
        first.Id.ShouldBe(1);
        first.NewValue.ShouldBe(110);
        first.DoubledValue.ShouldBe(20);
    }

    [Fact]
    public void PredefinedPattern_WithSingleCapturedVariable()
    {
        var val = 100;
        var converted = TestData
            .AsQueryable()
            .SelectExpr((x, capture) => new PredefinedDto1
            {
                Id = x.Id,
                NewValue = x.Value + capture.val,
            }, new { val })
            .ToList();

        converted.Count.ShouldBe(2);
        var first = converted[0];
        first.GetType().ShouldBe(typeof(PredefinedDto1));
        first.Id.ShouldBe(1);
        first.NewValue.ShouldBe(110);
    }

    [Fact]
    public void PredefinedPattern_WithMultipleCapturedVariables()
    {
        var val = 100;
        var multiplier = 2;
        var converted = TestData
            .AsQueryable()
            .SelectExpr((x, c) => new PredefinedDto2
            {
                Id = x.Id,
                NewValue = x.Value + c.val,
                DoubledValue = x.Value * c.multiplier,
            }, new { val, multiplier })
            .ToList();

        converted.Count.ShouldBe(2);
        var first = converted[0];
        first.GetType().ShouldBe(typeof(PredefinedDto2));
        first.Id.ShouldBe(1);
        first.NewValue.ShouldBe(110);
        first.DoubledValue.ShouldBe(20);
    }

    [Fact]
    public void CapturedVariable_WithComplexExpression()
    {
        var baseValue = 50;
        var offset = 60;
        var converted = TestData
            .AsQueryable()
            .SelectExpr((x, c) => new
            {
                x.Id,
                ComputedValue = x.Value + c.total,
            }, new { total = baseValue + offset })
            .ToList();

        converted.Count.ShouldBe(2);
        var first = converted[0];
        first.Id.ShouldBe(1);
        first.ComputedValue.ShouldBe(120); // 10 + 110
    }

    [Fact]
    public void CapturedVariable_WithDateTime()
    {
        var currentDate = new DateTime(2024, 1, 1);
        var converted = TestData
            .AsQueryable()
            .SelectExpr((x, c) => new
            {
                x.Id,
                x.Name,
                ProcessedDate = c.date,
            }, new { date = currentDate })
            .ToList();

        converted.Count.ShouldBe(2);
        var first = converted[0];
        first.ProcessedDate.ShouldBe(new DateTime(2024, 1, 1));
    }

    [Fact]
    public void IEnumerable_WithCapturedVariable()
    {
        var val = 100;
        var converted = TestData
            .AsEnumerable()
            .SelectExpr((x, capture) => new
            {
                x.Id,
                NewValue = x.Value + capture.val,
            }, new { val })
            .ToList();

        converted.Count.ShouldBe(2);
        var first = converted[0];
        first.Id.ShouldBe(1);
        first.NewValue.ShouldBe(110);
    }

    private readonly List<TestItem> TestData =
    [
        new()
        {
            Id = 1,
            Name = "Item1",
            Value = 10,
        },
        new()
        {
            Id = 2,
            Name = "Item2",
            Value = 20,
        },
    ];
}

internal class TestItem
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int Value { get; set; }
}

internal class PredefinedDto1
{
    public int Id { get; set; }
    public int NewValue { get; set; }
}

internal class PredefinedDto2
{
    public int Id { get; set; }
    public int NewValue { get; set; }
    public int DoubledValue { get; set; }
}
