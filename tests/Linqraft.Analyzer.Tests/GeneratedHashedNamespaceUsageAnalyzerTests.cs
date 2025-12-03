using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    Linqraft.Analyzer.GeneratedHashedNamespaceUsageAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier
>;

namespace Linqraft.Analyzer.Tests;

public class GeneratedHashedNamespaceUsageAnalyzerTests
{
    [Fact]
    public async Task UsingDirective_WithGeneratedHashNamespace_ReportsDiagnostic()
    {
        var test =
            @"
{|#0:using MyApp.LinqraftGenerated_A1470000;|}

namespace MyApp.LinqraftGenerated_A1470000
{
    public class Dto { }
}

class Test
{
    void Method()
    {
    }
}";

        var expected = VerifyCS
            .Diagnostic(GeneratedHashedNamespaceUsageAnalyzer.AnalyzerId)
            .WithLocation(0)
            .WithSeverity(DiagnosticSeverity.Warning)
            .WithArguments("LinqraftGenerated_A1470000");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task UsingDirective_WithNestedGeneratedHashNamespace_ReportsDiagnostic()
    {
        var test =
            @"
{|#0:using Company.Project.LinqraftGenerated_12345678.SubNamespace;|}

namespace Company.Project.LinqraftGenerated_12345678.SubNamespace
{
    public class Dto { }
}

class Test
{
    void Method()
    {
    }
}";

        var expected = VerifyCS
            .Diagnostic(GeneratedHashedNamespaceUsageAnalyzer.AnalyzerId)
            .WithLocation(0)
            .WithSeverity(DiagnosticSeverity.Warning)
            .WithArguments("LinqraftGenerated_12345678");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task UsingDirective_WithNormalNamespace_NoDiagnostic()
    {
        var test =
            @"
using System;
using System.Collections.Generic;

namespace MyApp.Models
{
    public class Dto { }
}

class Test
{
    void Method()
    {
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task UsingDirective_WithGeneratedButNoHash_NoDiagnostic()
    {
        var test =
            @"
using MyApp.Generated;
using MyApp.GeneratedCode;

namespace MyApp.Generated
{
    public class Dto1 { }
}

namespace MyApp.GeneratedCode
{
    public class Dto2 { }
}

class Test
{
    void Method()
    {
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task UsingDirective_WithShortHash_NoDiagnostic()
    {
        var test =
            @"
using MyApp.LinqraftGenerated_AB;
using MyApp.LinqraftGenerated_ABCD;
using MyApp.LinqraftGenerated_ABCD123;

namespace MyApp.LinqraftGenerated_AB
{
    public class Dto1 { }
}

namespace MyApp.LinqraftGenerated_ABCD
{
    public class Dto2 { }
}

namespace MyApp.LinqraftGenerated_ABCD123
{
    public class Dto3 { }
}

class Test
{
    void Method()
    {
    }
}";

        // Hash must be at least 8 characters to trigger the diagnostic
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task UsingDirective_WithLowercaseHash_NoDiagnostic()
    {
        var test =
            @"
using MyApp.LinqraftGenerated_abcd1234;

namespace MyApp.LinqraftGenerated_abcd1234
{
    public class Dto { }
}

class Test
{
    void Method()
    {
    }
}";

        // The pattern requires uppercase letters (matching Linqraft's hash generation)
        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task UsingDirective_WithMultipleGeneratedHashNamespaces_ReportsMultipleDiagnostics()
    {
        var test =
            @"
{|#0:using MyApp.LinqraftGenerated_AAAA1111;|}
{|#1:using MyApp.LinqraftGenerated_BBBB2222;|}

namespace MyApp.LinqraftGenerated_AAAA1111
{
    public class Dto1 { }
}

namespace MyApp.LinqraftGenerated_BBBB2222
{
    public class Dto2 { }
}

class Test
{
    void Method()
    {
    }
}";

        var expected1 = VerifyCS
            .Diagnostic(GeneratedHashedNamespaceUsageAnalyzer.AnalyzerId)
            .WithLocation(0)
            .WithSeverity(DiagnosticSeverity.Warning)
            .WithArguments("LinqraftGenerated_AAAA1111");

        var expected2 = VerifyCS
            .Diagnostic(GeneratedHashedNamespaceUsageAnalyzer.AnalyzerId)
            .WithLocation(1)
            .WithSeverity(DiagnosticSeverity.Warning)
            .WithArguments("LinqraftGenerated_BBBB2222");

        await VerifyCS.VerifyAnalyzerAsync(test, expected1, expected2);
    }
}
