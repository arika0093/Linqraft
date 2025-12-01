using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;

namespace Linqraft.Analyzer.Tests;

/// <summary>
/// Tests for TernaryNullCheckToConditionalCodeFixProvider.
/// NOTE: LQRS004 is now disabled by default because this transformation is automatically
/// applied during LQRS002/LQRS003 code fixes. The code fix provider still exists for
/// backward compatibility when LQRS004 is explicitly enabled.
/// 
/// These tests are marked as skipped because the analyzer is disabled and
/// no diagnostics will be reported to trigger the code fix.
/// The transformation logic is now tested through LQRS002/LQRS003 code fix tests instead.
/// </summary>
public class TernaryNullCheckToConditionalCodeFixProviderTests
{
    // All tests are skipped because LQRS004 is disabled by default.
    // The transformation is now tested through:
    // - SelectToSelectExprAnonymousCodeFixProviderTests.CodeFix_SimplifiesTernaryNullCheck_WhenReturningObject
    // - SelectToSelectExprNamedCodeFixProviderTests.CodeFix_Case1_AllExplicitConvert_PreservesFormatting (which includes ternary simplification)

    [Fact(Skip = "LQRS004 is disabled - transformation is now handled by LQRS002/LQRS003")]
    public async Task CodeFix_ConvertsTernaryToNullConditional_SimpleCase_InsideSelectExpr()
    {
        await Task.CompletedTask;
    }

    [Fact(Skip = "LQRS004 is disabled - transformation is now handled by LQRS002/LQRS003")]
    public async Task CodeFix_ConvertsTernaryToNullConditional_NestedNullChecks_InsideSelectExpr()
    {
        await Task.CompletedTask;
    }

    [Fact(Skip = "LQRS004 is disabled - transformation is now handled by LQRS002/LQRS003")]
    public async Task CodeFix_ConvertsTernaryToNullConditional_InvertedCondition_InsideSelectExpr()
    {
        await Task.CompletedTask;
    }
}
