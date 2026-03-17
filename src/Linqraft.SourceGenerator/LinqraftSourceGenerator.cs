using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Linqraft.Core.Collections;
using Linqraft.Core.Configuration;
using Linqraft.Core.Utilities;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Linqraft.SourceGenerator;

[Generator(LanguageNames.CSharp)]
public sealed class LinqraftSourceGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterPostInitializationOutput(static output =>
            output.AddSource(
                "Linqraft.Declarations.g.cs",
                SupportSourceEmitter.CreateSupportSource()
            )
        );

        var configuration = context.AnalyzerConfigOptionsProvider.Select(
            static (provider, _) => LinqraftConfiguration.Parse(provider.GlobalOptions)
        );

        var projectionTemplates = context
            .SyntaxProvider.CreateSyntaxProvider(
                static (node, _) =>
                    node is InvocationExpressionSyntax invocation
                    && IsPotentialProjectionInvocation(invocation),
                static (syntaxContext, cancellationToken) =>
                    ProjectionTemplateBuilder.AnalyzeProjectionInvocation(
                        syntaxContext,
                        cancellationToken
                    )
            )
            .Where(static template => template is not null)
            .Select(static (template, _) => template!);

        var objectGenerationTemplates = context
            .SyntaxProvider.CreateSyntaxProvider(
                static (node, _) =>
                    node is InvocationExpressionSyntax invocation
                    && IsPotentialObjectGenerationInvocation(invocation),
                static (syntaxContext, cancellationToken) =>
                    ProjectionTemplateBuilder.AnalyzeObjectGenerationInvocation(
                        syntaxContext,
                        cancellationToken
                    )
            )
            .Where(static template => template is not null)
            .Select(static (template, _) => template!);

        var mappingClassTemplates = context
            .SyntaxProvider.ForAttributeWithMetadataName(
                "Linqraft.LinqraftMappingGenerateAttribute",
                static (node, _) => node is ClassDeclarationSyntax,
                static (attributeContext, cancellationToken) =>
                    ProjectionTemplateBuilder.AnalyzeMappingClass(
                        attributeContext,
                        cancellationToken
                    )
            )
            .Where(static template => template is not null)
            .Select(static (template, _) => template!);

        var mappingMethodTemplates = context
            .SyntaxProvider.ForAttributeWithMetadataName(
                "Linqraft.LinqraftMappingGenerateAttribute",
                static (node, _) => node is MethodDeclarationSyntax,
                static (attributeContext, cancellationToken) =>
                    ProjectionTemplateBuilder.AnalyzeMappingMethod(
                        attributeContext,
                        cancellationToken
                    )
            )
            .Where(static template => template is not null)
            .Select(static (template, _) => template!);

        // Collect user-defined Linqraft extensions (annotated with [LinqraftExtension])
        // and register stub source output for each.
        var extensionModels = context
            .SyntaxProvider.ForAttributeWithMetadataName(
                "Linqraft.LinqraftExtensionAttribute",
                static (node, _) => node is ClassDeclarationSyntax,
                static (attributeContext, _) =>
                    LinqraftSourceGenerator.BuildExtensionInfo(attributeContext)
            )
            .Where(static info => info is not null)
            .Select(static (info, _) => info!);

        context.RegisterSourceOutput(
            extensionModels,
            static (output, extensionInfo) =>
            {
                var (hintName, source) = (
                    $"LinqraftExtensionStub_{extensionInfo.MethodName}.g.cs",
                    LinqraftExtensionStubEmitter.EmitExtensionStubSource(extensionInfo)
                );
                output.AddSource(hintName, source);
            }
        );

        var projectionModels = projectionTemplates
            .Combine(configuration)
            .Select(
                static (data, _) =>
                    ProjectionModelFinalizer.FinalizeProjection(data.Left, data.Right)
            );
        var objectGenerationModels = objectGenerationTemplates
            .Combine(configuration)
            .Select(
                static (data, _) =>
                    ProjectionModelFinalizer.FinalizeObjectGeneration(data.Left, data.Right)
            );
        var mappingClassModels = mappingClassTemplates
            .Combine(configuration)
            .Select(
                static (data, _) => ProjectionModelFinalizer.FinalizeMapping(data.Left, data.Right)
            );
        var mappingMethodModels = mappingMethodTemplates
            .Combine(configuration)
            .Select(
                static (data, _) => ProjectionModelFinalizer.FinalizeMapping(data.Left, data.Right)
            );

        var projectionSources = projectionModels.Select(
            static (model, _) =>
                (OwnedGeneratedSourceModel)
                    new ProjectionOwnedGeneratedSourceModel
                    {
                        HintName = $"{model.Request.HintName}.g.cs",
                        OwnerHintName = model.Request.HintName,
                        Request = model.Request,
                        GeneratedDtos = model.GeneratedDtos,
                    }
        );
        var objectGenerationSources = objectGenerationModels.Select(
            static (model, _) =>
                (OwnedGeneratedSourceModel)
                    new ObjectGenerationOwnedGeneratedSourceModel
                    {
                        HintName = $"{model.Request.HintName}.g.cs",
                        OwnerHintName = model.Request.HintName,
                        Request = model.Request,
                        GeneratedDtos = model.GeneratedDtos,
                    }
        );
        var mappingClassSources = mappingClassModels.Select(
            static (model, _) =>
                (OwnedGeneratedSourceModel)
                    new MappingOwnedGeneratedSourceModel
                    {
                        HintName = $"{model.Request.HintName}.g.cs",
                        OwnerHintName = model.Request.HintName,
                        Request = model.Request,
                        GeneratedDtos = model.GeneratedDtos,
                    }
        );
        var mappingMethodSources = mappingMethodModels.Select(
            static (model, _) =>
                (OwnedGeneratedSourceModel)
                    new MappingOwnedGeneratedSourceModel
                    {
                        HintName = $"{model.Request.HintName}.g.cs",
                        OwnerHintName = model.Request.HintName,
                        Request = model.Request,
                        GeneratedDtos = model.GeneratedDtos,
                    }
        );

        var queryOwnedSources = MergeCollectedValues(
            projectionSources.Collect(),
            objectGenerationSources.Collect()
        );
        var queryAndClassOwnedSources = MergeCollectedValues(
            queryOwnedSources,
            mappingClassSources.Collect()
        );
        var ownedSources = MergeCollectedValues(
            queryAndClassOwnedSources,
            mappingMethodSources.Collect()
        );
        var generatedSources = ownedSources
            .Combine(configuration)
            .Select(
                static (data, _) =>
                    new GeneratedSourceBuildContextModel
                    {
                        OwnedSources = data.Left,
                        Configuration = data.Right,
                    }
            )
            .Select(static (context, _) => LinqraftSourceGenerator.BuildGeneratedSources(context));

        context.RegisterSourceOutput(
            generatedSources,
            static (output, sourceSet) =>
                LinqraftSourceGenerator.AddGeneratedSources(output, sourceSet)
        );
    }

    private static GeneratedSourceSetModel BuildGeneratedSources(
        GeneratedSourceBuildContextModel context
    )
    {
        var mergedDtos = ProjectionModelFinalizer.MergeDtosForEmission(
            context.OwnedSources.SelectMany(static source => source.GeneratedDtos)
        );
        var ownedSourceHints = new HashSet<string>(
            context.OwnedSources.Select(static source => source.OwnerHintName),
            StringComparer.Ordinal
        );
        var collocatedDtosByOwnerHint = mergedDtos
            .Where(dto =>
                dto.OwnerHintNames.Length == 1 && ownedSourceHints.Contains(dto.OwnerHintNames[0])
            )
            .GroupBy(dto => dto.OwnerHintNames[0], StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(dto => dto.Dto).ToArray(),
                StringComparer.Ordinal
            );
        var generatedSources = new List<GeneratedSourceFileModel>();

        foreach (
            var ownedSource in context.OwnedSources.OrderBy(
                static source => source.HintName,
                StringComparer.Ordinal
            )
        )
        {
            collocatedDtosByOwnerHint.TryGetValue(
                ownedSource.OwnerHintName,
                out var collocatedDtos
            );
            generatedSources.Add(
                new GeneratedSourceFileModel
                {
                    HintName = ownedSource.HintName,
                    SourceText = SourceWriters.WriteOwnedUnit(
                        ownedSource,
                        collocatedDtos ?? Array.Empty<GeneratedDtoModel>(),
                        context.Configuration
                    ),
                }
            );
        }

        foreach (var dto in mergedDtos.Where(dto => ShouldEmitStandaloneDto(dto, ownedSourceHints)))
        {
            generatedSources.Add(
                new GeneratedSourceFileModel
                {
                    HintName = GetStandaloneDtoHintName(dto),
                    SourceText = SourceWriters.WriteDtoUnit(dto.Dto, context.Configuration),
                }
            );
        }

        return new GeneratedSourceSetModel
        {
            Sources = generatedSources
                .OrderBy(static source => source.HintName, StringComparer.Ordinal)
                .ToArray(),
        };
    }

    private static bool ShouldEmitStandaloneDto(
        GeneratedDtoEmissionModel dto,
        ISet<string> ownedSourceHints
    )
    {
        return dto.OwnerHintNames.Length != 1 || !ownedSourceHints.Contains(dto.OwnerHintNames[0]);
    }

    private static string GetStandaloneDtoHintName(GeneratedDtoEmissionModel dto)
    {
        if (dto.OwnerHintNames.Length == 1)
        {
            return $"{dto.OwnerHintNames[0]}.g.cs";
        }

        return $"Model_{HashingHelper.ComputeHash(dto.Dto.Key, 16)}.g.cs";
    }

    private static void AddGeneratedSources(
        SourceProductionContext output,
        GeneratedSourceSetModel sourceSet
    )
    {
        foreach (var source in sourceSet.Sources)
        {
            output.AddSource(source.HintName, source.SourceText);
        }
    }

    private static IncrementalValueProvider<EquatableArray<T>> MergeCollectedValues<T>(
        IncrementalValueProvider<ImmutableArray<T>> first,
        IncrementalValueProvider<ImmutableArray<T>> second
    )
        where T : IEquatable<T>
    {
        return first
            .Combine(second)
            .Select(static (pair, _) => new EquatableArray<T>(pair.Left.Concat(pair.Right)));
    }

    private static IncrementalValueProvider<EquatableArray<T>> MergeCollectedValues<T>(
        IncrementalValueProvider<EquatableArray<T>> first,
        IncrementalValueProvider<ImmutableArray<T>> second
    )
        where T : IEquatable<T>
    {
        return first
            .Combine(second)
            .Select(static (pair, _) => new EquatableArray<T>(pair.Left.Concat(pair.Right)));
    }

    private static IncrementalValueProvider<EquatableArray<T>> MergeCollectedValues<T>(
        IncrementalValueProvider<ImmutableArray<T>> first,
        IncrementalValueProvider<ImmutableArray<T>> second,
        IncrementalValueProvider<ImmutableArray<T>> third
    )
        where T : IEquatable<T>
    {
        return MergeCollectedValues(MergeCollectedValues(first, second), third);
    }

    private static bool IsPotentialProjectionInvocation(InvocationExpressionSyntax invocation)
    {
        return invocation.Expression switch
        {
            MemberAccessExpressionSyntax memberAccess
                when memberAccess.Name.Identifier.ValueText
                    is "SelectExpr"
                        or "SelectManyExpr"
                        or "GroupByExpr" => true,
            GenericNameSyntax genericName
                when genericName.Identifier.ValueText
                    is "SelectExpr"
                        or "SelectManyExpr"
                        or "GroupByExpr" => true,
            _ => false,
        };
    }

    private static bool IsPotentialObjectGenerationInvocation(InvocationExpressionSyntax invocation)
    {
        return invocation.Expression switch
        {
            MemberAccessExpressionSyntax memberAccess
                when memberAccess.Name.Identifier.ValueText == "Generate" => true,
            GenericNameSyntax genericName when genericName.Identifier.ValueText == "Generate" =>
                true,
            _ => false,
        };
    }

    private static LinqraftExtensionMethodInfo? BuildExtensionInfo(
        GeneratorAttributeSyntaxContext attributeContext
    )
    {
        if (
            attributeContext.TargetSymbol is not INamedTypeSymbol classSymbol
            || attributeContext.Attributes.IsEmpty
        )
        {
            return null;
        }

        var attr = attributeContext.Attributes[0];
        var methodName =
            attr.ConstructorArguments.Length > 0
                ? attr.ConstructorArguments[0].Value as string
                : null;
        if (methodName is null)
        {
            return null;
        }

        var generateNamespace = attr
            .NamedArguments.FirstOrDefault(na =>
                string.Equals(na.Key, "GenerateNamespace", StringComparison.Ordinal)
            )
            .Value.Value as string;
        var behaviorRaw = attr
            .NamedArguments.FirstOrDefault(na =>
                string.Equals(na.Key, "Behavior", StringComparison.Ordinal)
            )
            .Value.Value;
        var behavior = behaviorRaw is null
            ? LinqraftExtensionBehaviorKind.PassThrough
            : (LinqraftExtensionBehaviorKind)Convert.ToInt32(behaviorRaw);

        return new LinqraftExtensionMethodInfo
        {
            MethodName = methodName,
            GenerateNamespace = generateNamespace,
            Behavior = behavior,
        };
    }
}
