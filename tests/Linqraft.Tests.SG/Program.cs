using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using Linqraft.Core;
using Linqraft.Core.Configuration;
using Linqraft.Generator;
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
    public void Generator_emits_projection_interceptors_in_file_local_classes()
    {
        var driver = CreateDriver();
        var compilation = CreateCompilation(
            CreateProjectionTree(),
            CreateMarkerTree("file-local-interceptor")
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
        var interceptorSource = generatedSources.Values.First(source =>
            source.Contains(
                "[global::System.Runtime.CompilerServices.InterceptsLocationAttribute",
                StringComparison.Ordinal
            )
            && source.Contains("SelectExpr_", StringComparison.Ordinal)
            && source.Contains("SmokeOrderSummaryDto", StringComparison.Ordinal)
        );

        interceptorSource.ShouldContain("file static partial class SelectExprInterceptExtensions");
        interceptorSource.ShouldNotContain("internal static partial class SelectExprExtensions");
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
    public void Generator_emits_default_projection_hook_support_declarations()
    {
        var driver = CreateDriver();
        var compilation = CreateCompilation(
            CreateHookProjectionTree(),
            CreateMarkerTree("default-hooks")
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
        generatedSources["Linqraft.Declarations.g.cs"]
            .ShouldContain("internal partial interface IProjectionHelper");
        generatedSources["Linqraft.Declarations.g.cs"].ShouldContain("T AsLeftJoin<T>(T? value);");
        generatedSources["Linqraft.Declarations.g.cs"].ShouldContain("T AsInnerJoin<T>(T? value);");
        generatedSources["Linqraft.Declarations.g.cs"].ShouldContain("T AsInline<T>(T? value);");
        generatedSources["Linqraft.Declarations.g.cs"]
            .ShouldContain("TResult AsProjection<TResult>(object? value);");
        generatedSources["Linqraft.Declarations.g.cs"]
            .ShouldContain("object AsProjection(object? value);");
        generatedSources["Linqraft.Declarations.g.cs"]
            .ShouldContain("internal partial interface IProjectedValue<T>");
        generatedSources["Linqraft.Declarations.g.cs"]
            .ShouldContain("TResult Select<TResult>(global::System.Func<T, TResult> selector);");
        generatedSources["Linqraft.Declarations.g.cs"]
            .ShouldContain("IProjectedValue<T> Project<T>(T? value);");
        generatedSources["Linqraft.Declarations.g.cs"]
            .ShouldContain(
                "public static global::System.Linq.IQueryable<TResult> SelectExpr<TIn, TResult>(this global::System.Linq.IQueryable<TIn> query, global::System.Func<TIn, global::Linqraft.IProjectionHelper, TResult> selector) where TIn : class"
            );
    }

    [Test]
    public void Generator_rewrites_inner_join_projection_and_project_helpers()
    {
        var driver = CreateDriver();
        var compilation = CreateCompilation(
            CreateHookProjectionTree(),
            CreateMarkerTree("hook-rewrites")
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
        var projectionSource = generatedSources.Values.Single(source =>
            source.Contains("SelectExpr_", StringComparison.Ordinal)
            && source.Contains("HookDto", StringComparison.Ordinal)
            && source.Contains("RequiredChildName", StringComparison.Ordinal)
            && source.Contains("SelectedChild", StringComparison.Ordinal)
        );

        projectionSource.ShouldContain(".Where(");
        projectionSource.ShouldContain("x.Child! != null");
        projectionSource.ShouldContain(
            "ProjectedChild = new global::HookFixture.HookProjectedChildDto"
        );
        projectionSource.ShouldContain("SelectedChild = new global::HookFixture.HookChildDto");
    }

    [Test]
    public void Generator_core_allows_overriding_linqraft_specific_options()
    {
        var driver = CreateDriver(
            new CustomGenerator(),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["build_property.RootNamespace"] = "CustomFixture",
                ["build_property.InterceptorsNamespaces"] = "CustomSupport",
                ["build_property.InterceptorsPreviewNamespaces"] = "CustomSupport",
                ["build_property.CustomCommentOutput"] = "None",
                ["build_property.CustomHasRequired"] = "false",
                ["build_property.CustomUsePrebuildExpression"] = "true",
                ["build_property.CustomGlobalUsing"] = "true",
            }
        );
        var compilation = CreateCompilation(
            CreateCustomProjectionTree(),
            CreateMarkerTree("custom-options")
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
        generatedSources.ContainsKey("Custom.Declarations.g.cs").ShouldBeTrue();
        generatedSources.ContainsKey("Custom.GlobalUsings.g.cs").ShouldBeTrue();
        generatedSources
            .Keys.Any(key => key.StartsWith("CustomMapping_", StringComparison.Ordinal))
            .ShouldBeTrue();
        generatedSources
            .Keys.Any(key => key.StartsWith("CustomGenerate_", StringComparison.Ordinal))
            .ShouldBeTrue();
        generatedSources["Custom.Declarations.g.cs"].ShouldContain("Generated by custom test");
        generatedSources["Custom.Declarations.g.cs"].ShouldContain("namespace CustomSupport");
        generatedSources["Custom.Declarations.g.cs"]
            .ShouldContain("internal sealed partial class CustomMappingGenerateAttribute");
        generatedSources["Custom.Declarations.g.cs"]
            .ShouldContain("internal abstract partial class CustomMappingDeclare<T>");
        generatedSources["Custom.Declarations.g.cs"]
            .ShouldContain("internal static partial class CustomKit");
        generatedSources["Custom.Declarations.g.cs"]
            .ShouldContain("internal static partial class ProjectExprExtensions");
        generatedSources["Custom.GlobalUsings.g.cs"].ShouldContain("global using CustomSupport;");
    }

    [Test]
    public void Generator_core_allows_overriding_projection_hooks()
    {
        var driver = CreateDriver(
            new CustomHookGenerator(),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["build_property.RootNamespace"] = "CustomHookFixture",
                ["build_property.InterceptorsNamespaces"] = "CustomHookSupport",
                ["build_property.InterceptorsPreviewNamespaces"] = "CustomHookSupport",
            }
        );
        var compilation = CreateCompilation(
            CreateCustomHookProjectionTree(),
            CreateMarkerTree("custom-hooks")
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
        generatedSources["CustomHook.Declarations.g.cs"]
            .ShouldContain("internal partial interface IProjectionHelper");
        generatedSources["CustomHook.Declarations.g.cs"]
            .ShouldContain("T InlineProjectable<T>(T? value);");
        generatedSources
            .Where(pair => pair.Key.StartsWith("ProjectExpr_", StringComparison.Ordinal))
            .Select(pair => pair.Value)
            .ShouldContain(source =>
                !source.Contains("InlineProjectable(", StringComparison.Ordinal)
            );
    }

    [Test]
    public void Generator_reports_recursive_asprojectable_cycle()
    {
        var driver = CreateDriver();
        var compilation = CreateCompilation(
            CreateRecursiveProjectableProjectionTree(),
            CreateMarkerTree("recursive-projectable")
        );

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var diagnostics);

        diagnostics
            .Select(diagnostic => diagnostic.Id)
            .Any(id => id is "CS8784" or "CS8785")
            .ShouldBeTrue();
        diagnostics
            .Select(diagnostic => diagnostic.GetMessage())
            .Any(message =>
                message.Contains(
                    "Detected recursive projectable helper expansion",
                    StringComparison.Ordinal
                )
            )
            .ShouldBeTrue();
    }

    [Test]
    public void Generator_reports_duplicate_projection_hook_names()
    {
        var driver = CreateDriver(
            new DuplicateHookGenerator(),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["build_property.RootNamespace"] = "DuplicateHookFixture",
                ["build_property.InterceptorsNamespaces"] = "DuplicateHookSupport",
                ["build_property.InterceptorsPreviewNamespaces"] = "DuplicateHookSupport",
            }
        );
        var compilation = CreateCompilation(
            CreateDuplicateHookProjectionTree(),
            CreateMarkerTree("duplicate-hooks")
        );

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var diagnostics);

        diagnostics
            .Select(diagnostic => diagnostic.Id)
            .Any(id => id is "CS8784" or "CS8785")
            .ShouldBeTrue();
        diagnostics
            .Select(diagnostic => diagnostic.GetMessage())
            .Any(message =>
                message.Contains(
                    "ProjectionHooks contains duplicate method name(s): InlineProjectable.",
                    StringComparison.Ordinal
                )
            )
            .ShouldBeTrue();
    }

    [Test]
    public void Generator_core_omits_optional_linqraft_specific_support_when_names_are_null()
    {
        var driver = CreateDriver(
            new OmittedSupportGenerator(),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["build_property.RootNamespace"] = "MinimalFixture",
                ["build_property.InterceptorsNamespaces"] = "MinimalSupport",
                ["build_property.InterceptorsPreviewNamespaces"] = "MinimalSupport",
            }
        );
        var compilation = CreateCompilation(
            CreateMinimalProjectionTree(),
            CreateMarkerTree("omit-support")
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
        generatedSources.ContainsKey("Minimal.Declarations.g.cs").ShouldBeTrue();
        generatedSources.ContainsKey("Minimal.GlobalUsings.g.cs").ShouldBeTrue();
        generatedSources["Minimal.Declarations.g.cs"]
            .ShouldContain("internal static partial class ProjectExprExtensions");
        generatedSources["Minimal.Declarations.g.cs"]
            .ShouldNotContain("MinimalMappingGenerateAttribute");
        generatedSources["Minimal.Declarations.g.cs"].ShouldNotContain("MinimalMappingDeclare");
        generatedSources["Minimal.Declarations.g.cs"].ShouldNotContain("MinimalKit");
        generatedSources["Minimal.Declarations.g.cs"]
            .ShouldNotContain("MinimalAutoGeneratedDtoAttribute");
        generatedSources["Minimal.GlobalUsings.g.cs"].ShouldContain("global using MinimalSupport;");
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
        return CreateDriver(
            new LinqraftGenerator(),
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
    }

    private static GeneratorDriver CreateDriver(
        IIncrementalGenerator incrementalGenerator,
        IReadOnlyDictionary<string, string> globalOptions
    )
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
        var generator = incrementalGenerator.AsSourceGenerator();
        return CSharpGeneratorDriver.Create(
            new[] { generator },
            additionalTexts: Array.Empty<AdditionalText>(),
            parseOptions: parseOptions,
            optionsProvider: new TestAnalyzerConfigOptionsProvider(globalOptions),
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

    private static SyntaxTree CreateCustomProjectionTree()
    {
        const string source = """
            using System.Linq;
            using CustomSupport;

            namespace CustomFixture;

            public sealed class CustomEntity
            {
                public int Id { get; set; }
            }

            public partial class CustomDto;

            public static class CustomQueries
            {
                public static IQueryable<CustomDto> Run(IQueryable<CustomEntity> query)
                    => query.ProjectExpr(x => new CustomDto { Id = x.Id });

                public static CustomDto Create()
                    => CustomKit.Build<CustomDto>(new { Id = 1 });
            }

            [CustomMappingGenerate]
            internal sealed class CustomEntityMapping : CustomMappingDeclare<CustomEntity>
            {
                protected override void DefineMapping()
                {
                    _ = Source.ProjectExpr(x => new CustomDto { Id = x.Id });
                }
            }
            """;

        return CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(LanguageVersion.Preview),
            path: "CustomProjection.cs"
        );
    }

    private static SyntaxTree CreateHookProjectionTree()
    {
        const string source = """
            using System.Linq;
            using Linqraft;

            namespace HookFixture;

            public sealed class HookChild
            {
                public string? Name { get; set; }
            }

            public sealed class HookEntity
            {
                public HookChild? Child { get; set; }
            }

            public partial class HookDto;
            public partial class HookProjectedChildDto;

            public static class HookQueries
            {
                public static IQueryable<HookDto> Run(IQueryable<HookEntity> query)
                    => query.SelectExpr<HookEntity, HookDto>((x, helper) => new
                    {
                        ChildName = helper.AsLeftJoin(x.Child).Name,
                        RequiredChildName = helper.AsInnerJoin(x.Child!).Name,
                        ProjectedChild = helper.AsProjection<HookProjectedChildDto>(x.Child!),
                        SelectedChild = helper.Project<HookChild>(x.Child!).Select(child => new
                        {
                            child.Name,
                        }),
                    });
            }
            """;

        return CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(LanguageVersion.Preview),
            path: "HookProjection.cs"
        );
    }

    private static SyntaxTree CreateMinimalProjectionTree()
    {
        const string source = """
            using System.Linq;
            using MinimalSupport;

            namespace MinimalFixture;

            public sealed class MinimalEntity
            {
                public int Id { get; set; }
            }

            public static class MinimalQueries
            {
                public static IQueryable<int> Run(IQueryable<MinimalEntity> query)
                    => query.ProjectExpr(x => x.Id);
            }
            """;

        return CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(LanguageVersion.Preview),
            path: "MinimalProjection.cs"
        );
    }

    private static SyntaxTree CreateCustomHookProjectionTree()
    {
        const string source = """
            using System.Linq;
            using CustomHookSupport;

            namespace CustomHookFixture;

            public sealed class CustomHookEntity
            {
                public int Value { get; set; }

                public int Computed => this.Value + 1;
            }

            public partial class CustomHookDto;

            public static class CustomHookQueries
            {
                public static IQueryable<CustomHookDto> Run(IQueryable<CustomHookEntity> query)
                    => query.ProjectExpr<CustomHookEntity, CustomHookDto>((x, helper) => new
                    {
                        Value = helper.InlineProjectable(x.Computed),
                    });
            }
            """;

        return CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(LanguageVersion.Preview),
            path: "CustomHookProjection.cs"
        );
    }

    private static SyntaxTree CreateRecursiveProjectableProjectionTree()
    {
        const string source = """
            using System.Linq;
            using Linqraft;

            namespace RecursiveProjectableFixture;

            public sealed class RecursiveEntity
            {
                public int Value { get; set; }

                public int Recursive(IProjectionHelper helper) => helper.AsInline(Recursive(helper));
            }

            public partial class RecursiveDto;

            public static class RecursiveQueries
            {
                public static IQueryable<RecursiveDto> Run(IQueryable<RecursiveEntity> query)
                    => query.SelectExpr<RecursiveEntity, RecursiveDto>((x, helper) => new
                    {
                        Value = helper.AsInline(x.Recursive(helper)),
                    });
            }
            """;

        return CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(LanguageVersion.Preview),
            path: "RecursiveProjectableProjection.cs"
        );
    }

    private static SyntaxTree CreateDuplicateHookProjectionTree()
    {
        const string source = """
            using System.Linq;
            using DuplicateHookSupport;

            namespace DuplicateHookFixture;

            public sealed class DuplicateHookEntity
            {
                public int Value { get; set; }
            }

            public partial class DuplicateHookDto;

            public static class DuplicateHookQueries
            {
                public static IQueryable<DuplicateHookDto> Run(IQueryable<DuplicateHookEntity> query)
                    => query.ProjectExpr<DuplicateHookEntity, DuplicateHookDto>(x => new
                    {
                        Value = x.Value,
                    });
            }
            """;

        return CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(LanguageVersion.Preview),
            path: "DuplicateHookProjection.cs"
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
        IReadOnlyDictionary<string, string> globalOptions
    ) : AnalyzerConfigOptionsProvider
    {
        private readonly AnalyzerConfigOptions _empty = new TestAnalyzerConfigOptions(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        );
        private readonly AnalyzerConfigOptions _globalOptions = new TestAnalyzerConfigOptions(
            globalOptions
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

    private sealed class CustomGenerator : LinqraftGeneratorCore<CustomGeneratorOptions>;

    private sealed class CustomHookGenerator : LinqraftGeneratorCore<CustomHookGeneratorOptions>;

    private sealed class DuplicateHookGenerator
        : LinqraftGeneratorCore<DuplicateHookGeneratorOptions>;

    private sealed class CustomGeneratorOptions : LinqraftGeneratorOptionsCore
    {
        public override string GeneratorDisplayName => "CustomTest";

        public override string GeneratedHeaderComment => "Generated by custom test";

        public override string SupportNamespace => "CustomSupport";

        public override string GlobalUsingNamespace => "CustomSupport";

        public override string DeclarationSourceHintName => "Custom.Declarations.g.cs";

        public override string GlobalUsingsSourceHintName => "Custom.GlobalUsings.g.cs";

        public override string? MappingGenerateAttributeName => "CustomMappingGenerateAttribute";

        public override string? MappingDeclareClassName => "CustomMappingDeclare";

        public override string? GeneratorKitClassName => "CustomKit";

        public override string ObjectGenerationMethodName => "Build";

        public override string SelectExprMethodName => "ProjectExpr";

        public override string GlobalUsingPropertyName => "build_property.CustomGlobalUsing";

        public override string UsePrebuildExpressionPropertyName =>
            "build_property.CustomUsePrebuildExpression";

        public override string HasRequiredPropertyName => "build_property.CustomHasRequired";

        public override string CommentOutputPropertyName => "build_property.CustomCommentOutput";

        public override string MappingHintNamePrefix => "CustomMapping";

        public override string ObjectGenerationHintNamePrefix => "CustomGenerate";
    }

    private sealed class CustomHookGeneratorOptions : LinqraftGeneratorOptionsCore
    {
        public override string GeneratorDisplayName => "CustomHookTest";

        public override string SupportNamespace => "CustomHookSupport";

        public override string GlobalUsingNamespace => "CustomHookSupport";

        public override string DeclarationSourceHintName => "CustomHook.Declarations.g.cs";

        public override string GlobalUsingsSourceHintName => "CustomHook.GlobalUsings.g.cs";

        public override string SelectExprMethodName => "ProjectExpr";

        public override IReadOnlyList<LinqraftProjectionHookDefinition> ProjectionHooks =>
            [
                new(
                    "InlineProjectable",
                    LinqraftProjectionHookKind.Projectable,
                    "CustomProjectionHooks"
                ),
            ];
    }

    private sealed class DuplicateHookGeneratorOptions : LinqraftGeneratorOptionsCore
    {
        public override string SupportNamespace => "DuplicateHookSupport";

        public override string GlobalUsingNamespace => "DuplicateHookSupport";

        public override string DeclarationSourceHintName => "DuplicateHook.Declarations.g.cs";

        public override string GlobalUsingsSourceHintName => "DuplicateHook.GlobalUsings.g.cs";

        public override string SelectExprMethodName => "ProjectExpr";

        public override IReadOnlyList<LinqraftProjectionHookDefinition> ProjectionHooks =>
            [
                new(
                    "InlineProjectable",
                    LinqraftProjectionHookKind.Projectable,
                    "DuplicateProjectionHooks"
                ),
                new(
                    "InlineProjectable",
                    LinqraftProjectionHookKind.LeftJoin,
                    "DuplicateProjectionHooks2"
                ),
            ];
    }

    private sealed class OmittedSupportGenerator : LinqraftGeneratorCore<OmittedSupportOptions>;

    private sealed class OmittedSupportOptions : LinqraftGeneratorOptionsCore
    {
        public override string GeneratorDisplayName => "MinimalTest";

        public override string GeneratedHeaderComment => "Generated by minimal test";

        public override string SupportNamespace => "MinimalSupport";

        public override string GlobalUsingNamespace => "MinimalSupport";

        public override string DeclarationSourceHintName => "Minimal.Declarations.g.cs";

        public override string GlobalUsingsSourceHintName => "Minimal.GlobalUsings.g.cs";

        public override string SelectExprMethodName => "ProjectExpr";

        public override string? MappingVisibilityEnumName => null;

        public override string? MappingGenerateAttributeName => null;

        public override string? AutoGeneratedDtoAttributeName => null;

        public override string? MappingDeclareClassName => null;

        public override string? GeneratorKitClassName => null;

        public override string? GlobalUsingPropertyName => null;

        public override string? CommentOutputPropertyName => null;

        public override string? HasRequiredPropertyName => null;

        public override string? UsePrebuildExpressionPropertyName => null;
    }
}
