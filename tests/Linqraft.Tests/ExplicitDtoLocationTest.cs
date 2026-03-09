using System;
using System.IO;
using System.Linq;

namespace Linqraft.Tests;

public class ExplicitDtoLocationTest
{
    [Test]
    public void Explicit_dto_classes_are_emitted_with_expression_file()
    {
        var projectDir = GetProjectDirectory();
        var generatorDir = Path.Combine(projectDir, ".generated", "Linqraft.SourceGenerator");
        var expressionFiles = Directory.GetFiles(
            generatorDir,
            "SelectExpr_*.g.cs",
            SearchOption.AllDirectories
        );

        var simpleDtoFile = expressionFiles.SingleOrDefault(file =>
            File.ReadAllText(file)
                .Contains("partial class SimpleNullableDto", StringComparison.Ordinal)
        );
        var nullConditionalDtoFile = expressionFiles.SingleOrDefault(file =>
            File.ReadAllText(file)
                .Contains("partial class NullConditionalDto", StringComparison.Ordinal)
        );

        simpleDtoFile.ShouldNotBeNull();
        nullConditionalDtoFile.ShouldNotBeNull();

        Path.GetFileName(simpleDtoFile).ShouldStartWith("SelectExpr_");
        Path.GetFileName(nullConditionalDtoFile).ShouldStartWith("SelectExpr_");

        var dtoFiles = Directory.GetFiles(
            generatorDir,
            "GeneratedDtos.g.cs",
            SearchOption.AllDirectories
        );
        foreach (var dtoFile in dtoFiles)
        {
            var dtoCode = File.ReadAllText(dtoFile);
            dtoCode.ShouldNotContain("partial class SimpleNullableDto");
            dtoCode.ShouldNotContain("partial class NullConditionalDto");
        }
    }

    [Test]
    public void Generated_interceptors_inline_projection_logic()
    {
        var projectDir = GetProjectDirectory();
        var generatorDir = Path.Combine(
            projectDir,
            ".generated",
            "Linqraft.SourceGenerator",
            "Linqraft.SourceGenerator.LinqraftSourceGenerator"
        );
        var supportFile = Path.Combine(generatorDir, "Linqraft.Declarations.g.cs");
        var expressionFiles = Directory.GetFiles(
            generatorDir,
            "SelectExpr_*.g.cs",
            SearchOption.AllDirectories
        );

        File.ReadAllText(supportFile).ShouldNotContain("SelectExprRuntimeHelper");

        expressionFiles.Length.ShouldBeGreaterThan(0);
        foreach (var expressionFile in expressionFiles)
        {
            File.ReadAllText(expressionFile).ShouldNotContain("SelectExprRuntimeHelper");
        }

        expressionFiles
            .Any(file =>
                File.ReadAllText(file).Contains("matchedQuery.Select(", StringComparison.Ordinal)
            )
            .ShouldBeTrue();
    }

    private static string GetProjectDirectory()
    {
        var baseDir = AppContext.BaseDirectory;
        return Path.GetFullPath(Path.Combine(baseDir, "..", "..", ".."));
    }
}
