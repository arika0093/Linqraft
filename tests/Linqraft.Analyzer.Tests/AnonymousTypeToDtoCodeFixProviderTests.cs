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
            var result = new ResultDto { Id = 1, Name = ""Test"" };
        }
    }

    public partial class ResultDto
    {
        public required int Id { get; set; }
        public required string Name { get; set; }
    }
}";

        var expected = new DiagnosticResult(
            AnonymousTypeToDtoAnalyzer.AnalyzerId,
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

        var expected = new DiagnosticResult(
            AnonymousTypeToDtoAnalyzer.AnalyzerId,
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
            return new UserDto { Id = 1, Name = ""Test"" };
        }
    }

    public partial class UserDto
    {
        public required int Id { get; set; }
        public required string Name { get; set; }
    }
}";

        var expected = new DiagnosticResult(
            AnonymousTypeToDtoAnalyzer.AnalyzerId,
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

        var expected = new DiagnosticResult(
            AnonymousTypeToDtoAnalyzer.AnalyzerId,
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
        var result = new ResultDto { Id = 1, Name = ""Test"" };
    }
}
public partial class ResultDto
{
    public required int Id { get; set; }
    public required string Name { get; set; }
}";

        var expected = new DiagnosticResult(
            AnonymousTypeToDtoAnalyzer.AnalyzerId,
            DiagnosticSeverity.Hidden
        ).WithLocation(0);

        await RunCodeFixTestAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task CodeFix_NestedAnonymousType_GeneratesNestedDtos()
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

        // Note: Issue #155 changes the naming of nested DTOs to use the property name (Data)
        // instead of the source type name (Channel). So the nested DTO is now named DataDto_*
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
                Data = new DataDto_B0258595
                {
                    Id = channel.Id,
                    Name = channel.Name
                }
            };
        }
    }

    public partial class DataDto_B0258595
    {
        public required int Id { get; set; }
        public required string Name { get; set; }
    }

    public partial class ResultDto
    {
        public required int Id { get; set; }
        public required global::TestNamespace.DataDto_B0258595? Data { get; set; }
    }
}";

        var expected0 = new DiagnosticResult(
            AnonymousTypeToDtoAnalyzer.AnalyzerId,
            DiagnosticSeverity.Hidden
        ).WithLocation(0);

        var expected1 = new DiagnosticResult(
            AnonymousTypeToDtoAnalyzer.AnalyzerId,
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
        var result = new ResultDto { Id = 1, Name = ""Test"" };
    }
}

public partial class ResultDto
{
    public required int Id { get; set; }
    public required string Name { get; set; }
}";

        var expected = new DiagnosticResult(
            AnonymousTypeToDtoAnalyzer.AnalyzerId,
            DiagnosticSeverity.Hidden
        ).WithLocation(0);

        await RunCodeFixTestAsync(test, expected, fixedCode);
    }

    [Fact]
    public async Task CodeFix_GlobalNamespace_WithNestedAnonymousType_GeneratesDtoCorrectly()
    {
        var test =
            @"
class Item
{
    public string Name { get; set; }
    public int Value { get; set; }
}

class Test
{
    void Method()
    {
        var item = new Item();
        var result = {|#0:new
        {
            Id = 1,
            ItemData = {|#1:new
            {
                item.Name,
                item.Value
            }|}
        }|};
    }
}";

        // Note: Issue #155 changes the naming of nested DTOs to use the property name (ItemData)
        // instead of the source type name (Item). So the nested DTO is now named ItemDataDto_*
        var fixedCode =
            @"class Item
{
    public string Name { get; set; }
    public int Value { get; set; }
}

class Test
{
    void Method()
    {
        var item = new Item();
        var result = new ResultDto
        {
            Id = 1,
            ItemData = new ItemDataDto_B1D867F4
            {
                Name = item.Name,
                Value = item.Value
            }
        };
    }
}

public partial class ItemDataDto_B1D867F4
{
    public required string Name { get; set; }
    public required int Value { get; set; }
}


public partial class ResultDto
{
    public required int Id { get; set; }
    public required global::ItemDataDto_B1D867F4? ItemData { get; set; }
}";

        var expected0 = new DiagnosticResult(
            AnonymousTypeToDtoAnalyzer.AnalyzerId,
            DiagnosticSeverity.Hidden
        ).WithLocation(0);

        var expected1 = new DiagnosticResult(
            AnonymousTypeToDtoAnalyzer.AnalyzerId,
            DiagnosticSeverity.Hidden
        ).WithLocation(1);

        await RunCodeFixTestAsync(test, [expected0, expected1], fixedCode);
    }

    [Fact]
    public async Task CodeFix_MultilineAnonymousType_PreservesFormatting()
    {
        var test =
            @"
namespace TestNamespace
{
    class SampleClass
    {
        public int Id { get; set; }
        public string Foo { get; set; }
        public string Bar { get; set; }
    }

    class Test
    {
        void Method()
        {
            var s = new SampleClass();
            var result = {|#0:new
            {
                s.Id,
                s.Foo,
                s.Bar,
            }|};
        }
    }
}";

        var fixedCode =
            @"namespace TestNamespace
{
    class SampleClass
    {
        public int Id { get; set; }
        public string Foo { get; set; }
        public string Bar { get; set; }
    }

    class Test
    {
        void Method()
        {
            var s = new SampleClass();
            var result = new ResultDto
            {
                Id = s.Id,
                Foo = s.Foo,
                Bar = s.Bar,
            };
        }
    }

    public partial class ResultDto
    {
        public required int Id { get; set; }
        public required string Foo { get; set; }
        public required string Bar { get; set; }
    }
}";

        var expected = new DiagnosticResult(
            AnonymousTypeToDtoAnalyzer.AnalyzerId,
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
        // Normalize line endings to LF to avoid CRLF/LF mismatch issues
        static string NormalizeLineEndings(string text) => text.Replace("\r\n", "\n");

        var test = new CSharpCodeFixTest<
            AnonymousTypeToDtoAnalyzer,
            AnonymousTypeToDtoCodeFixProvider,
            DefaultVerifier
        >
        {
            TestCode = NormalizeLineEndings(source),
            FixedCode = NormalizeLineEndings(fixedSource),
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
