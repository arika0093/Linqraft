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

internal static class LinqraftGeneratorPipeline
{
    public static void Initialize(
        IncrementalGeneratorInitializationContext context,
        LinqraftGeneratorOptionsCore generatorOptions
    )
    {
        context.RegisterPostInitializationOutput(output =>
            output.AddSource(
                generatorOptions.DeclarationSourceHintName,
                SupportSourceEmitter.CreateSupportSource(generatorOptions)
            )
        );

        var configuration = context.AnalyzerConfigOptionsProvider.Select(
            (provider, _) => LinqraftConfiguration.Parse(provider.GlobalOptions, generatorOptions)
        );

        var projectionTemplates = context
            .SyntaxProvider.CreateSyntaxProvider(
                (node, _) =>
                    node is InvocationExpressionSyntax invocation
                    && IsPotentialProjectionInvocation(invocation, generatorOptions),
                (syntaxContext, cancellationToken) =>
                    ProjectionTemplateBuilder.AnalyzeProjectionInvocation(
                        syntaxContext,
                        cancellationToken,
                        generatorOptions
                    )
            )
            .Where(static template => template is not null)
            .Select(static (template, _) => template!);

        var objectGenerationTemplates = context
            .SyntaxProvider.CreateSyntaxProvider(
                (node, _) =>
                    node is InvocationExpressionSyntax invocation
                    && IsPotentialObjectGenerationInvocation(invocation, generatorOptions),
                (syntaxContext, cancellationToken) =>
                    ProjectionTemplateBuilder.AnalyzeObjectGenerationInvocation(
                        syntaxContext,
                        cancellationToken,
                        generatorOptions
                    )
            )
            .Where(static template => template is not null)
            .Select(static (template, _) => template!);

        var mappingClassTemplates =
            generatorOptions.MappingGenerateAttributeMetadataName is { } mappingAttributeMetadataName
                ? context
                    .SyntaxProvider.ForAttributeWithMetadataName(
                        mappingAttributeMetadataName,
                        static (node, _) => node is ClassDeclarationSyntax,
                        (attributeContext, cancellationToken) =>
                            ProjectionTemplateBuilder.AnalyzeMappingClass(
                                attributeContext,
                                cancellationToken,
                                generatorOptions
                            )
                    )
                    .Where(static template => template is not null)
                    .Select(static (template, _) => template!)
                : CreateEmptyTemplates<MappingSourceTemplateModel>(context);

        var mappingMethodTemplates =
            generatorOptions.MappingGenerateAttributeMetadataName is { } mappingMethodMetadataName
                ? context
                    .SyntaxProvider.ForAttributeWithMetadataName(
                        mappingMethodMetadataName,
                        static (node, _) => node is MethodDeclarationSyntax,
                        (attributeContext, cancellationToken) =>
                            ProjectionTemplateBuilder.AnalyzeMappingMethod(
                                attributeContext,
                                cancellationToken,
                                generatorOptions
                            )
                    )
                    .Where(static template => template is not null)
                    .Select(static (template, _) => template!)
                : CreateEmptyTemplates<MappingSourceTemplateModel>(context);

        var projectionModels = projectionTemplates
            .Combine(configuration)
            .Select((data, _) => ProjectionModelFinalizer.FinalizeProjection(data.Left, data.Right));
        var objectGenerationModels = objectGenerationTemplates
            .Combine(configuration)
            .Select(
                (data, _) =>
                    ProjectionModelFinalizer.FinalizeObjectGeneration(data.Left, data.Right)
            );
        var mappingClassModels = mappingClassTemplates
            .Combine(configuration)
            .Select(
                (data, _) => ProjectionModelFinalizer.FinalizeMapping(data.Left, data.Right)
            );
        var mappingMethodModels = mappingMethodTemplates
            .Combine(configuration)
            .Select(
                (data, _) => ProjectionModelFinalizer.FinalizeMapping(data.Left, data.Right)
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
                (data, _) =>
                    new GeneratedSourceBuildContextModel
                    {
                        OwnedSources = data.Left,
                        Configuration = data.Right,
                    }
            )
            .Select((buildContext, _) => BuildGeneratedSources(buildContext));

        context.RegisterSourceOutput(
            generatedSources,
            (output, sourceSet) => AddGeneratedSources(output, sourceSet)
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
                    HintName = GetStandaloneDtoHintName(dto, context.Configuration.GeneratorOptions),
                    SourceText = SourceWriters.WriteDtoUnit(dto.Dto, context.Configuration),
                }
            );
        }

        if (context.Configuration.GlobalUsing)
        {
            generatedSources.Add(
                new GeneratedSourceFileModel
                {
                    HintName = context.Configuration.GeneratorOptions.GlobalUsingsSourceHintName,
                    SourceText = SourceWriters.WriteGlobalUsingsUnit(context.Configuration),
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

    private static string GetStandaloneDtoHintName(
        GeneratedDtoEmissionModel dto,
        LinqraftGeneratorOptionsCore generatorOptions
    )
    {
        if (dto.OwnerHintNames.Length == 1)
        {
            return $"{dto.OwnerHintNames[0]}.g.cs";
        }

        return
            $"{generatorOptions.StandaloneDtoHintNamePrefix}_{HashingHelper.ComputeHash(dto.Dto.Key, 16)}.g.cs";
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

    private static bool IsPotentialProjectionInvocation(
        InvocationExpressionSyntax invocation,
        LinqraftGeneratorOptionsCore generatorOptions
    )
    {
        return invocation.Expression switch
        {
            MemberAccessExpressionSyntax memberAccess
                when memberAccess.Name.Identifier.ValueText
                    is var methodName
                        && (
                            methodName == generatorOptions.SelectExprMethodName
                            || methodName == generatorOptions.SelectManyExprMethodName
                            || methodName == generatorOptions.GroupByExprMethodName
                        ) => true,
            GenericNameSyntax genericName
                when genericName.Identifier.ValueText is var methodName
                    && (
                        methodName == generatorOptions.SelectExprMethodName
                        || methodName == generatorOptions.SelectManyExprMethodName
                        || methodName == generatorOptions.GroupByExprMethodName
                    ) => true,
            _ => false,
        };
    }

    private static bool IsPotentialObjectGenerationInvocation(
        InvocationExpressionSyntax invocation,
        LinqraftGeneratorOptionsCore generatorOptions
    )
    {
        if (generatorOptions.GeneratorKitClassName is null)
        {
            return false;
        }

        return invocation.Expression switch
        {
            MemberAccessExpressionSyntax memberAccess
                when memberAccess.Name.Identifier.ValueText
                    == generatorOptions.ObjectGenerationMethodName => true,
            GenericNameSyntax genericName
                when genericName.Identifier.ValueText == generatorOptions.ObjectGenerationMethodName =>
                true,
            _ => false,
        };
    }

    private static IncrementalValuesProvider<T> CreateEmptyTemplates<T>(
        IncrementalGeneratorInitializationContext context
    )
        where T : class
    {
        return context
            .SyntaxProvider.CreateSyntaxProvider(static (_, _) => false, static (_, _) => (T?)null)
            .Where(static template => template is not null)
            .Select(static (template, _) => template!);
    }
}
