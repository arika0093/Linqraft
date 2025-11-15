using System.Collections.Generic;
using System.Linq;

namespace Linqraft.Tests;

/// <summary>
/// Comprehensive tests for explicit DTO patterns with various object structures.
/// Focus on nullable operators and nested selects in combination.
/// </summary>
public class ExplicitDtoComprehensiveTest
{
    #region Test Data Models

    // Simple entity with nullable properties
    internal class SimpleEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string? NullableDescription { get; set; }
        public int? NullableValue { get; set; }
    }

    // Entity with nullable reference navigation
    internal class EntityWithNullableChild
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public ChildEntity? Child { get; set; }
    }

    internal class ChildEntity
    {
        public string Description { get; set; } = "";
        public int Value { get; set; }
        public GrandChildEntity? GrandChild { get; set; }
    }

    internal class GrandChildEntity
    {
        public string Details { get; set; } = "";
        public int Number { get; set; }
        public GreatGrandChildEntity? GreatGrandChild { get; set; }
    }

    internal class GreatGrandChildEntity
    {
        public string Info { get; set; } = "";
        public int Count { get; set; }
    }

    // Entity with collections
    internal class EntityWithCollections
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public List<CollectionItem> Items { get; set; } = [];
        public List<CollectionItem>? NullableItems { get; set; }
    }

    internal class CollectionItem
    {
        public string Name { get; set; } = "";
        public int Value { get; set; }
        public CollectionItemChild? Child { get; set; }
    }

    internal class CollectionItemChild
    {
        public string Description { get; set; } = "";
        public int Score { get; set; }
    }

    // Complex entity with multiple levels and collections
    internal class ComplexEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public ComplexChild? PrimaryChild { get; set; }
        public List<ComplexChild> Children { get; set; } = [];
    }

    internal class ComplexChild
    {
        public string Name { get; set; } = "";
        public int Value { get; set; }
        public ComplexGrandChild? Detail { get; set; }
        public List<ComplexGrandChild> Details { get; set; } = [];
    }

    internal class ComplexGrandChild
    {
        public string Description { get; set; } = "";
        public int Score { get; set; }
        public ComplexGreatGrandChild? Extra { get; set; }
    }

    internal class ComplexGreatGrandChild
    {
        public string Note { get; set; } = "";
        public int Quantity { get; set; }
    }

    #endregion

    #region Test Data

    private readonly List<SimpleEntity> SimpleTestData =
    [
        new SimpleEntity
        {
            Id = 1,
            Name = "Item1",
            NullableDescription = "Desc1",
            NullableValue = 100,
        },
        new SimpleEntity
        {
            Id = 2,
            Name = "Item2",
            NullableDescription = null,
            NullableValue = null,
        },
        new SimpleEntity
        {
            Id = 3,
            Name = "Item3",
            NullableDescription = "Desc3",
            NullableValue = null,
        },
    ];

    private readonly List<EntityWithNullableChild> NullableChildTestData =
    [
        new EntityWithNullableChild
        {
            Id = 1,
            Name = "Parent1",
            Child = new ChildEntity
            {
                Description = "Child1",
                Value = 10,
                GrandChild = new GrandChildEntity
                {
                    Details = "GrandChild1",
                    Number = 20,
                    GreatGrandChild = new GreatGrandChildEntity
                    {
                        Info = "GreatGrandChild1",
                        Count = 30,
                    },
                },
            },
        },
        new EntityWithNullableChild
        {
            Id = 2,
            Name = "Parent2",
            Child = new ChildEntity
            {
                Description = "Child2",
                Value = 15,
                GrandChild = null,
            },
        },
        new EntityWithNullableChild
        {
            Id = 3,
            Name = "Parent3",
            Child = null,
        },
    ];

    private readonly List<EntityWithCollections> CollectionTestData =
    [
        new EntityWithCollections
        {
            Id = 1,
            Name = "Entity1",
            Items =
            [
                new CollectionItem
                {
                    Name = "Item1",
                    Value = 10,
                    Child = new CollectionItemChild { Description = "ItemChild1", Score = 100 },
                },
                new CollectionItem
                {
                    Name = "Item2",
                    Value = 20,
                    Child = null,
                },
            ],
            NullableItems =
            [
                new CollectionItem
                {
                    Name = "NullableItem1",
                    Value = 5,
                    Child = null,
                },
            ],
        },
        new EntityWithCollections
        {
            Id = 2,
            Name = "Entity2",
            Items = [],
            NullableItems = null,
        },
    ];

    private readonly List<ComplexEntity> ComplexTestData =
    [
        new ComplexEntity
        {
            Id = 1,
            Name = "Complex1",
            PrimaryChild = new ComplexChild
            {
                Name = "Primary1",
                Value = 100,
                Detail = new ComplexGrandChild
                {
                    Description = "PrimaryDetail1",
                    Score = 200,
                    Extra = new ComplexGreatGrandChild { Note = "PrimaryExtra1", Quantity = 300 },
                },
                Details =
                [
                    new ComplexGrandChild
                    {
                        Description = "Detail1",
                        Score = 50,
                        Extra = null,
                    },
                    new ComplexGrandChild
                    {
                        Description = "Detail2",
                        Score = 60,
                        Extra = new ComplexGreatGrandChild { Note = "Extra2", Quantity = 70 },
                    },
                ],
            },
            Children =
            [
                new ComplexChild
                {
                    Name = "Child1",
                    Value = 10,
                    Detail = null,
                    Details = [],
                },
                new ComplexChild
                {
                    Name = "Child2",
                    Value = 20,
                    Detail = new ComplexGrandChild
                    {
                        Description = "ChildDetail2",
                        Score = 30,
                        Extra = null,
                    },
                    Details =
                    [
                        new ComplexGrandChild
                        {
                            Description = "ChildDetails2-1",
                            Score = 40,
                            Extra = null,
                        },
                    ],
                },
            ],
        },
        new ComplexEntity
        {
            Id = 2,
            Name = "Complex2",
            PrimaryChild = null,
            Children = [],
        },
    ];

    #endregion

    #region Basic Nullable Operator Tests

    [Fact]
    public void ExplicitDto_SingleLevelNullableOperator()
    {
        var result = SimpleTestData
            .AsQueryable()
            .SelectExpr<SimpleEntity, SimpleNullableDto>(e => new
            {
                e.Id,
                e.Name,
                Description = e.NullableDescription,
                Value = e.NullableValue,
            })
            .ToList();

        result.Count.ShouldBe(3);
        result[0].Description.ShouldBe("Desc1");
        result[0].Value.ShouldBe(100);
        result[1].Description.ShouldBeNull();
        result[1].Value.ShouldBeNull();
    }

    [Fact]
    public void ExplicitDto_NullConditionalOnNullableProperty()
    {
        var result = NullableChildTestData
            .AsQueryable()
            .SelectExpr<EntityWithNullableChild, NullConditionalDto>(e => new
            {
                e.Id,
                e.Name,
                ChildDescription = e.Child?.Description,
                ChildValue = e.Child?.Value,
            })
            .ToList();

        result.Count.ShouldBe(3);
        result[0].ChildDescription.ShouldBe("Child1");
        result[0].ChildValue.ShouldBe(10);
        result[2].ChildDescription.ShouldBeNull();
        result[2].ChildValue.ShouldBeNull();
    }

    [Fact]
    public void ExplicitDto_MultiLevelNullConditional_TwoLevels()
    {
        var result = NullableChildTestData
            .AsQueryable()
            .SelectExpr<EntityWithNullableChild, TwoLevelNullConditionalDto>(e => new
            {
                e.Id,
                GrandChildDetails = e.Child?.GrandChild?.Details,
                GrandChildNumber = e.Child?.GrandChild?.Number,
            })
            .ToList();

        result.Count.ShouldBe(3);
        result[0].GrandChildDetails.ShouldBe("GrandChild1");
        result[0].GrandChildNumber.ShouldBe(20);
        result[1].GrandChildDetails.ShouldBeNull();
        result[1].GrandChildNumber.ShouldBeNull();
        result[2].GrandChildDetails.ShouldBeNull();
    }

    [Fact]
    public void ExplicitDto_MultiLevelNullConditional_ThreeLevels()
    {
        var result = NullableChildTestData
            .AsQueryable()
            .SelectExpr<EntityWithNullableChild, ThreeLevelNullConditionalDto>(e => new
            {
                e.Id,
                GreatGrandChildInfo = e.Child?.GrandChild?.GreatGrandChild?.Info,
                GreatGrandChildCount = e.Child?.GrandChild?.GreatGrandChild?.Count,
            })
            .ToList();

        result.Count.ShouldBe(3);
        result[0].GreatGrandChildInfo.ShouldBe("GreatGrandChild1");
        result[0].GreatGrandChildCount.ShouldBe(30);
        result[1].GreatGrandChildInfo.ShouldBeNull();
        result[2].GreatGrandChildInfo.ShouldBeNull();
    }

    [Fact]
    public void ExplicitDto_MixedNullConditionalLevels()
    {
        var result = NullableChildTestData
            .AsQueryable()
            .SelectExpr<EntityWithNullableChild, MixedNullConditionalDto>(e => new
            {
                e.Id,
                e.Name,
                ChildDescription = e.Child?.Description,
                GrandChildDetails = e.Child?.GrandChild?.Details,
                GreatGrandChildInfo = e.Child?.GrandChild?.GreatGrandChild?.Info,
            })
            .ToList();

        result.Count.ShouldBe(3);
        result[0].ChildDescription.ShouldBe("Child1");
        result[0].GrandChildDetails.ShouldBe("GrandChild1");
        result[0].GreatGrandChildInfo.ShouldBe("GreatGrandChild1");
        result[1].ChildDescription.ShouldBe("Child2");
        result[1].GrandChildDetails.ShouldBeNull();
        result[1].GreatGrandChildInfo.ShouldBeNull();
        result[2].ChildDescription.ShouldBeNull();
        result[2].GrandChildDetails.ShouldBeNull();
        result[2].GreatGrandChildInfo.ShouldBeNull();
    }

    #endregion

    #region Nested Select Tests

    [Fact]
    public void ExplicitDto_NestedSelectWithSimpleProjection()
    {
        var result = CollectionTestData
            .AsQueryable()
            .SelectExpr<EntityWithCollections, NestedSelectSimpleDto>(e => new
            {
                e.Id,
                e.Name,
                ItemNames = e.Items.Select(i => i.Name).ToList(),
            })
            .ToList();

        result.Count.ShouldBe(2);
        result[0].ItemNames.ShouldBe(["Item1", "Item2"]);
        result[1].ItemNames.ShouldBe([]);
    }

    [Fact]
    public void ExplicitDto_NestedSelectWithComplexProjection()
    {
        var result = CollectionTestData
            .AsQueryable()
            .SelectExpr<EntityWithCollections, NestedSelectComplexDto>(e => new
            {
                e.Id,
                Items = e.Items.Select(i => new { i.Name, i.Value }).ToList(),
            })
            .ToList();

        result.Count.ShouldBe(2);
        result[0].Items.Count.ShouldBe(2);
        result[0].Items[0].Name.ShouldBe("Item1");
        result[0].Items[0].Value.ShouldBe(10);
    }

    [Fact]
    public void ExplicitDto_NestedSelectWithNullConditional()
    {
        var result = CollectionTestData
            .AsQueryable()
            .SelectExpr<EntityWithCollections, NestedSelectWithNullConditionalDto>(e => new
            {
                e.Id,
                Items = e
                    .Items.Select(i => new
                    {
                        i.Name,
                        ChildDescription = i.Child?.Description,
                        ChildScore = i.Child?.Score,
                    })
                    .ToList(),
            })
            .ToList();

        result.Count.ShouldBe(2);
        result[0].Items.Count.ShouldBe(2);
        result[0].Items[0].ChildDescription.ShouldBe("ItemChild1");
        result[0].Items[0].ChildScore.ShouldBe(100);
        result[0].Items[1].ChildDescription.ShouldBeNull();
        result[0].Items[1].ChildScore.ShouldBeNull();
    }

    [Fact]
    public void ExplicitDto_MultipleNestedSelects()
    {
        var result = ComplexTestData
            .AsQueryable()
            .SelectExpr<ComplexEntity, MultipleNestedSelectsDto>(e => new
            {
                e.Id,
                ChildNames = e.Children.Select(c => c.Name).ToList(),
                ChildValues = e.Children.Select(c => c.Value).ToList(),
            })
            .ToList();

        result.Count.ShouldBe(2);
        result[0].ChildNames.ShouldBe(["Child1", "Child2"]);
        result[0].ChildValues.ShouldBe([10, 20]);
        result[1].ChildNames.ShouldBe([]);
        result[1].ChildValues.ShouldBe([]);
    }

    [Fact]
    public void ExplicitDto_TwoLevelNestedSelect()
    {
        var result = ComplexTestData
            .AsQueryable()
            .SelectExpr<ComplexEntity, TwoLevelNestedSelectDto>(e => new
            {
                e.Id,
                Children = e
                    .Children.Select(c => new
                    {
                        c.Name,
                        DetailDescriptions = c.Details.Select(d => d.Description).ToList(),
                    })
                    .ToList(),
            })
            .ToList();

        result.Count.ShouldBe(2);
        result[0].Children.Count.ShouldBe(2);
        result[0].Children[0].DetailDescriptions.ShouldBe([]);
        result[0].Children[1].DetailDescriptions.ShouldBe(["ChildDetails2-1"]);
    }

    #endregion

    #region Complex Combinations

    [Fact]
    public void ExplicitDto_NullConditionalWithNestedSelect()
    {
        var result = ComplexTestData
            .AsQueryable()
            .SelectExpr<ComplexEntity, NullConditionalWithNestedSelectDto>(e => new
            {
                e.Id,
                PrimaryChildName = e.PrimaryChild?.Name,
                PrimaryDetailDescriptions = e
                    .PrimaryChild?.Details.Select(d => d.Description)
                    .ToList(),
            })
            .ToList();

        result.Count.ShouldBe(2);
        result[0].PrimaryChildName.ShouldBe("Primary1");
        result[0].PrimaryDetailDescriptions.ShouldNotBeNull();
        result[0].PrimaryDetailDescriptions!.ShouldBe(["Detail1", "Detail2"]);
        result[1].PrimaryChildName.ShouldBeNull();
        result[1].PrimaryDetailDescriptions.ShouldBeNull();
    }

    [Fact]
    public void ExplicitDto_NestedSelectWithMultiLevelNullConditional()
    {
        var result = ComplexTestData
            .AsQueryable()
            .SelectExpr<ComplexEntity, NestedSelectWithMultiLevelNullConditionalDto>(e => new
            {
                e.Id,
                Children = e
                    .Children.Select(c => new
                    {
                        c.Name,
                        DetailDescription = c.Detail?.Description,
                        ExtraNote = c.Detail?.Extra?.Note,
                    })
                    .ToList(),
            })
            .ToList();

        result.Count.ShouldBe(2);
        result[0].Children.Count.ShouldBe(2);
        result[0].Children[0].DetailDescription.ShouldBeNull();
        result[0].Children[0].ExtraNote.ShouldBeNull();
        result[0].Children[1].DetailDescription.ShouldBe("ChildDetail2");
        result[0].Children[1].ExtraNote.ShouldBeNull();
    }

    [Fact]
    public void ExplicitDto_TwoLevelNestedSelectWithNullConditionals()
    {
        var result = ComplexTestData
            .AsQueryable()
            .SelectExpr<ComplexEntity, TwoLevelNestedSelectWithNullConditionalsDto>(e => new
            {
                e.Id,
                Children = e
                    .Children.Select(c => new
                    {
                        c.Name,
                        Details = c
                            .Details.Select(d => new
                            {
                                d.Description,
                                ExtraNote = d.Extra?.Note,
                                ExtraQuantity = d.Extra?.Quantity,
                            })
                            .ToList(),
                    })
                    .ToList(),
            })
            .ToList();

        result.Count.ShouldBe(2);
        result[0].Children.Count.ShouldBe(2);
        result[0].Children[1].Details.Count.ShouldBe(1);
        result[0].Children[1].Details[0].ExtraNote.ShouldBeNull();
        result[0].Children[1].Details[0].ExtraQuantity.ShouldBeNull();
    }

    [Fact]
    public void ExplicitDto_ComplexMixedScenario()
    {
        var result = ComplexTestData
            .AsQueryable()
            .SelectExpr<ComplexEntity, ComplexMixedScenarioDto>(e => new
            {
                e.Id,
                e.Name,
                PrimaryChildName = e.PrimaryChild?.Name,
                PrimaryDetailDescription = e.PrimaryChild?.Detail?.Description,
                PrimaryExtraNote = e.PrimaryChild?.Detail?.Extra?.Note,
                Children = e
                    .Children.Select(c => new
                    {
                        c.Name,
                        c.Value,
                        DetailDescription = c.Detail?.Description,
                        Details = c
                            .Details.Select(d => new
                            {
                                d.Description,
                                d.Score,
                                ExtraNote = d.Extra?.Note,
                            })
                            .ToList(),
                    })
                    .ToList(),
            })
            .ToList();

        result.Count.ShouldBe(2);

        // First complex entity
        result[0].PrimaryChildName.ShouldBe("Primary1");
        result[0].PrimaryDetailDescription.ShouldBe("PrimaryDetail1");
        result[0].PrimaryExtraNote.ShouldBe("PrimaryExtra1");
        result[0].Children.Count.ShouldBe(2);
        result[0].Children[0].Name.ShouldBe("Child1");
        result[0].Children[0].DetailDescription.ShouldBeNull();
        result[0].Children[0].Details.ShouldBe([]);
        result[0].Children[1].Details.Count.ShouldBe(1);
        result[0].Children[1].Details[0].ExtraNote.ShouldBeNull();

        // Second complex entity
        result[1].PrimaryChildName.ShouldBeNull();
        result[1].PrimaryDetailDescription.ShouldBeNull();
        result[1].PrimaryExtraNote.ShouldBeNull();
        result[1].Children.ShouldBe([]);
    }

    [Fact]
    public void ExplicitDto_NullableCollectionWithNestedSelect()
    {
        var result = CollectionTestData
            .AsQueryable()
            .SelectExpr<EntityWithCollections, NullableCollectionDto>(e => new
            {
                e.Id,
                NullableItemNames = e.NullableItems == null
                    ? new List<string>()
                    : e.NullableItems.Select(i => i.Name).ToList(),
            })
            .ToList();

        result.Count.ShouldBe(2);
        result[0].NullableItemNames.ShouldBe(["NullableItem1"]);
        result[1].NullableItemNames.Count.ShouldBe(0);
    }

    [Fact]
    public void ExplicitDto_ThreeLevelNestedSelectWithMixedNullConditionals()
    {
        // Create specific test data for this scenario
        var testData = new List<ComplexEntity>
        {
            new ComplexEntity
            {
                Id = 1,
                Name = "Complex1",
                Children =
                [
                    new ComplexChild
                    {
                        Name = "Child1",
                        Value = 10,
                        Details =
                        [
                            new ComplexGrandChild
                            {
                                Description = "Detail1",
                                Score = 100,
                                Extra = new ComplexGreatGrandChild
                                {
                                    Note = "Extra1",
                                    Quantity = 5,
                                },
                            },
                            new ComplexGrandChild
                            {
                                Description = "Detail2",
                                Score = 200,
                                Extra = null,
                            },
                        ],
                    },
                ],
            },
        };

        var result = testData
            .AsQueryable()
            .SelectExpr<ComplexEntity, ThreeLevelNestedDto>(e => new
            {
                e.Id,
                Children = e
                    .Children.Select(c => new
                    {
                        c.Name,
                        Details = c
                            .Details.Select(d => new
                            {
                                d.Description,
                                ExtraNote = d.Extra?.Note,
                                ExtraQuantity = d.Extra?.Quantity,
                            })
                            .ToList(),
                    })
                    .ToList(),
            })
            .ToList();

        result.Count.ShouldBe(1);
        result[0].Children.Count.ShouldBe(1);
        result[0].Children[0].Details.Count.ShouldBe(2);
        result[0].Children[0].Details[0].ExtraNote.ShouldBe("Extra1");
        result[0].Children[0].Details[0].ExtraQuantity.ShouldBe(5);
        result[0].Children[0].Details[1].ExtraNote.ShouldBeNull();
        result[0].Children[0].Details[1].ExtraQuantity.ShouldBeNull();
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void ExplicitDto_AllNullValues()
    {
        var testData = new List<EntityWithNullableChild>
        {
            new EntityWithNullableChild
            {
                Id = 1,
                Name = "AllNull",
                Child = null,
            },
        };

        var result = testData
            .AsQueryable()
            .SelectExpr<EntityWithNullableChild, AllNullDto>(e => new
            {
                e.Id,
                ChildDescription = e.Child?.Description,
                GrandChildDetails = e.Child?.GrandChild?.Details,
                GreatGrandChildInfo = e.Child?.GrandChild?.GreatGrandChild?.Info,
            })
            .ToList();

        result.Count.ShouldBe(1);
        result[0].ChildDescription.ShouldBeNull();
        result[0].GrandChildDetails.ShouldBeNull();
        result[0].GreatGrandChildInfo.ShouldBeNull();
    }

    [Fact]
    public void ExplicitDto_EmptyCollections()
    {
        var testData = new List<ComplexEntity>
        {
            new ComplexEntity
            {
                Id = 1,
                Name = "EmptyCollections",
                PrimaryChild = new ComplexChild
                {
                    Name = "Primary",
                    Value = 1,
                    Detail = null,
                    Details = [],
                },
                Children = [],
            },
        };

        var result = testData
            .AsQueryable()
            .SelectExpr<ComplexEntity, EmptyCollectionsDto>(e => new
            {
                e.Id,
                PrimaryDetails = e.PrimaryChild?.Details.Select(d => d.Description).ToList(),
                ChildNames = e.Children.Select(c => c.Name).ToList(),
            })
            .ToList();

        result.Count.ShouldBe(1);
        result[0].PrimaryDetails.ShouldNotBeNull();
        result[0].PrimaryDetails!.ShouldBe([]);
        result[0].ChildNames.ShouldBe([]);
    }

    [Fact]
    public void ExplicitDto_MixedNullAndNonNullInCollection()
    {
        var result = CollectionTestData
            .AsQueryable()
            .SelectExpr<EntityWithCollections, MixedNullCollectionDto>(e => new
            {
                e.Id,
                Items = e
                    .Items.Select(i => new
                    {
                        i.Name,
                        HasChild = i.Child != null,
                        ChildDescription = i.Child?.Description,
                    })
                    .ToList(),
            })
            .ToList();

        result.Count.ShouldBe(2);
        result[0].Items.Count.ShouldBe(2);
        result[0].Items[0].HasChild.ShouldBeTrue();
        result[0].Items[0].ChildDescription.ShouldBe("ItemChild1");
        result[0].Items[1].HasChild.ShouldBeFalse();
        result[0].Items[1].ChildDescription.ShouldBeNull();
    }

    [Fact]
    public void ExplicitDto_ConditionalNestedSelect()
    {
        var result = CollectionTestData
            .AsQueryable()
            .SelectExpr<EntityWithCollections, ConditionalNestedSelectDto>(e => new
            {
                e.Id,
                HasNullableItems = e.NullableItems != null,
                NullableItemCount = e.NullableItems == null ? 0 : e.NullableItems.Count(),
            })
            .ToList();

        result.Count.ShouldBe(2);
        result[0].HasNullableItems.ShouldBeTrue();
        result[0].NullableItemCount.ShouldBe(1);
        result[1].HasNullableItems.ShouldBeFalse();
        result[1].NullableItemCount.ShouldBe(0);
    }

    #endregion

    #region Additional Complex Scenarios

    [Fact]
    public void ExplicitDto_MultiplePropertiesWithSameNullConditionalChain()
    {
        var result = NullableChildTestData
            .AsQueryable()
            .SelectExpr<EntityWithNullableChild, MultiplePropertiesSameChainDto>(e => new
            {
                e.Id,
                GrandChildDetails = e.Child?.GrandChild?.Details,
                GrandChildNumber = e.Child?.GrandChild?.Number,
                GreatGrandChildInfo = e.Child?.GrandChild?.GreatGrandChild?.Info,
                GreatGrandChildCount = e.Child?.GrandChild?.GreatGrandChild?.Count,
            })
            .ToList();

        result.Count.ShouldBe(3);
        result[0].GrandChildDetails.ShouldBe("GrandChild1");
        result[0].GrandChildNumber.ShouldBe(20);
        result[0].GreatGrandChildInfo.ShouldBe("GreatGrandChild1");
        result[0].GreatGrandChildCount.ShouldBe(30);
    }

    [Fact]
    public void ExplicitDto_NestedSelectWithAggregation()
    {
        var result = CollectionTestData
            .AsQueryable()
            .SelectExpr<EntityWithCollections, NestedSelectAggregationDto>(e => new
            {
                e.Id,
                ItemCount = e.Items.Count(),
                TotalValue = e.Items.Sum(i => i.Value),
                MaxValue = e.Items.Any() ? e.Items.Max(i => i.Value) : 0,
            })
            .ToList();

        result.Count.ShouldBe(2);
        result[0].ItemCount.ShouldBe(2);
        result[0].TotalValue.ShouldBe(30);
        result[0].MaxValue.ShouldBe(20);
        result[1].ItemCount.ShouldBe(0);
        result[1].TotalValue.ShouldBe(0);
        result[1].MaxValue.ShouldBe(0);
    }

    [Fact]
    public void ExplicitDto_NestedSelectWithFiltering()
    {
        var result = CollectionTestData
            .AsQueryable()
            .SelectExpr<EntityWithCollections, NestedSelectFilteringDto>(e => new
            {
                e.Id,
                HighValueItems = e.Items.Where(i => i.Value > 15).Select(i => i.Name).ToList(),
                ItemsWithChildren = e
                    .Items.Where(i => i.Child != null)
                    .Select(i => i.Name)
                    .ToList(),
            })
            .ToList();

        result.Count.ShouldBe(2);
        result[0].HighValueItems.ShouldBe(["Item2"]);
        result[0].ItemsWithChildren.ShouldBe(["Item1"]);
    }

    [Fact]
    public void ExplicitDto_CombinedAggregationAndNullConditional()
    {
        var result = ComplexTestData
            .AsQueryable()
            .SelectExpr<ComplexEntity, CombinedAggregationNullConditionalDto>(e => new
            {
                e.Id,
                ChildCount = e.Children.Count(),
                PrimaryDetailCount = e.PrimaryChild?.Details.Count(),
                TotalChildValue = e.Children.Sum(c => c.Value),
            })
            .ToList();

        result.Count.ShouldBe(2);
        result[0].ChildCount.ShouldBe(2);
        result[0].PrimaryDetailCount.ShouldBe(2);
        result[0].TotalChildValue.ShouldBe(30);
        result[1].ChildCount.ShouldBe(0);
        result[1].PrimaryDetailCount.ShouldBeNull();
        result[1].TotalChildValue.ShouldBe(0);
    }

    #endregion
}
