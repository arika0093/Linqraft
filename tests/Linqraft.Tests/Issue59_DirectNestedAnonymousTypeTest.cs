using System.Collections.Generic;
using System.Linq;

namespace Linqraft.Tests;

/// <summary>
/// Tests for issue #59: Build Errors with Multiple Nested Anonymous Types in SelectExpr
/// This tests the scenario where nested anonymous types are used directly (not within a Select call)
/// </summary>
public class Issue59DirectNestedAnonymousTypeTest
{
    private readonly List<Quote> TestData =
    [
        new Quote
        {
            Id = 1,
            ReferenceId = "REF001",
            Channel = new Channel
            {
                Id = 10,
                ReferenceId = "CH001",
                Name = "Online",
                IsActive = true,
            },
            VehicleCategory = new VehicleCategory
            {
                Id = 20,
                ReferenceId = "VC001",
                Name = "Sedan",
                IsActive = true,
            },
            PickUpLocation = new Location
            {
                Id = 30,
                ReferenceId = "LOC001",
                Name = "Airport",
                IsActive = true,
            },
            DropOffLocation = new Location
            {
                Id = 40,
                ReferenceId = "LOC002",
                Name = "Downtown",
                IsActive = true,
            },
            QuoteExtras =
            [
                new QuoteExtra
                {
                    Id = 100,
                    ReferenceId = "QE001",
                    Quantity = 2,
                    CalculatedPrice = 50.00m,
                    TotalPrice = 100.00m,
                    Extra = new Extra
                    {
                        Id = 200,
                        ReferenceId = "EX001",
                        Name = "GPS",
                        IsActive = true,
                    },
                },
                new QuoteExtra
                {
                    Id = 101,
                    ReferenceId = "QE002",
                    Quantity = 1,
                    CalculatedPrice = 25.00m,
                    TotalPrice = 25.00m,
                    Extra = new Extra
                    {
                        Id = 201,
                        ReferenceId = "EX002",
                        Name = "Child Seat",
                        IsActive = false,
                    },
                },
            ],
        },
        new Quote
        {
            Id = 2,
            ReferenceId = "REF002",
            Channel = new Channel
            {
                Id = 11,
                ReferenceId = "CH002",
                Name = "Phone",
                IsActive = false,
            },
            VehicleCategory = new VehicleCategory
            {
                Id = 21,
                ReferenceId = "VC002",
                Name = "SUV",
                IsActive = true,
            },
            PickUpLocation = new Location
            {
                Id = 31,
                ReferenceId = "LOC003",
                Name = "Hotel",
                IsActive = true,
            },
            DropOffLocation = new Location
            {
                Id = 41,
                ReferenceId = "LOC004",
                Name = "Station",
                IsActive = false,
            },
            QuoteExtras = [],
        },
    ];

    [Fact]
    public void ExplicitDto_SingleDirectNestedAnonymousType()
    {
        var result = TestData
            .AsQueryable()
            .SelectExpr<Quote, SingleNestedDto>(q => new
            {
                q.Id,
                q.ReferenceId,
                Channel = new
                {
                    q.Channel.Id,
                    q.Channel.ReferenceId,
                    q.Channel.Name,
                    q.Channel.IsActive,
                },
            })
            .ToList();

        result.Count.ShouldBe(2);
        result[0].Id.ShouldBe(1);
        result[0].ReferenceId.ShouldBe("REF001");
        result[0].Channel.Id.ShouldBe(10);
        result[0].Channel.ReferenceId.ShouldBe("CH001");
        result[0].Channel.Name.ShouldBe("Online");
        result[0].Channel.IsActive.ShouldBeTrue();

        result[1].Channel.Id.ShouldBe(11);
        result[1].Channel.Name.ShouldBe("Phone");
        result[1].Channel.IsActive.ShouldBeFalse();
    }

    [Fact]
    public void ExplicitDto_SingleDirectNestedAnonymousType2()
    {
        var result = TestData
            .AsQueryable()
            .SelectExpr<Quote, SingleNestedDto2>(q => new
            {
                q.Id,
                q.ReferenceId,
                Channel = new
                {
                    q.Channel.Id,
                    q.Channel.ReferenceId,
                    NameInfo = new { q.Channel.Name },
                    AdditionalInfo = new { q.Channel.IsActive },
                },
            })
            .ToList();

        result.Count.ShouldBe(2);
        result[0].Id.ShouldBe(1);
        result[0].ReferenceId.ShouldBe("REF001");
        result[0].Channel.GetType().Name.ShouldStartWith("ChannelDto_");
        result[0].Channel.Id.ShouldBe(10);
        result[0].Channel.ReferenceId.ShouldBe("CH001");
        result[0].Channel.NameInfo.Name.ShouldBe("Online");
        result[0].Channel.NameInfo.GetType().Name.ShouldStartWith("ChannelDto_NameInfo_");
        result[0].Channel.AdditionalInfo.IsActive.ShouldBeTrue();
        result[0]
            .Channel.AdditionalInfo.GetType()
            .Name.ShouldStartWith("ChannelDto_AdditionalInfo_");

        result[1].Channel.Id.ShouldBe(11);
        result[1].Channel.NameInfo.Name.ShouldBe("Phone");
        result[1].Channel.AdditionalInfo.IsActive.ShouldBeFalse();
    }

