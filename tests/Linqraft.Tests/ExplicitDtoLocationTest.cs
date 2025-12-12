using System;
using System.IO;

namespace Linqraft.Tests;

public class ExplicitDtoLocationTest
{
    [Fact]
    public void Explicit_dto_classes_are_emitted_with_expression_file()
    {
        var projectDir = GetProjectDirectory();
        var generatorDir = Path.Combine(
            projectDir,
            ".generated",
            "Linqraft.SourceGenerator",
            "Linqraft.SelectExprGenerator"
        );

        var expressionFile = Path.Combine(
            generatorDir,
            "GeneratedExpression_Linqraft_Tests_ExplicitDtoComprehensiveTest.g.cs"
        );
        var dtoFile = Path.Combine(generatorDir, "GeneratedDtos.g.cs");

        File.Exists(expressionFile).ShouldBeTrue();

        var expressionCode = File.ReadAllText(expressionFile);
        expressionCode.ShouldContain("partial class SimpleNullableDto");
        expressionCode.ShouldContain("partial class NullConditionalDto");

        if (File.Exists(dtoFile))
        {
            var dtoCode = File.ReadAllText(dtoFile);
            dtoCode.ShouldNotContain("partial class SimpleNullableDto");
            dtoCode.ShouldNotContain("partial class NullConditionalDto");
        }
    }

    private static string GetProjectDirectory()
    {
        var baseDir = AppContext.BaseDirectory;
        return Path.GetFullPath(Path.Combine(baseDir, "..", "..", ".."));
    }
}
