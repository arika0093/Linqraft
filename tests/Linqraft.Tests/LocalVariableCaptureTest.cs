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
            .SelectExpr(x => new { x.Id, NewValue = x.Value + val }, new { val })
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
            .SelectExpr(
                x => new
                {
                    x.Id,
                    NewValue = x.Value + val,
                    DoubledValue = x.Value * multiplier,
                    Description = x.Name + suffix,
                },
                new
                {
                    val,
                    multiplier,
                    suffix,
                }
            )
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
            .SelectExpr<TestItem, ExplicitDto1>(
                x => new { x.Id, NewValue = x.Value + val },
                new { val }
            )
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
            .SelectExpr<TestItem, ExplicitDto2>(
                x => new
                {
                    x.Id,
                    NewValue = x.Value + val,
                    DoubledValue = x.Value * multiplier,
                },
                new { val, multiplier }
            )
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
            .SelectExpr(
                x => new PredefinedDto1 { Id = x.Id, NewValue = x.Value + val },
                new { val }
            )
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
            .SelectExpr(
                x => new PredefinedDto2
                {
                    Id = x.Id,
                    NewValue = x.Value + val,
                    DoubledValue = x.Value * multiplier,
                },
                new { val, multiplier }
            )
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
        var total = baseValue + offset;
        var converted = TestData
            .AsQueryable()
            .SelectExpr(x => new { x.Id, ComputedValue = x.Value + total }, new { total })
            .ToList();

        converted.Count.ShouldBe(2);
        var first = converted[0];
        first.Id.ShouldBe(1);
        first.ComputedValue.ShouldBe(120); // 10 + 110
    }

    [Fact]
    public void CapturedVariable_WithDateTime()
    {
        var date = new DateTime(2024, 1, 1);
        var converted = TestData
            .AsQueryable()
            .SelectExpr(
                x => new
                {
                    x.Id,
                    x.Name,
                    ProcessedDate = date,
                },
                new { date }
            )
            .ToList();

        converted.Count.ShouldBe(2);
        var first = converted[0];
        first.ProcessedDate.ShouldBe(new DateTime(2024, 1, 1));
    }

    [Fact]
    public void Case1_CapturedRequestObject_AllowsMemberAccess()
    {
        var request = new RequestRange
        {
            FromDate = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
            ToDate = new DateTimeOffset(2024, 1, 3, 0, 0, 0, TimeSpan.Zero),
        };
        var users = BuildUsers();

        var converted = users
            .AsQueryable()
            .SelectExpr<UserWithCommits, UserCommitDto>(
                u => new
                {
                    u.Id,
                    CommitCount = u.Commits.Count(c =>
                        request.FromDate <= c.Created && c.Created <= request.ToDate
                    ),
                },
                new { request }
            )
            .ToList();

        converted.Count.ShouldBe(2);
        converted[0].CommitCount.ShouldBe(1);
        converted[1].CommitCount.ShouldBe(0);
    }

    [Fact]
    public void Case2_CapturedRequestFields_AllowsMemberAccess()
    {
        var request = new RequestRange
        {
            FromDate = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
            ToDate = new DateTimeOffset(2024, 1, 3, 0, 0, 0, TimeSpan.Zero),
        };
        var users = BuildUsers();

        var converted = users
            .AsQueryable()
            .SelectExpr<UserWithCommits, UserCommitDto>(
                u => new
                {
                    u.Id,
                    CommitCount = u.Commits.Count(c =>
                        request.FromDate <= c.Created && c.Created <= request.ToDate
                    ),
                },
                new { request.FromDate, request.ToDate }
            )
            .ToList();

        converted.Count.ShouldBe(2);
        converted[0].CommitCount.ShouldBe(1);
        converted[1].CommitCount.ShouldBe(0);
    }

    [Fact]
    public void IEnumerable_WithCapturedVariable()
    {
        var val = 100;
        var converted = TestData
            .AsEnumerable()
            .SelectExpr(x => new { x.Id, NewValue = x.Value + val }, new { val })
            .ToList();

        converted.Count.ShouldBe(2);
        var first = converted[0];
        first.Id.ShouldBe(1);
        first.NewValue.ShouldBe(110);
    }

    private static List<UserWithCommits> BuildUsers() =>
    [
        new()
        {
            Id = 1,
            Commits =
            [
                new Commit { Created = new DateTimeOffset(2024, 1, 2, 0, 0, 0, TimeSpan.Zero) },
            ],
        },
        new()
        {
            Id = 2,
            Commits =
            [
                new Commit { Created = new DateTimeOffset(2024, 1, 10, 0, 0, 0, TimeSpan.Zero) },
            ],
        },
    ];

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

internal class RequestRange
{
    public DateTimeOffset FromDate { get; set; }
    public DateTimeOffset ToDate { get; set; }
}

internal class Commit
{
    public DateTimeOffset Created { get; set; }
}

internal class UserWithCommits
{
    public int Id { get; set; }
    public List<Commit> Commits { get; set; } = new();
}

internal partial class UserCommitDto
{
    public int Id { get; set; }
    public int CommitCount { get; set; }
}
