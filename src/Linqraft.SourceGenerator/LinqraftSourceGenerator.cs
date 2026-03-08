using Linqraft.Core.Configuration;
using Linqraft.Core.Utilities;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Linqraft.SourceGenerator;

[Generator(LanguageNames.CSharp)]
public sealed class LinqraftSourceGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterPostInitializationOutput(
            static output => output.AddSource("Linqraft.Support.g.cs", SupportSourceEmitter.CreateSupportSource())
        );

        var selectExprInvocations = context.SyntaxProvider.CreateSyntaxProvider(
            static (node, _) => node is InvocationExpressionSyntax invocation && IsPotentialSelectExprInvocation(invocation),
            static (syntaxContext, _) => (InvocationExpressionSyntax)syntaxContext.Node
        );

        var mappingClasses = context.SyntaxProvider.ForAttributeWithMetadataName(
            "Linqraft.LinqraftMappingGenerateAttribute",
            static (node, _) => node is ClassDeclarationSyntax,
            static (context, _) => (ClassDeclarationSyntax)context.TargetNode
        );

        var mappingMethods = context.SyntaxProvider.ForAttributeWithMetadataName(
            "Linqraft.LinqraftMappingGenerateAttribute",
            static (node, _) => node is MethodDeclarationSyntax,
            static (context, _) => (MethodDeclarationSyntax)context.TargetNode
        );

        var configuration = context.AnalyzerConfigOptionsProvider.Select(
            static (provider, _) => LinqraftConfiguration.Parse(provider.GlobalOptions)
        );

        var pipeline = context.CompilationProvider
            .Combine(configuration)
            .Combine(selectExprInvocations.Collect())
            .Combine(mappingClasses.Collect())
            .Combine(mappingMethods.Collect());

        context.RegisterSourceOutput(
            pipeline,
            static (output, data) =>
            {
                var ((((compilation, configuration), selectExprs), mappingClassDeclarations), mappingMethodDeclarations) = data;
                var analyzer = new ProjectionAnalyzer(
                    compilation,
                    configuration,
                    output,
                    output.CancellationToken
                );

                analyzer.AnalyzeSelectInvocations(selectExprs);
                analyzer.AnalyzeMappingClasses(mappingClassDeclarations);
                analyzer.AnalyzeMappingMethods(mappingMethodDeclarations);

                foreach (var dto in analyzer.GeneratedDtos)
                {
                    output.AddSource(
                        $"{SymbolNameHelper.SanitizeHintName(dto.Key)}.g.cs",
                        SourceWriters.WriteDtoSource(dto, configuration)
                    );
                }

                foreach (var request in analyzer.ProjectionRequests)
                {
                    var semanticModel = compilation.GetSemanticModel(request.Invocation.SyntaxTree);
                    output.AddSource(
                        $"{request.HintName}.g.cs",
                        SourceWriters.WriteInterceptorSource(request, semanticModel, configuration)
                    );
                }

                foreach (var mapping in analyzer.MappingRequests)
                {
                    var semanticModel = compilation.GetSemanticModel(mapping.SourceNode.SyntaxTree);
                    output.AddSource(
                        $"{mapping.HintName}.g.cs",
                        SourceWriters.WriteMappingSource(mapping, semanticModel, configuration)
                    );
                }
            }
        );
    }

    private static bool IsPotentialSelectExprInvocation(InvocationExpressionSyntax invocation)
    {
        return invocation.Expression switch
        {
            MemberAccessExpressionSyntax memberAccess when memberAccess.Name.Identifier.ValueText == "SelectExpr" => true,
            GenericNameSyntax genericName when genericName.Identifier.ValueText == "SelectExpr" => true,
            _ => false,
        };
    }
}
