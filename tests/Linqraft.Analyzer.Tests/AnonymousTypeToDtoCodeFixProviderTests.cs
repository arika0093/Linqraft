using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;

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
            @"namespace TestNamespace
{
    class Test
    {
        void Method()
        {
            var result = new ResultDto
            {
                Id = 1,
                Name = ""Test""
            };
        }
    }

    public partial class ResultDto
    {
        public required int Id { get; set; }
        public required string Name { get; set; }
    }
}";

        var expected = new DiagnosticResult(
            AnonymousTypeToDtoAnalyzer.DiagnosticId,
            DiagnosticSeverity.Hidden
        ).WithLocation(0);

        await RunCodeFixTestAsync(test, expected, fixedCode);
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
            @"namespace TestNamespace
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
            var source = new Source
            {
                Id = 1,
                Name = ""Test""
            };
            var data = new DataDto
            {
                Id = source.Id,
                Name = source.Name
            };
        }
    }

    public partial class DataDto
    {
        public required int Id { get; set; }
        public required string Name { get; set; }
    }
}";

        var expected = new DiagnosticResult(
            AnonymousTypeToDtoAnalyzer.DiagnosticId,
            DiagnosticSeverity.Hidden
        ).WithLocation(0);

        await RunCodeFixTestAsync(test, expected, fixedCode);
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
            @"namespace TestNamespace
{
    class Test
    {
        object GetUser()
        {
            return new UserDto
            {
                Id = 1,
                Name = ""Test""
            };
        }
    }

    public partial class UserDto
    {
        public required int Id { get; set; }
        public required string Name { get; set; }
    }
}";

        var expected = new DiagnosticResult(
            AnonymousTypeToDtoAnalyzer.DiagnosticId,
            DiagnosticSeverity.Hidden
        ).WithLocation(0);

        await RunCodeFixTestAsync(test, expected, fixedCode);
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
            @"namespace TestNamespace
{
    class Test
    {
        void Method()
        {
            var id = 1;
            var result = new ResultDto
            {
                Id = id,
                Name = ""Test"",
                Active = true
            };
        }
    }

    public partial class ResultDto
    {
        public required int Id { get; set; }
        public required string Name { get; set; }
        public required bool Active { get; set; }
    }
}";

        var expected = new DiagnosticResult(
            AnonymousTypeToDtoAnalyzer.DiagnosticId,
            DiagnosticSeverity.Hidden
        ).WithLocation(0);

        await RunCodeFixTestAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task CodeFix_FileScopedNamespace_GeneratesDtoWithoutIndent()
    {
        var test =
            @"
namespace TestNamespace;

class Test
{
    void Method()
    {
        var result = {|#0:new { Id = 1, Name = ""Test"" }|};
    }
}";

        var fixedCode =
            @"namespace TestNamespace;
class Test
{
    void Method()
    {
        var result = new ResultDto
        {
            Id = 1,
            Name = ""Test""
        };
    }
}

public partial class ResultDto
{
    public required int Id { get; set; }
    public required string Name { get; set; }
}";

        var expected = new DiagnosticResult(
            AnonymousTypeToDtoAnalyzer.DiagnosticId,
            DiagnosticSeverity.Hidden
        ).WithLocation(0);

        await RunCodeFixTestAsync(test, expected, fixedCode);
    }

    private static async Task RunCodeFixTestAsync(
        string source,
        DiagnosticResult expected,
        string fixedSource
    )
    {
        var test = new CSharpCodeFixTest<
            AnonymousTypeToDtoAnalyzer,
            AnonymousTypeToDtoCodeFixProvider,
            DefaultVerifier
        >
        {
            TestCode = source,
            FixedCode = fixedSource,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };

        test.ExpectedDiagnostics.Add(expected);

        await test.RunAsync();
    }
}
