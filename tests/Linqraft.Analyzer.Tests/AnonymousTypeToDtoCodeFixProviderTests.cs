using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixVerifier<
    Linqraft.Analyzer.AnonymousTypeToDtoAnalyzer,
    Linqraft.Analyzer.AnonymousTypeToDtoCodeFixProvider,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier
>;

namespace Linqraft.Analyzer.Tests;

public class AnonymousTypeToDtoCodeFixProviderTests
{
    [Fact]
    public async Task CodeFix_SimpleAnonymousType_GeneratesDto()
    {
        var test =
            @"
namespace TestNamespace
{
    class Test
    {
        void Method()
        {
            var result = {|#0:new { Id = 1, Name = ""Test"" }|};
        }
    }
}";

        var fixedCode =
            @"
namespace TestNamespace
{
    class Test
    {
        void Method()
        {
            var result = new ResultDto { Id = 1, Name = ""Test"" };
        }
    }
public partial class ResultDto
{
    public required int Id { get; set; }
    public required string Name { get; set; }
}
}";

        var expected = VerifyCS
            .Diagnostic(AnonymousTypeToDtoAnalyzer.DiagnosticId)
            .WithLocation(0)
            .WithSeverity(DiagnosticSeverity.Info);

        await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task CodeFix_AnonymousTypeWithImplicitProperties_GeneratesDto()
    {
        var test =
            @"
namespace TestNamespace
{
    class Source
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }

    class Test
    {
        void Method()
        {
            var source = new Source { Id = 1, Name = ""Test"" };
            var data = {|#0:new { source.Id, source.Name }|};
        }
    }
}";

        var fixedCode =
            @"
namespace TestNamespace
{
    class Source
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }

    class Test
    {
        void Method()
        {
            var source = new Source { Id = 1, Name = ""Test"" };
            var data = new DataDto { Id = source.Id, Name = source.Name };
        }
    }
public partial class DataDto
{
    public required int Id { get; set; }
    public required string Name { get; set; }
}
}";

        var expected = VerifyCS
            .Diagnostic(AnonymousTypeToDtoAnalyzer.DiagnosticId)
            .WithLocation(0)
            .WithSeverity(DiagnosticSeverity.Info);

        await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task CodeFix_AnonymousTypeInMethodWithGetPrefix_GeneratesDto()
    {
        var test =
            @"
namespace TestNamespace
{
    class Test
    {
        object GetUser()
        {
            return {|#0:new { Id = 1, Name = ""Test"" }|};
        }
    }
}";

        var fixedCode =
            @"
namespace TestNamespace
{
    class Test
    {
        object GetUser()
        {
            return new UserDto { Id = 1, Name = ""Test"" };
        }
    }
public partial class UserDto
{
    public required int Id { get; set; }
    public required string Name { get; set; }
}
}";

        var expected = VerifyCS
            .Diagnostic(AnonymousTypeToDtoAnalyzer.DiagnosticId)
            .WithLocation(0)
            .WithSeverity(DiagnosticSeverity.Info);

        await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task CodeFix_AnonymousTypeWithMixedProperties_GeneratesDto()
    {
        var test =
            @"
namespace TestNamespace
{
    class Test
    {
        void Method()
        {
            var id = 1;
            var result = {|#0:new { Id = id, Name = ""Test"", Active = true }|};
        }
    }
}";

        var fixedCode =
            @"
namespace TestNamespace
{
    class Test
    {
        void Method()
        {
            var id = 1;
            var result = new ResultDto { Id = id, Name = ""Test"", Active = true };
        }
    }
public partial class ResultDto
{
    public required int Id { get; set; }
    public required string Name { get; set; }
    public required bool Active { get; set; }
}
}";

        var expected = VerifyCS
            .Diagnostic(AnonymousTypeToDtoAnalyzer.DiagnosticId)
            .WithLocation(0)
            .WithSeverity(DiagnosticSeverity.Info);

        await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
    }
}
