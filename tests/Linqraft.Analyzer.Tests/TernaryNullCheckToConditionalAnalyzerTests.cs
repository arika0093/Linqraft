using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;

namespace Linqraft.Analyzer.Tests;

/// <summary>
/// Tests for TernaryNullCheckToConditionalAnalyzer (LQRS004).
/// NOTE: LQRS004 is now disabled by default because this transformation is automatically
/// applied during LQRS002/LQRS003 code fixes. 
/// 
/// The tests that previously verified LQRS004 behavior are now skipped.
/// The transformation logic is tested through LQRS002/LQRS003 code fix tests instead.
/// </summary>
public class TernaryNullCheckToConditionalAnalyzerTests
{
    // Tests for LQRS004 are skipped because:
    // 1. The transformation is now automatically applied by LQRS002/LQRS003 code fixes
    // 2. The analyzer is disabled by default (isEnabledByDefault: false)
    // 3. The functionality is tested through:
    //    - SelectToSelectExprAnonymousCodeFixProviderTests.CodeFix_SimplifiesTernaryNullCheck_WhenReturningObject
    //    - SelectToSelectExprNamedCodeFixProviderTests.CodeFix_Case1_AllExplicitConvert_PreservesFormatting

    [Fact(Skip = "LQRS004 is disabled - transformation is now handled by LQRS002/LQRS003")]
    public async Task Analyzer_Disabled_NoLongerDetectsTernaryNullCheckWithObjectCreation_InsideSelectExpr()
    {
        await Task.CompletedTask;
    }

    [Fact(Skip = "LQRS004 is disabled - transformation is now handled by LQRS002/LQRS003")]
    public async Task Analyzer_Disabled_NoLongerDetectsTernaryNullCheckWithAnonymousObjectCreation_InsideSelectExpr()
    {
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Analyzer_DoesNotDetectTernaryWithoutObjectCreation()
    {
        var test =
            @"
class Sample
{
    public Nest? Nest { get; set; }
}

class Nest
{
    public int Id { get; set; }
}

class Test
{
    void Method()
    {
        var s = new Sample();
        var result = s.Nest != null ? s.Nest.Id : 0;
    }
}";

        await RunAnalyzerTestAsync(test);
    }

    [Fact]
    public async Task Analyzer_DoesNotDetectTernaryWithoutNullCheck()
    {
        var test =
            @"
class Sample
{
    public int Value { get; set; }
}

class Test
{
    void Method()
    {
        var s = new Sample();
        var result = s.Value > 0
            ? new { s.Value }
            : null;
    }
}";

        await RunAnalyzerTestAsync(test);
    }

    [Fact(Skip = "LQRS004 is disabled - transformation is now handled by LQRS002/LQRS003")]
    public async Task Analyzer_Disabled_NoLongerDetectsTernaryNullCheckWithInvertedCondition_InsideSelectExpr()
    {
        await Task.CompletedTask;
    }

    [Fact(Skip = "LQRS004 is disabled - transformation is now handled by LQRS002/LQRS003")]
    public async Task Analyzer_Disabled_NoLongerDetectsTernaryNullCheckWithInvertedCondition_InsideSelectExprWithSelect()
    {
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Analyzer_DoesNotDetectTernaryNullCheck_OutsideSelectExpr()
    {
        // Issue #156: LQRS004 should NOT trigger outside SelectExpr
        var test =
            @"
class Sample
{
    public Nest? Nest { get; set; }
}

class Nest
{
    public int Id { get; set; }
    public string Name { get; set; }
}

class Test
{
    void Method()
    {
        var s = new Sample();
        var result = s.Nest != null
            ? new {
                Id = s.Nest.Id,
                Name = s.Nest.Name
            }
            : null;
    }
}";

        // Expect no diagnostics because it's outside SelectExpr
        await RunAnalyzerTestAsync(test);
    }

    [Fact]
    public async Task Analyzer_DoesNotDetectTernaryNullCheck_InsideRegularSelect()
    {
        // Issue #156: LQRS004 should NOT trigger inside regular .Select() calls
        var test = """
            using System.Collections.Generic;
            using System.Linq;

            class Sample
            {
                public Nest? Nest { get; set; }
            }

            class Nest
            {
                public int Id { get; set; }
            }

            class Test
            {
                void Method()
                {
                    var data = new List<Sample>();
                    var result = data.AsQueryable()
                        .Select(s => new
                        {
                            NestedData = s.Nest != null ? new { s.Nest.Id } : null
                        })
                        .ToList();
                }
            }
            """;

        // Expect no diagnostics because it's inside regular Select, not SelectExpr
        await RunAnalyzerTestAsync(test);
    }

    private static async Task RunAnalyzerTestAsync(
        string source,
        params DiagnosticResult[] expected
    )
    {
        var test = new CSharpAnalyzerTest<TernaryNullCheckToConditionalAnalyzer, DefaultVerifier>
        {
            TestCode = source,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };

        test.ExpectedDiagnostics.AddRange(expected);

        await test.RunAsync();
    }
}