    [Fact]
    public void ExplicitDto_MultipleDirectNestedAnonymousTypes()
    {
        var result = TestData
            .AsQueryable()
            .SelectExpr<Quote, MultipleNestedDto>(q => new
            {
                q.Id,
                Channel = new
                {
                    q.Channel.Id,
                    q.Channel.Name,
                    q.Channel.IsActive,
                },
                VehicleCategory = new
                {
                    q.VehicleCategory.Id,
                    q.VehicleCategory.Name,
                    q.VehicleCategory.IsActive,
                },
                PickUpLocation = new { q.PickUpLocation.Id, q.PickUpLocation.Name },
                DropOffLocation = new { q.DropOffLocation.Id, q.DropOffLocation.Name },
            })
            .ToList();

        result.Count.ShouldBe(2);

        // First quote
        result[0].Id.ShouldBe(1);
        result[0].Channel.Id.ShouldBe(10);
        result[0].Channel.Name.ShouldBe("Online");
        result[0].VehicleCategory.Id.ShouldBe(20);
        result[0].VehicleCategory.Name.ShouldBe("Sedan");
        result[0].PickUpLocation.Id.ShouldBe(30);
        result[0].PickUpLocation.Name.ShouldBe("Airport");
        result[0].DropOffLocation.Id.ShouldBe(40);
        result[0].DropOffLocation.Name.ShouldBe("Downtown");

        // Second quote
        result[1].Id.ShouldBe(2);
        result[1].Channel.Name.ShouldBe("Phone");
        result[1].VehicleCategory.Name.ShouldBe("SUV");
    }

    [Fact]
    public void ExplicitDto_NestedAnonymousTypeInCollection()
    {
        var result = TestData
            .AsQueryable()
            .SelectExpr<Quote, CollectionWithNestedDto>(q => new
            {
                q.Id,
                QuoteExtras = q
                    .QuoteExtras.Select(qe => new
                    {
                        qe.Id,
                        qe.Quantity,
                        qe.TotalPrice,
                        Extra = new
                        {
                            qe.Extra.Id,
                            qe.Extra.Name,
                            qe.Extra.IsActive,
                        },
                    })
                    .ToList(),
            })
            .ToList();

        result.Count.ShouldBe(2);

        // First quote with extras
        result[0].Id.ShouldBe(1);
        result[0].QuoteExtras.Count.ShouldBe(2);
        result[0].QuoteExtras[0].Id.ShouldBe(100);
        result[0].QuoteExtras[0].Quantity.ShouldBe(2);
        result[0].QuoteExtras[0].TotalPrice.ShouldBe(100.00m);
        result[0].QuoteExtras[0].Extra.Id.ShouldBe(200);
        result[0].QuoteExtras[0].Extra.Name.ShouldBe("GPS");
        result[0].QuoteExtras[0].Extra.IsActive.ShouldBeTrue();

        result[0].QuoteExtras[1].Extra.Name.ShouldBe("Child Seat");
        result[0].QuoteExtras[1].Extra.IsActive.ShouldBeFalse();

        // Second quote with no extras
        result[1].QuoteExtras.Count.ShouldBe(0);
    }

