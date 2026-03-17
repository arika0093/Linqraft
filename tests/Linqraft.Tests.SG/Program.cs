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
    public void Generator_emits_global_using_by_default_and_can_opt_out()
    {
        var defaultDriver = CreateDriver();
        var defaultCompilation = CreateCompilation(
            CreateProjectionTree(),
            CreateMarkerTree("global-using-default")
        );

        defaultDriver = defaultDriver.RunGeneratorsAndUpdateCompilation(
            defaultCompilation,
            out _,
            out var defaultDiagnostics
        );

        defaultDiagnostics.ShouldBeEmpty();

        var defaultGeneratedSources = GetGeneratedSourceMap(defaultDriver.GetRunResult());
        defaultGeneratedSources.TryGetValue(
            "Linqraft.GlobalUsings.g.cs",
            out var defaultGlobalUsing
        );
        defaultGlobalUsing.ShouldNotBeNull();
        defaultGlobalUsing.ShouldContain("global using Linqraft;");

        var optOutDriver = CreateDriver(useGlobalUsing: false);
        var optOutCompilation = CreateCompilation(
            CreateProjectionTree(),
            CreateMarkerTree("global-using-opt-out")
        );

        optOutDriver = optOutDriver.RunGeneratorsAndUpdateCompilation(
            optOutCompilation,
            out _,
            out var optOutDiagnostics
        );

        optOutDiagnostics.ShouldBeEmpty();

        var optOutGeneratedSources = GetGeneratedSourceMap(optOutDriver.GetRunResult());
        optOutGeneratedSources.ContainsKey("Linqraft.GlobalUsings.g.cs").ShouldBeFalse();
    }

    [Test]
    public void Generator_suppresses_duplicate_global_using_warning_when_warnings_are_errors()
    {
        var driver = CreateDriver();
        var compilation = CreateCompilation(
            [
                CreateProjectionTree(),
                CreateMarkerTree("global-using-duplicate"),
                CreateGlobalUsingTree(),
            ],
            treatWarningsAsErrors: true
        );

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
        generatedSources["Linqraft.GlobalUsings.g.cs"]
            .ShouldContain("#pragma warning disable CS8933");
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

    [Test]
    public void Class_level_mapping_requires_mapping_declare_inheritance()
    {
        var driver = CreateDriver();
        var compilation = CreateCompilation(
            CreateMappingDeclarationTree(),
            CreateMarkerTree("class")
        );

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
        generatedSources
            .Keys.Count(key => key.StartsWith("Mapping_", StringComparison.Ordinal))
            .ShouldBe(1);
        generatedSources
            .Values.Any(source =>
                source.Contains("ProjectToValidOrderProjection", StringComparison.Ordinal)
            )
            .ShouldBeTrue();
        generatedSources
            .Values.Any(source =>
                source.Contains("partial class ValidOrderProjectionDto", StringComparison.Ordinal)
            )
            .ShouldBeTrue();
        generatedSources
            .Values.Any(source =>
                source.Contains("IgnoredOrderProjectionDto", StringComparison.Ordinal)
            )
            .ShouldBeFalse();
    }

    [Test]
    public void Mapping_generation_respects_visibility_override_and_indents_multiline_null_ternaries()
    {
        var driver = CreateDriver();
        var compilation = CreateCompilation(
            CreateMappingVisibilityAndFormattingTree(),
            CreateMarkerTree("visibility")
        );

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
        var mappingSource = generatedSources.Values.Single(source =>
            source.Contains("ProjectToPublicOrderRow", StringComparison.Ordinal)
        );

        mappingSource.ShouldContain("public static partial class PublicOrderMapping_");
        mappingSource.ShouldContain(
            "public static global::System.Linq.IQueryable<global::SmokeFixture.PublicOrderRow> ProjectToPublicOrderRow"
        );

        var lines = mappingSource.Replace("\r\n", "\n").Split('\n');
        var propertyIndex = FindLineIndex(
            lines,
            0,
            line =>
                line.Contains("TotalFeeAmount = order.Shipment != null", StringComparison.Ordinal)
        );
        var questionIndex = FindLineIndex(
            lines,
            propertyIndex + 1,
            line => line.TrimStart().StartsWith("? ", StringComparison.Ordinal)
        );
        var shipmentIndex = FindLineIndex(
            lines,
            questionIndex + 1,
            line => line.Contains("global::System.Linq.Enumerable.Sum(", StringComparison.Ordinal)
        );
        var eventsIndex = FindLineIndex(
            lines,
            shipmentIndex + 1,
            line =>
                line.Contains("order.Shipment.Events", StringComparison.Ordinal)
                || line.Trim().StartsWith(".Events", StringComparison.Ordinal)
        );
        var closingIndex = FindLineIndex(
            lines,
            eventsIndex + 1,
            line =>
                line.Trim().Equals(")", StringComparison.Ordinal)
                || line.Trim().Equals("))", StringComparison.Ordinal)
        );
        var nullIndex = FindLineIndex(
            lines,
            closingIndex + 1,
            line => line.TrimStart().StartsWith(": ", StringComparison.Ordinal)
        );

        CountLeadingSpaces(lines[shipmentIndex])
            .ShouldBeGreaterThan(CountLeadingSpaces(lines[questionIndex]));
        CountLeadingSpaces(lines[eventsIndex])
            .ShouldBeGreaterThan(CountLeadingSpaces(lines[shipmentIndex]));
        CountLeadingSpaces(lines[closingIndex])
            .ShouldBeGreaterThanOrEqualTo(CountLeadingSpaces(lines[questionIndex]));
        CountLeadingSpaces(lines[nullIndex]).ShouldBe(CountLeadingSpaces(lines[questionIndex]));
    }

    [Test]
    public void Generator_does_not_intercept_unrelated_linqraftkit_generate_methods()
    {
        var driver = CreateDriver();
        var compilation = CreateCompilation(
            CreateUnrelatedGenerateTree(),
            CreateMarkerTree("generate-identity")
        );

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
        generatedSources
            .Keys.Any(key => key.StartsWith("Generate_", StringComparison.Ordinal))
            .ShouldBeFalse();
    }

    [Test]
    public void Generator_preserves_qualified_external_type_syntax_when_binding_fails()
    {
        var driver = CreateDriver();
        var compilation = CreateCompilation(
            CreateProjectionWithUnresolvedQualifiedExternalTypeTree(),
            CreateMarkerTree("unresolved-qualified-external")
        );

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);

        var generatedSourceText = string.Join(
            "\n",
            GetGeneratedSourceMap(driver.GetRunResult()).Values
        );

        generatedSourceText.ShouldContain(
            "public global::ExternalFixture.ExternalCustomer Customer { get; set; }"
        );
        generatedSourceText.ShouldNotContain("global::SmokeFixture.ExternalCustomer");
    }

    private static GeneratorDriver CreateDriver(
        bool usePrebuildExpression = true,
        bool useGlobalUsing = true
    )
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
        var generator = new LinqraftSourceGenerator().AsSourceGenerator();
        return CSharpGeneratorDriver.Create(
            new[] { generator },
            additionalTexts: Array.Empty<AdditionalText>(),
            parseOptions: parseOptions,
            optionsProvider: new TestAnalyzerConfigOptionsProvider(
                usePrebuildExpression,
                useGlobalUsing
            ),
            driverOptions: new GeneratorDriverOptions(
                IncrementalGeneratorOutputKind.None,
                trackIncrementalGeneratorSteps: true
            )
        );
    }

    private static CSharpCompilation CreateCompilation(
        IReadOnlyList<SyntaxTree> syntaxTrees,
        bool treatWarningsAsErrors = false
    )
    {
        return CSharpCompilation.Create(
            assemblyName: "Linqraft.Tests.SG.SmokeFixture",
            syntaxTrees: syntaxTrees,
            references: GetMetadataReferences(),
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable,
                generalDiagnosticOption: treatWarningsAsErrors
                    ? ReportDiagnostic.Error
                    : ReportDiagnostic.Default
            )
        );
    }

    private static CSharpCompilation CreateCompilation(params SyntaxTree[] syntaxTrees)
    {
        return CreateCompilation((IReadOnlyList<SyntaxTree>)syntaxTrees);
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

    private static SyntaxTree CreateGlobalUsingTree()
    {
        const string source = """
            global using Linqraft;
            """;

        return CSharpSyntaxTree.ParseText(
            SourceText.From(source),
            new CSharpParseOptions(LanguageVersion.Preview),
            path: "ManualGlobalUsing.cs"
        );
    }

    private static SyntaxTree CreateMappingDeclarationTree()
    {
        const string source = """
            using System.Linq;
            using Linqraft;

            namespace SmokeFixture;

            public sealed class SmokeOrder
            {
                public int Id { get; set; }
            }

            [LinqraftMappingGenerate("ProjectToValidOrderProjection")]
            internal sealed class ValidOrderMapping : LinqraftMappingDeclare<SmokeOrder>
            {
                protected override void DefineMapping()
                {
                    Source.SelectExpr<SmokeOrder, ValidOrderProjectionDto>(order => new
                    {
                        order.Id,
                    });
                }
            }

            [LinqraftMappingGenerate("ProjectToIgnoredOrderProjection")]
            internal sealed class IgnoredOrderMapping
            {
                internal IQueryable<IgnoredOrderProjectionDto> DefineMapping(IQueryable<SmokeOrder> source)
                {
                    return source.SelectExpr<SmokeOrder, IgnoredOrderProjectionDto>(order => new
                    {
                        order.Id,
                    });
                }
            }

            public partial class ValidOrderProjectionDto;

            public partial class IgnoredOrderProjectionDto;
            """;

        return CSharpSyntaxTree.ParseText(
            SourceText.From(source),
            new CSharpParseOptions(LanguageVersion.Preview),
            path: "SmokeMappings.cs"
        );
    }

    private static SyntaxTree CreateMappingVisibilityAndFormattingTree()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Linq;
            using Linqraft;

            namespace SmokeFixture;

            public sealed class Fee
            {
                public int Amount { get; set; }
            }

            public sealed class ShipmentEvent
            {
                public List<Fee> Fees { get; set; } = new();
            }

            public sealed class Shipment
            {
                public List<ShipmentEvent> Events { get; set; } = new();
            }

            public sealed class SmokeOrder
            {
                public Shipment? Shipment { get; set; }
            }

            [LinqraftMappingGenerate("ProjectToPublicOrderRow", Visibility = LinqraftMappingVisibility.Public)]
            internal sealed class PublicOrderMapping : LinqraftMappingDeclare<SmokeOrder>
            {
                protected override void DefineMapping()
                {
                    Source.SelectExpr<SmokeOrder, PublicOrderRow>(order => new
                    {
                        TotalFeeAmount = order.Shipment != null
                            ? (int)(
                                order.Shipment
                                    .Events
                                    .Sum(evt => evt.Fees.Sum(fee => fee.Amount))
                            )
                            : (int?)null,
                    });
                }
            }

            public partial class PublicOrderRow;
            """;

        return CSharpSyntaxTree.ParseText(
            SourceText.From(source),
            new CSharpParseOptions(LanguageVersion.Preview),
            path: "SmokeMappings.Visibility.cs"
        );
    }

    private static SyntaxTree CreateUnrelatedGenerateTree()
    {
        const string source = """
            namespace OtherLibrary;

            public static class LinqraftKit
            {
                public static T Generate<T>(object x) => throw new global::System.NotImplementedException();
            }

            public sealed class ExternalDto
            {
                public int Id { get; set; }
            }

            public static class Consumer
            {
                public static ExternalDto Create(int id)
                {
                    return global::OtherLibrary.LinqraftKit.Generate<ExternalDto>(new { Id = id });
                }
            }
            """;

        return CSharpSyntaxTree.ParseText(
            SourceText.From(source),
            new CSharpParseOptions(LanguageVersion.Preview),
            path: "SmokeGenerate.Identity.cs"
        );
    }

    private static SyntaxTree CreateGenerateCaptureTree()
    {
        const string source = """
            using Linqraft;

            namespace SmokeFixture;

            public static class GenerateConsumer
            {
                public static GeneratedCaptureDto Create(int id, string prefix)
                {
                    return LinqraftKit.Generate<GeneratedCaptureDto>(
                        new
                        {
                            Id = id,
                            Label = prefix + id,
                        },
                        capture: () => (id, prefix)
                    );
                }
            }

            public partial class GeneratedCaptureDto;
            """;

        return CSharpSyntaxTree.ParseText(
            SourceText.From(source),
            new CSharpParseOptions(LanguageVersion.Preview),
            path: "SmokeGenerate.Capture.cs"
        );
    }

    private static SyntaxTree CreateProjectionWithUnresolvedQualifiedExternalTypeTree()
    {
        const string source = """
            using System.Linq;
            using Linqraft;

            namespace SmokeFixture;

            public static class Consumer
            {
                public static void Run(IQueryable<UnresolvedQualifiedExternalTypeOrder> orders)
                {
                    _ = orders.SelectExpr<UnresolvedQualifiedExternalTypeOrder, UnresolvedQualifiedExternalTypeOrderDto>(order => new
                    {
                        Customer = new ExternalFixture.ExternalCustomer
                        {
                            Name = order.CustomerName,
                        },
                    });
                }
            }

            public sealed class UnresolvedQualifiedExternalTypeOrder
            {
                public required string CustomerName { get; set; }
            }

            public partial class UnresolvedQualifiedExternalTypeOrderDto;
            """;

        return CSharpSyntaxTree.ParseText(
            SourceText.From(source),
            new CSharpParseOptions(LanguageVersion.Preview),
            path: "SmokeProjection.UnresolvedQualifiedExternalType.cs"
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

    private static int FindLineIndex(
        IReadOnlyList<string> lines,
        int startIndex,
        Func<string, bool> predicate
    )
    {
        for (var index = startIndex; index < lines.Count; index++)
        {
            if (predicate(lines[index]))
            {
                return index;
            }
        }

        throw new InvalidOperationException("Expected generated line was not found.");
    }

    private static int CountLeadingSpaces(string value)
    {
        return value.TakeWhile(character => character == ' ').Count();
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

    private sealed class TestAnalyzerConfigOptionsProvider(
        bool usePrebuildExpression,
        bool useGlobalUsing
    ) : AnalyzerConfigOptionsProvider
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
                ["build_property.LinqraftGlobalUsing"] = useGlobalUsing ? "true" : "false",
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
