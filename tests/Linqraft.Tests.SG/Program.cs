using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using Linqraft.SourceGenerator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

public sealed class SourceGeneratorSmokeTests
{
    [Test]
    public void Generator_emits_compilable_sources_for_multiple_projection_shapes()
    {
        var driver = CreateDriver();
        var compilation = CreateCompilation(CreateProjectionTree(), CreateMarkerTree("initial"));

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var outputCompilation,
            out var diagnostics
        );

        diagnostics.ShouldBeEmpty();
        outputCompilation
            .GetDiagnostics()
            .Where(diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error && diagnostic.Id != "CS9137"
            )
            .ShouldBeEmpty();

        var generatedSources = GetGeneratedSourceMap(driver.GetRunResult());
        generatedSources.ShouldNotBeEmpty();
        generatedSources
            .Keys.Any(path => path.EndsWith("Linqraft.Declarations.g.cs", StringComparison.Ordinal))
            .ShouldBeTrue();
        var projectionSources = generatedSources
            .Where(pair => pair.Key.Contains("SelectExpr_", StringComparison.Ordinal))
            .ToArray();
        projectionSources.Length.ShouldBe(2);
        projectionSources
            .Any(source =>
                source.Value.Contains(
                    "partial class SmokeOrderSummaryDto",
                    StringComparison.Ordinal
                )
            )
            .ShouldBeTrue();
        projectionSources
            .Any(source =>
                source.Value.Contains("partial class SmokeOrderTotalsDto", StringComparison.Ordinal)
            )
            .ShouldBeTrue();
        generatedSources
            .Where(pair => !pair.Key.Contains("SelectExpr_", StringComparison.Ordinal))
            .Any(pair =>
                pair.Value.Contains("partial class SmokeOrderSummaryDto", StringComparison.Ordinal)
                || pair.Value.Contains(
                    "partial class SmokeOrderTotalsDto",
                    StringComparison.Ordinal
                )
            )
            .ShouldBeFalse();
    }

    [Test]
    public void Generator_keeps_prebuilt_expression_generation_opt_in()
    {
        var driver = CreateDriver(usePrebuildExpression: false);
        var compilation = CreateCompilation(CreateProjectionTree(), CreateMarkerTree("default"));

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var outputCompilation,
            out var diagnostics
        );

        diagnostics.ShouldBeEmpty();
        outputCompilation
            .GetDiagnostics()
            .Where(diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error && diagnostic.Id != "CS9137"
            )
            .ShouldBeEmpty();

        var generatedSources = GetGeneratedSourceMap(driver.GetRunResult());
        generatedSources.ShouldNotBeEmpty();
        generatedSources
            .Values.Any(source =>
                source.Contains(
                    "private static readonly global::System.Linq.Expressions.Expression",
                    StringComparison.Ordinal
                )
            )
            .ShouldBeFalse();
    }

    [Test]
    public void Unrelated_syntax_changes_reuse_cached_incremental_outputs()
    {
        var driver = CreateDriver();
        var projectionTree = CreateProjectionTree();
        var originalMarkerTree = CreateMarkerTree("initial");
        var originalCompilation = CreateCompilation(projectionTree, originalMarkerTree);

        driver = driver.RunGeneratorsAndUpdateCompilation(
            originalCompilation,
            out var originalOutputCompilation,
            out var originalDiagnostics
        );

        originalDiagnostics.ShouldBeEmpty();
        originalOutputCompilation
            .GetDiagnostics()
            .Where(diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error && diagnostic.Id != "CS9137"
            )
            .ShouldBeEmpty();

        var firstGeneratedSources = GetGeneratedSourceMap(driver.GetRunResult());

        var updatedCompilation = originalCompilation.ReplaceSyntaxTree(
            originalMarkerTree,
            CreateMarkerTree(Guid.NewGuid().ToString("N"))
        );

        driver = driver.RunGeneratorsAndUpdateCompilation(
            updatedCompilation,
            out var updatedOutputCompilation,
            out var updatedDiagnostics
        );

        updatedDiagnostics.ShouldBeEmpty();
        updatedOutputCompilation
            .GetDiagnostics()
            .Where(diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error && diagnostic.Id != "CS9137"
            )
            .ShouldBeEmpty();

        var secondRunResult = driver.GetRunResult();
        var secondGeneratedSources = GetGeneratedSourceMap(secondRunResult);
        secondGeneratedSources.Count.ShouldBe(firstGeneratedSources.Count);
        foreach (
            var entry in firstGeneratedSources.OrderBy(pair => pair.Key, StringComparer.Ordinal)
        )
        {
            secondGeneratedSources.ContainsKey(entry.Key).ShouldBeTrue();
            secondGeneratedSources[entry.Key].ShouldBe(entry.Value);
        }

        var trackedStepReasons = secondRunResult
            .Results.Single()
            .TrackedSteps.SelectMany(pair => pair.Value)
            .SelectMany(step => step.Outputs, (step, output) => output.Item2)
            .ToArray();
        var trackedStepReasonSummary = string.Join(
            ", ",
            trackedStepReasons.Select(reason => reason.ToString())
        );

        trackedStepReasons.ShouldNotBeEmpty();
        trackedStepReasons
            .Any(reason =>
                reason == IncrementalStepRunReason.Cached
                || reason == IncrementalStepRunReason.Unchanged
            )
            .ShouldBeTrue(trackedStepReasonSummary);
    }

    private static GeneratorDriver CreateDriver(bool usePrebuildExpression = true)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
        var generator = new LinqraftSourceGenerator().AsSourceGenerator();
        return CSharpGeneratorDriver.Create(
            new[] { generator },
            additionalTexts: Array.Empty<AdditionalText>(),
            parseOptions: parseOptions,
            optionsProvider: new TestAnalyzerConfigOptionsProvider(usePrebuildExpression),
            driverOptions: new GeneratorDriverOptions(
                IncrementalGeneratorOutputKind.None,
                trackIncrementalGeneratorSteps: true
            )
        );
    }

    private static CSharpCompilation CreateCompilation(params SyntaxTree[] syntaxTrees)
    {
        return CSharpCompilation.Create(
            assemblyName: "Linqraft.Tests.SG.SmokeFixture",
            syntaxTrees: syntaxTrees,
            references: GetMetadataReferences(),
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable
            )
        );
    }

    private static SyntaxTree CreateProjectionTree()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Linq;
            using Linqraft;

            namespace SmokeFixture;

            public static class SmokeQueries
            {
                public static void Run()
                {
                    var orders = new[]
                    {
                        new SmokeOrder
                        {
                            Id = 1,
                            Customer = new SmokeCustomer { Name = "Ada" },
                            Items = [new SmokeOrderItem { Quantity = 2 }],
                        },
                        new SmokeOrder
                        {
                            Id = 2,
                            Customer = new SmokeCustomer { Name = "Grace" },
                            Items = [],
                        },
                    }.AsQueryable();

                    var summaries = orders.SelectExpr<SmokeOrder, SmokeOrderSummaryDto>(order => new
                    {
                        order.Id,
                        CustomerName = order.Customer.Name,
                    });

                    var totals = orders.SelectExpr<SmokeOrder, SmokeOrderTotalsDto>(order => new
                    {
                        order.Id,
                        ItemCount = order.Items.Count,
                        TotalQuantity = order.Items.Sum(item => item.Quantity),
                    });
                }
            }

            public sealed class SmokeOrder
            {
                public int Id { get; set; }

                public required SmokeCustomer Customer { get; set; }

                public required List<SmokeOrderItem> Items { get; set; }
            }

            public sealed class SmokeCustomer
            {
                public required string Name { get; set; }
            }

            public sealed class SmokeOrderItem
            {
                public int Quantity { get; set; }
            }

            public partial class SmokeOrderSummaryDto;

            public partial class SmokeOrderTotalsDto;
            """;

        return CSharpSyntaxTree.ParseText(
            SourceText.From(source),
            new CSharpParseOptions(LanguageVersion.Preview),
            path: "SmokeProjection.cs"
        );
    }

    private static SyntaxTree CreateMarkerTree(string value)
    {
        var source = $$"""
            namespace SmokeFixture;

            internal static class BuildMarker
            {
                public const string Value = "{{value}}";
            }
            """;

        return CSharpSyntaxTree.ParseText(
            SourceText.From(source),
            new CSharpParseOptions(LanguageVersion.Preview),
            path: "BuildMarker.cs"
        );
    }

    private static Dictionary<string, string> GetGeneratedSourceMap(
        GeneratorDriverRunResult runResult
    )
    {
        return runResult
            .Results.Single()
            .GeneratedSources.OrderBy(source => source.HintName, StringComparer.Ordinal)
            .ToDictionary(
                source => source.HintName,
                source => source.SourceText.ToString(),
                StringComparer.Ordinal
            );
    }

    private static IEnumerable<MetadataReference> GetMetadataReferences()
    {
        var explicitAssemblies = new List<Assembly>
        {
            typeof(object).Assembly,
            typeof(Enumerable).Assembly,
            typeof(Queryable).Assembly,
            typeof(List<>).Assembly,
            typeof(System.Runtime.CompilerServices.DynamicAttribute).Assembly,
        };

        foreach (
            var assemblyName in new[]
            {
                "System.Runtime",
                "netstandard",
                "System.Collections",
                "System.Linq",
            }
        )
        {
            try
            {
                explicitAssemblies.Add(System.Reflection.Assembly.Load(assemblyName));
            }
            catch
            {
                // Best-effort load for compilation metadata.
            }
        }

        return AppDomain
            .CurrentDomain.GetAssemblies()
            .Concat(explicitAssemblies)
            .Where(assembly => !assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
            .Where(assembly =>
            {
                var name = assembly.GetName().Name ?? string.Empty;
                return !name.StartsWith("Linqraft", StringComparison.Ordinal);
            })
            .Select(assembly => MetadataReference.CreateFromFile(assembly.Location))
            .DistinctBy(reference => reference.Display, StringComparer.Ordinal);
    }

    private sealed class TestAnalyzerConfigOptionsProvider(bool usePrebuildExpression)
        : AnalyzerConfigOptionsProvider
    {
        private readonly AnalyzerConfigOptions _empty = new TestAnalyzerConfigOptions(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        );
        private readonly AnalyzerConfigOptions _globalOptions = new TestAnalyzerConfigOptions(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["build_property.RootNamespace"] = "SmokeFixture",
                ["build_property.InterceptorsNamespaces"] = "Linqraft",
                ["build_property.InterceptorsPreviewNamespaces"] = "Linqraft",
                ["build_property.LinqraftCommentOutput"] = "None",
                ["build_property.LinqraftHasRequired"] = "false",
                ["build_property.LinqraftUsePrebuildExpression"] = usePrebuildExpression
                    ? "true"
                    : "false",
            }
        );

        public override AnalyzerConfigOptions GlobalOptions => _globalOptions;

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree)
        {
            return _empty;
        }

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile)
        {
            return _empty;
        }
    }

    private sealed class TestAnalyzerConfigOptions(IReadOnlyDictionary<string, string> values)
        : AnalyzerConfigOptions
    {
        public override bool TryGetValue(string key, out string value)
        {
            if (values.TryGetValue(key, out var match))
            {
                value = match;
                return true;
            }

            value = string.Empty;
            return false;
        }
    }
}