    [Fact]
    public void ExplicitDto_ComplexCombination()
    {
        // This is the pattern from issue #59
        var result = TestData
            .AsQueryable()
            .SelectExpr<Quote, ComplexQuoteDto>(q => new
            {
                q.Id,
                q.ReferenceId,
                Channel = new
                {
                    q.Channel.Id,
                    q.Channel.ReferenceId,
                    q.Channel.Name,
                    q.Channel.IsActive,
                },
                VehicleCategory = new
                {
                    q.VehicleCategory.Id,
                    q.VehicleCategory.ReferenceId,
                    q.VehicleCategory.Name,
                    q.VehicleCategory.IsActive,
                },
                PickUpLocation = new
                {
                    q.PickUpLocation.Id,
                    q.PickUpLocation.ReferenceId,
                    q.PickUpLocation.Name,
                    q.PickUpLocation.IsActive,
                },
                DropOffLocation = new
                {
                    q.DropOffLocation.Id,
                    q.DropOffLocation.ReferenceId,
                    q.DropOffLocation.Name,
                    q.DropOffLocation.IsActive,
                },
                QuoteExtras = q
                    .QuoteExtras.Select(qe => new
                    {
                        qe.Id,
                        qe.ReferenceId,
                        qe.Quantity,
                        qe.CalculatedPrice,
                        qe.TotalPrice,
                        Extra = new
                        {
                            qe.Extra.Id,
                            qe.Extra.ReferenceId,
                            qe.Extra.Name,
                            qe.Extra.IsActive,
                        },
                    })
                    .ToList(),
            })
            .ToList();

        result.Count.ShouldBe(2);

        // First quote - full validation
        var first = result[0];
        first.Id.ShouldBe(1);
        first.ReferenceId.ShouldBe("REF001");

        first.Channel.Id.ShouldBe(10);
        first.Channel.ReferenceId.ShouldBe("CH001");
        first.Channel.Name.ShouldBe("Online");
        first.Channel.IsActive.ShouldBeTrue();

        first.VehicleCategory.Id.ShouldBe(20);
        first.VehicleCategory.Name.ShouldBe("Sedan");

        first.PickUpLocation.Id.ShouldBe(30);
        first.PickUpLocation.Name.ShouldBe("Airport");

        first.DropOffLocation.Id.ShouldBe(40);
        first.DropOffLocation.Name.ShouldBe("Downtown");

        first.QuoteExtras.Count.ShouldBe(2);
        first.QuoteExtras[0].Extra.Id.ShouldBe(200);
        first.QuoteExtras[0].Extra.Name.ShouldBe("GPS");
        first.QuoteExtras[1].Extra.Name.ShouldBe("Child Seat");

        // Second quote
        var second = result[1];
        second.Id.ShouldBe(2);
        second.QuoteExtras.Count.ShouldBe(0);
    }

    [Fact]
    public void ExplicitDto_DirectNestedWithFirstOrDefault()
    {
        var result = TestData
            .AsQueryable()
            .SelectExpr<Quote, FirstOrDefaultNestedDto>(q => new
            {
                q.Id,
                Channel = new { q.Channel.Id, q.Channel.Name },
            })
            .FirstOrDefault();

        result.ShouldNotBeNull();
        result.Id.ShouldBe(1);
        result.Channel.Id.ShouldBe(10);
        result.Channel.Name.ShouldBe("Online");
    }

    [Fact]
    public void ExplicitDto_MultipleNestedSameStructure()
    {
        // Test case where multiple nested types have the same structure
        // This ensures unique DTO names are generated correctly
        var result = TestData
            .AsQueryable()
            .SelectExpr<Quote, SameStructureNestedDto>(q => new
            {
                q.Id,
                PickUp = new
                {
                    q.PickUpLocation.Id,
                    q.PickUpLocation.Name,
                    q.PickUpLocation.IsActive,
                },
                DropOff = new
                {
                    q.DropOffLocation.Id,
                    q.DropOffLocation.Name,
                    q.DropOffLocation.IsActive,
                },
            })
            .ToList();

        result.Count.ShouldBe(2);
        result[0].PickUp.Id.ShouldBe(30);
        result[0].PickUp.Name.ShouldBe("Airport");
        result[0].DropOff.Id.ShouldBe(40);
        result[0].DropOff.Name.ShouldBe("Downtown");
    }
}

internal class Quote
{
    public int Id { get; set; }
    public string? ReferenceId { get; set; }
    public Channel Channel { get; set; } = null!;
    public VehicleCategory VehicleCategory { get; set; } = null!;
    public Location PickUpLocation { get; set; } = null!;
    public Location DropOffLocation { get; set; } = null!;
    public List<QuoteExtra> QuoteExtras { get; set; } = [];
}

internal class Channel
{
    public int Id { get; set; }
    public string? ReferenceId { get; set; }
    public string Name { get; set; } = "";
    public bool IsActive { get; set; }
}

internal class VehicleCategory
{
    public int Id { get; set; }
    public string? ReferenceId { get; set; }
    public string Name { get; set; } = "";
    public bool IsActive { get; set; }
}

internal class Location
{
    public int Id { get; set; }
    public string? ReferenceId { get; set; }
    public string Name { get; set; } = "";
    public bool IsActive { get; set; }
}

internal class QuoteExtra
{
    public int Id { get; set; }
    public string? ReferenceId { get; set; }
    public int Quantity { get; set; }
    public decimal CalculatedPrice { get; set; }
    public decimal TotalPrice { get; set; }
    public Extra Extra { get; set; } = null!;
}

internal class Extra
{
    public int Id { get; set; }
    public string? ReferenceId { get; set; }
    public string Name { get; set; } = "";
    public bool IsActive { get; set; }
}
