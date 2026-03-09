using System;
using System.Collections.Generic;
using System.Linq;

namespace Linqraft.Tests;

/// <summary>
/// Tests for null-conditional operators with LINQ method chains.
/// Addresses issue where null-conditional access before LINQ chains generates incorrect code.
/// </summary>
public class Issue_NullConditionalWithChainsTest
{
    [Fact]
    public void NullConditionalWithOrderByAndSelect_ShouldGenerateCorrectCode()
    {
        var testData = new List<TestData>
        {
            new TestData
            {
                Children =
                [
                    new ChildData
                    {
                        Child2 = new Child2
                        {
                            Child3 = new Child3
                            {
                                Child4s =
                                [
                                    new Child4
                                    {
                                        CreatedAt = new DateTimeOffset(
                                            2024,
                                            1,
                                            3,
                                            0,
                                            0,
                                            0,
                                            TimeSpan.Zero
                                        ),
                                    },
                                    new Child4
                                    {
                                        CreatedAt = new DateTimeOffset(
                                            2024,
                                            1,
                                            1,
                                            0,
                                            0,
                                            0,
                                            TimeSpan.Zero
                                        ),
                                    },
                                    new Child4
                                    {
                                        CreatedAt = new DateTimeOffset(
                                            2024,
                                            1,
                                            2,
                                            0,
                                            0,
                                            0,
                                            TimeSpan.Zero
                                        ),
                                    },
                                ],
                            },
                        },
                    },
                ],
            },
            new TestData
            {
                Children =
                [
                    new ChildData
                    {
                        Child2 = new Child2 { Child3 = null }, // Null Child3
                    },
                ],
            },
        };

        var result = testData
            .AsQueryable()
            .SelectExpr<TestData, ResultDto>(d => new
            {
                data = d.Children.Select(c => new
                {
                    // Should be List<DateTimeOffset> (non-nullable) - when Child3 is null, result1 should be empty list
                    result1 = c
                        .Child2.Child3?.Child4s.OrderByDescending(c4 => c4.CreatedAt)
                        .Select(c4 => c4.CreatedAt)
                        .ToList(),
                    // Should be a nullable anonymous type - when Child3 is null, result2 should be null
                    result2 = c
                        .Child2.Child3?.Child4s.OrderByDescending(c4 => c4.CreatedAt)
                        .Select(c4 => new { c4.CreatedAt })
                        .FirstOrDefault(),
                }),
            })
            .ToList();

        result.Count.ShouldBe(2);

        // First TestData has valid Child3 with Child4s
        var first = result[0];
        first.data.ShouldNotBeNull();
        var firstChild = first.data.First();
        firstChild.result1.ShouldNotBeNull();
        firstChild.result1!.Count.ShouldBe(3);
        // Should be ordered descending
        firstChild.result1[0].ShouldBe(new DateTimeOffset(2024, 1, 3, 0, 0, 0, TimeSpan.Zero));

        firstChild.result2.ShouldNotBeNull();
        firstChild.result2!.CreatedAt.ShouldBe(
            new DateTimeOffset(2024, 1, 3, 0, 0, 0, TimeSpan.Zero)
        );

        // Second TestData has null Child3
        var second = result[1];
        second.data.ShouldNotBeNull();
        var secondChild = second.data.First();
        // result1 is a collection type with Select, so it should be an empty list (not null)
        // per the new behavior: collections with null-conditional access and Select/SelectMany
        // use empty collection fallback instead of null
        secondChild.result1.ShouldNotBeNull();
        secondChild.result1!.Count.ShouldBe(0);
        // result2 is a single item from FirstOrDefault, so it should still be null
        secondChild.result2.ShouldBeNull();
    }

    [Fact]
    public void NullConditionalWithSimpleSelect_ShouldReturnNullForSingleResult()
    {
        var testData = new List<Parent2>
        {
            new Parent2
            {
                Child = new Child5
                {
                    Items =
                    [
                        new Item { Value = 10, CreatedAt = new DateTime(2024, 1, 1) },
                        new Item { Value = 20, CreatedAt = new DateTime(2024, 1, 2) },
                    ],
                },
            },
            new Parent2 { Child = null }, // Null child
        };

        var result = testData
            .AsQueryable()
            .SelectExpr<Parent2, Parent2Dto>(p => new
            {
                // FirstOrDefault returns single nullable element
                LatestItem = p
                    .Child?.Items.OrderByDescending(i => i.CreatedAt)
                    .Select(i => new { i.Value })
                    .FirstOrDefault(),
            })
            .ToList();

        result.Count.ShouldBe(2);

        // First parent has valid child
        var first = result[0];
        first.LatestItem.ShouldNotBeNull();
        first.LatestItem!.Value.ShouldBe(20);

        // Second parent has null child
        var second = result[1];
        second.LatestItem.ShouldBeNull();
    }
}

internal class TestData
{
    public required List<ChildData> Children { get; set; }
}

internal class ChildData
{
    public required Child2 Child2 { get; set; }
}

internal class Child2
{
    public Child3? Child3 { get; set; }
}

internal class Child3
{
    public required List<Child4> Child4s { get; set; }
}

internal class Child4
{
    public required DateTimeOffset CreatedAt { get; set; }
}

internal class Parent2
{
    public Child5? Child { get; set; }
}

internal class Child5
{
    public required List<Item> Items { get; set; }
}

internal class Item
{
    public int Value { get; set; }
    public DateTime CreatedAt { get; set; }
}
