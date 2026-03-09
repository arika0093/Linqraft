using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Linqraft.SourceGenerator;

internal static class GeneratedSourceFormatter
{
    private static readonly CSharpParseOptions ParseOptions = new(
        languageVersion: LanguageVersion.Preview,
        documentationMode: DocumentationMode.Parse
    );

    public static string FormatCompilationUnit(string source)
    {
        var root = CSharpSyntaxTree.ParseText(source, ParseOptions).GetRoot();
        return root.NormalizeWhitespace(eol: "\n", indentation: "    ").ToFullString();
    }
}
