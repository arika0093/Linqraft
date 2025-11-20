using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    Linqraft.Analyzer.AnonymousTypeToDtoAnalyzer>;

namespace Linqraft.Analyzer.Tests;

public class AnonymousTypeToDtoAnalyzerTests
{
    [Fact]
    public async Task AnonymousType_InVariableDeclaration_ReportsDiagnostic()
    {
        var test = @"
class Test
{
    void Method()
    {
        var result = {|#0:new { Id = 1, Name = ""Test"" }|};
    }
}";

        var expected = VerifyCS.Diagnostic(AnonymousTypeToDtoAnalyzer.DiagnosticId)
            .WithLocation(0)
            .WithSeverity(DiagnosticSeverity.Info);

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task AnonymousType_InReturnStatement_ReportsDiagnostic()
    {
        var test = @"
class Test
{
    object Method()
    {
        return {|#0:new { Id = 1, Name = ""Test"" }|};
    }
}";

        var expected = VerifyCS.Diagnostic(AnonymousTypeToDtoAnalyzer.DiagnosticId)
            .WithLocation(0)
            .WithSeverity(DiagnosticSeverity.Info);

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task AnonymousType_InAssignment_ReportsDiagnostic()
    {
        var test = @"
class Test
{
    void Method()
    {
        object result;
        result = {|#0:new { Id = 1, Name = ""Test"" }|};
    }
}";

        var expected = VerifyCS.Diagnostic(AnonymousTypeToDtoAnalyzer.DiagnosticId)
            .WithLocation(0)
            .WithSeverity(DiagnosticSeverity.Info);

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task AnonymousType_InMethodArgument_ReportsDiagnostic()
    {
        var test = @"
class Test
{
    void Method()
    {
        Process({|#0:new { Id = 1, Name = ""Test"" }|});
    }

    void Process(object obj) { }
}";

        var expected = VerifyCS.Diagnostic(AnonymousTypeToDtoAnalyzer.DiagnosticId)
            .WithLocation(0)
            .WithSeverity(DiagnosticSeverity.Info);

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task AnonymousType_InLambda_ReportsDiagnostic()
    {
        var test = @"
using System;
using System.Linq;
using System.Collections.Generic;

class Test
{
    void Method()
    {
        var list = new List<int> { 1, 2, 3 };
        var result = list.Select(x => {|#0:new { Value = x }|});
    }
}";

        var expected = VerifyCS.Diagnostic(AnonymousTypeToDtoAnalyzer.DiagnosticId)
            .WithLocation(0)
            .WithSeverity(DiagnosticSeverity.Info);

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task AnonymousType_Empty_NoDiagnostic()
    {
        var test = @"
class Test
{
    void Method()
    {
        var result = new { };
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task AnonymousType_WithMultipleProperties_ReportsDiagnostic()
    {
        var test = @"
class Test
{
    void Method()
    {
        var result = {|#0:new { Id = 1, Name = ""Test"", Value = 42.5, Active = true }|};
    }
}";

        var expected = VerifyCS.Diagnostic(AnonymousTypeToDtoAnalyzer.DiagnosticId)
            .WithLocation(0)
            .WithSeverity(DiagnosticSeverity.Info);

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task AnonymousType_InConditional_ReportsDiagnostic()
    {
        var test = @"
class Test
{
    void Method()
    {
        var condition = true;
        var result = condition ? {|#0:new { Id = 1 }|} : {|#1:new { Id = 2 }|};
    }
}";

        var expected1 = VerifyCS.Diagnostic(AnonymousTypeToDtoAnalyzer.DiagnosticId)
            .WithLocation(0)
            .WithSeverity(DiagnosticSeverity.Info);
        var expected2 = VerifyCS.Diagnostic(AnonymousTypeToDtoAnalyzer.DiagnosticId)
            .WithLocation(1)
            .WithSeverity(DiagnosticSeverity.Info);

        await VerifyCS.VerifyAnalyzerAsync(test, expected1, expected2);
    }

    [Fact]
    public async Task AnonymousType_InArrayInitializer_ReportsDiagnostic()
    {
        var test = @"
class Test
{
    void Method()
    {
        var result = new[] { {|#0:new { Id = 1 }|}, {|#1:new { Id = 2 }|} };
    }
}";

        var expected1 = VerifyCS.Diagnostic(AnonymousTypeToDtoAnalyzer.DiagnosticId)
            .WithLocation(0)
            .WithSeverity(DiagnosticSeverity.Info);
        var expected2 = VerifyCS.Diagnostic(AnonymousTypeToDtoAnalyzer.DiagnosticId)
            .WithLocation(1)
            .WithSeverity(DiagnosticSeverity.Info);

        await VerifyCS.VerifyAnalyzerAsync(test, expected1, expected2);
    }
}
