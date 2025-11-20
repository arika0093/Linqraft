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

    [Fact]
    public async Task CodeFix_NestedAnonymousType_GeneratesDtoWithNestedClass()
    {
        var test =
            @"
namespace TestNamespace
{
    class Channel
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }

    class Test
    {
        void Method()
        {
            var channel = new Channel();
            var result = {|#0:new
            {
                Id = 1,
                Data = {|#1:new
                {
                    channel.Id,
                    channel.Name
                }|}
            }|};
        }
    }
}";

        var fixedCode =
            @"namespace TestNamespace
{
    class Channel
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }

    class Test
    {
        void Method()
        {
            var channel = new Channel();
            var result = new ResultDto
            {
                Id = 1,
                Data = new ChannelDto_97CA5AF3
                {
                    Id = channel.Id,
                    Name = channel.Name
                }
            };
        }
    }

    public partial class ChannelDto_97CA5AF3
    {
        public required int Id { get; set; }
        public required string Name { get; set; }
    }

    public partial class ResultDto
    {
        public required int Id { get; set; }
        public required global::TestNamespace.ChannelDto_97CA5AF3 Data { get; set; }
    }
}";

        var expected0 = new DiagnosticResult(
            AnonymousTypeToDtoAnalyzer.DiagnosticId,
            DiagnosticSeverity.Hidden
        ).WithLocation(0);

        var expected1 = new DiagnosticResult(
            AnonymousTypeToDtoAnalyzer.DiagnosticId,
            DiagnosticSeverity.Hidden
        ).WithLocation(1);

        await RunCodeFixTestAsync(test, [expected0, expected1], fixedCode);
    }

    [Fact]
    public async Task CodeFix_GlobalNamespace_GeneratesDtoWithoutNamespaceDeclaration()
    {
        var test =
            @"
class Test
{
    void Method()
    {
        var result = {|#0:new { Id = 1, Name = ""Test"" }|};
    }
}";

        var fixedCode =
            @"class Test
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

    [Fact]
    public async Task CodeFix_CollectionWithNestedAnonymousType_GeneratesNestedDtos()
    {
        var test =
            @"
using System.Linq;
using System.Collections.Generic;

namespace TestNamespace
{
    class Quote
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }

    class Test
    {
        void Method()
        {
            var quotes = new List<Quote>();
            var result = {|#0:new
            {
                Id = 1,
                Items = quotes.Select(q => new
                {
                    q.Id,
                    SubItem = new { q.Name }
                }).ToList()
            }|};
        }
    }
}";

        var fixedCode =
            @"using System.Linq;
using System.Collections.Generic;
namespace TestNamespace
{
    class Quote
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }

    class Test
    {
        void Method()
        {
            var quotes = new List<Quote>();
            var result = new ResultDto
            {
                Id = 1,
                Items = quotes.Select(q => new QuoteDto_ABCD1234
                {
                    Id = q.Id,
                    SubItem = new QuoteDto_EFGH5678 { Name = q.Name }
                }).ToList()
            };
        }
    }

    public partial class QuoteDto_EFGH5678
    {
        public required string Name { get; set; }
    }

    public partial class QuoteDto_ABCD1234
    {
        public required int Id { get; set; }
        public required global::TestNamespace.QuoteDto_EFGH5678 SubItem { get; set; }
    }

    public partial class ResultDto
    {
        public required int Id { get; set; }
        public required global::System.Collections.Generic.List<global::TestNamespace.QuoteDto_ABCD1234> Items { get; set; }
    }
}";

        var expected = new DiagnosticResult(
            AnonymousTypeToDtoAnalyzer.DiagnosticId,
            DiagnosticSeverity.Hidden
        ).WithLocation(0);

        await RunCodeFixTestAsync(test, expected, fixedCode);
    }

    private static async Task RunCodeFixTestAsync(
        string source,
        DiagnosticResult[] expected,
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

        test.ExpectedDiagnostics.AddRange(expected);

        await test.RunAsync();
    }

    private static async Task RunCodeFixTestAsync(
        string source,
        DiagnosticResult expected,
        string fixedSource
    ) => await RunCodeFixTestAsync(source, [expected], fixedSource);
}
