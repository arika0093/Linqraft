using System.Collections.Generic;
using System.Linq;

namespace Linqraft.Tests;

/// <summary>
/// Test for issue #159: Complexly nested object creation is not correctly converted
/// Two bugs are addressed:
/// 1. Nested object creations (Child = new TestInnerData2Child { ... }) should use fully qualified names
/// 2. Named types in Select (new TestInnerData { ... }) should preserve the original type, not create DTOs
/// </summary>
public class Issue159_NestedObjectCreationTest
{
    private readonly List<Issue159_TestData> _datas =
    [
        new Issue159_TestData
        {
            TestInnerId = 1,
            InnerData = new Issue159_ChildData
            {
                Childs =
                [
                    new Issue159_Child2
                    {
                        AnotherChilds =
                        [
                            new Issue159_Child3 { Id = 1 },
                            new Issue159_Child3 { Id = 2 },
                        ]
                    }
                ]
            }
        }
    ];

    /// <summary>
    /// Test that nested object creations in Select use fully qualified type names
    /// and named types are preserved without DTO conversion
    /// </summary>
    [Fact]
    public void SelectExpr_NestedNamedType_ShouldPreserveOriginalType()
    {
        var result = _datas
            .AsQueryable()
            .SelectExpr(d => new
            {
                TestData = d.InnerData?.Childs.Select(c => new
                {
                    // Named type in nested Select should be preserved
                    InnerNestSelect = d
                        .InnerData?.Childs.Select(c2 => new Issue159_TestInnerData
                        {
                            Id = d.TestInnerId,
                        })
                        .ToList(),
                }),
            })
            .ToList();

        result.ShouldNotBeNull();
        result.Count.ShouldBe(1);
        var first = result[0];
        first.TestData.ShouldNotBeNull();
    }

    /// <summary>
    /// Test that nested object creations use fully qualified type names in initializers
    /// </summary>
    [Fact]
    public void SelectExpr_NestedObjectInInitializer_ShouldUseFullyQualifiedNames()
    {
        var result = _datas
            .AsQueryable()
            .SelectExpr(d => new
            {
                TestData = d.InnerData?.Childs.Select(c => new
                {
                    TestData2 = c.AnotherChilds.Select(a => new
                    {
                        // Direct named type - should be fully qualified
                        Inner1 = new Issue159_TestInnerData { Id = d.TestInnerId },
                        // Named type with nested object creation - both should be fully qualified
                        Inner2 = new Issue159_TestInnerData2
                        {
                            Child = new Issue159_TestInnerData2Child { Id = d.TestInnerId },
                        },
                    }),
                }),
            })
            .ToList();

        result.ShouldNotBeNull();
        result.Count.ShouldBe(1);
    }
}

// Test classes for issue 159
public class Issue159_TestData
{
    public required int TestInnerId { get; set; }
    public required Issue159_ChildData? InnerData { get; set; }
}

public class Issue159_ChildData
{
    public required List<Issue159_Child2> Childs { get; set; }
}

public class Issue159_Child2
{
    public required List<Issue159_Child3> AnotherChilds { get; set; }
}

public class Issue159_Child3
{
    public required int Id { get; set; }
}

public class Issue159_TestInnerData
{
    public required int Id { get; set; }
}

public class Issue159_TestInnerData2
{
    public required Issue159_TestInnerData2Child Child { get; set; }
}

public class Issue159_TestInnerData2Child
{
    public required int Id { get; set; }
}
