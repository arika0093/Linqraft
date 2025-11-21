using System.Collections.Generic;
using System.Linq;
using System.Xml;
using Linqraft.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Linqraft;

/// <summary>
/// Generator for SelectExpr method
/// </summary>
[Generator]
public partial class SelectExprGenerator : IIncrementalGenerator
{
    /// <summary>
    /// Initialize the generator
    /// </summary>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Generate pre-defined source code
        context.RegisterPostInitializationOutput(ctx => ConstSourceCodes.ExportAll(ctx));

        // Read MSBuild properties for configuration
        var configurationProvider = context.AnalyzerConfigOptionsProvider.Select(
            static (provider, _) => LinqraftConfiguration.GenerateFromGlobalOptions(provider)
        );

        // Provider to detect SelectExpr method invocations
        var invocations = context
            .SyntaxProvider.CreateSyntaxProvider(
                predicate: static (node, _) => IsSelectExprInvocation(node),
                transform: static (ctx, _) => GetSelectExprInfo(ctx)
            )
            .Where(static info => info is not null)
            .Collect();

        // Combine configuration with invocations
        var invocationsWithConfig = invocations.Combine(configurationProvider);

        // Code generation
        context.RegisterSourceOutput(
            invocationsWithConfig,
            (spc, data) =>
            {
                var (infos, config) = data;
                var infoWithoutNulls = infos.Where(info => info is not null).Select(info => info!);

                // assign configuration to each SelectExprInfo
                foreach (var info in infoWithoutNulls)
                {
                    info.Configuration = config;
                }

                // record locations by SelectExprInfo Id
                var exprGroups = infoWithoutNulls
                    .GroupBy(info => new
                    {
                        Namespace = info.GetNamespaceString(),
                        FileName = info.GetFileNameString() ?? "",
                    })
                    .Select(g =>
                    {
                        var exprs = g.Select(info =>
                        {
                            var location = info.SemanticModel.GetInterceptableLocation(
                                info.Invocation
                            )!;
                            return new SelectExprLocations { Info = info, Location = location };
                        });
                        return new SelectExprGroups
                        {
                            TargetNamespace = g.Key.Namespace,
                            TargetFileName = g.Key.FileName,
                            Exprs = [.. exprs],
                            Configuration = config,
                        };
                    })
                    .ToList();

                // Generate code for explicit DTO infos (one method per group)
                foreach (var exprGroup in exprGroups)
                {
                    exprGroup.GenerateCode(spc);
                }
            }
        );
    }

    private static bool IsSelectExprInvocation(SyntaxNode node)
    {
        // Detect InvocationExpression with method name "SelectExpr"
        if (node is not InvocationExpressionSyntax invocation)
            return false;

        var expression = invocation.Expression;

        // Use shared helper for syntax-level check
        return SelectExprHelper.IsSelectExprInvocationSyntax(expression);
    }

    private static SelectExprInfo? GetSelectExprInfo(GeneratorSyntaxContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        // Get lambda expression from arguments
        if (invocation.ArgumentList.Arguments.Count == 0)
            return null;

        var lambdaArg = invocation.ArgumentList.Arguments[0].Expression;
        if (lambdaArg is not LambdaExpressionSyntax lambda)
            return null;

        // Extract lambda parameter name
        var lambdaParamName = GetLambdaParameterName(lambda);

        // Extract capture argument info (if present)
        var (captureArgExpr, captureType) = GetCaptureInfo(invocation, context.SemanticModel);

        // check
        // 1. SelectExpr with predefined DTO type
        // 2. SelectExpr with explicit DTO type in generic arguments
        // 3. SelectExpr with anonymous object creation
        var body = lambda.Body;

        // 1. Check if this is a generic invocation with predefined DTO type
        // If generics are used, but the body is an ObjectCreationExpression, this takes precedence.
        if (body is ObjectCreationExpressionSyntax objCreation)
        {
            return GetNamedSelectExprInfo(
                context,
                objCreation,
                lambdaParamName,
                captureArgExpr,
                captureType
            );
        }

        // 2. Check for SelectExpr<TIn, TResult>
        if (
            invocation.Expression is MemberAccessExpressionSyntax memberAccess
            && memberAccess.Name is GenericNameSyntax genericName
            && genericName.TypeArgumentList.Arguments.Count >= 2
            && body is AnonymousObjectCreationExpressionSyntax anonSyntax
        )
        {
            return GetExplicitDtoSelectExprInfo(
                context,
                anonSyntax,
                genericName,
                lambdaParamName,
                captureArgExpr,
                captureType
            );
        }

        // 3. Check for anonymous object creation
        if (body is AnonymousObjectCreationExpressionSyntax anon)
        {
            return GetAnonymousSelectExprInfo(
                context,
                anon,
                lambdaParamName,
                captureArgExpr,
                captureType
            );
        }

        // Not a supported form
        return null;
    }

    private static string GetLambdaParameterName(LambdaExpressionSyntax lambda)
    {
        return lambda switch
        {
            SimpleLambdaExpressionSyntax simple => simple.Parameter.Identifier.Text,
            ParenthesizedLambdaExpressionSyntax paren
                when paren.ParameterList.Parameters.Count > 0 => paren
                .ParameterList
                .Parameters[0]
                .Identifier
                .Text,
            _ => "s", // fallback to default
        };
    }

    private static (ExpressionSyntax? captureArgExpr, ITypeSymbol? captureType) GetCaptureInfo(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel
    )
    {
        // Check if invocation has 2 arguments (second one is the capture argument)
        ExpressionSyntax? captureArgExpr = null;
        ITypeSymbol? captureType = null;
        if (invocation.ArgumentList.Arguments.Count == 2)
        {
            captureArgExpr = invocation.ArgumentList.Arguments[1].Expression;
            // Get the type of the capture argument
            var typeInfo = semanticModel.GetTypeInfo(captureArgExpr);
            captureType = typeInfo.Type ?? typeInfo.ConvertedType;
        }

        return (captureArgExpr, captureType);
    }

    private static SelectExprInfoAnonymous? GetAnonymousSelectExprInfo(
        GeneratorSyntaxContext context,
        AnonymousObjectCreationExpressionSyntax anonymousObj,
        string lambdaParameterName,
        ExpressionSyntax? captureArgumentExpression,
        ITypeSymbol? captureArgumentType
    )
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        var semanticModel = context.SemanticModel;

        // Get target type from MemberAccessExpression
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return null;

        // Get type information
        var typeInfo = semanticModel.GetTypeInfo(memberAccess.Expression);
        if (typeInfo.Type is not INamedTypeSymbol namedType)
            return null;

        // Get T from IQueryable<T>
        var sourceType = namedType.TypeArguments.FirstOrDefault();
        if (sourceType is null)
            return null;

        // Get the namespace of the calling code
        var namespaceDecl = invocation
            .Ancestors()
            .OfType<BaseNamespaceDeclarationSyntax>()
            .FirstOrDefault();
        var callerNamespace = namespaceDecl?.Name.ToString() ?? "";

        return new SelectExprInfoAnonymous
        {
            SourceType = sourceType,
            AnonymousObject = anonymousObj,
            SemanticModel = semanticModel,
            Invocation = invocation,
            LambdaParameterName = lambdaParameterName,
            CallerNamespace = callerNamespace,
            CaptureParameterName = null, // No longer used - capture is via closure
            CaptureArgumentExpression = captureArgumentExpression,
            CaptureArgumentType = captureArgumentType,
        };
    }

    private static SelectExprInfoNamed? GetNamedSelectExprInfo(
        GeneratorSyntaxContext context,
        ObjectCreationExpressionSyntax obj,
        string lambdaParameterName,
        ExpressionSyntax? captureArgumentExpression,
        ITypeSymbol? captureArgumentType
    )
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        var semanticModel = context.SemanticModel;

        // Get target type from MemberAccessExpression
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return null;

        // Get type information
        var typeInfo = semanticModel.GetTypeInfo(memberAccess.Expression);
        if (typeInfo.Type is not INamedTypeSymbol namedType)
            return null;

        // Get T from IQueryable<T>
        var sourceType = namedType.TypeArguments.FirstOrDefault();
        if (sourceType is null)
            return null;

        // Get the namespace of the calling code
        var namespaceDecl = invocation
            .Ancestors()
            .OfType<BaseNamespaceDeclarationSyntax>()
            .FirstOrDefault();
        var callerNamespace = namespaceDecl?.Name.ToString() ?? "";

        return new SelectExprInfoNamed
        {
            SourceType = sourceType,
            ObjectCreation = obj,
            SemanticModel = semanticModel,
            Invocation = invocation,
            LambdaParameterName = lambdaParameterName,
            CallerNamespace = callerNamespace,
            CaptureParameterName = null, // No longer used - capture is via closure
            CaptureArgumentExpression = captureArgumentExpression,
            CaptureArgumentType = captureArgumentType,
        };
    }

    private static SelectExprInfoExplicitDto? GetExplicitDtoSelectExprInfo(
        GeneratorSyntaxContext context,
        AnonymousObjectCreationExpressionSyntax anonymousObj,
        GenericNameSyntax genericName,
        string lambdaParameterName,
        ExpressionSyntax? captureArgumentExpression,
        ITypeSymbol? captureArgumentType
    )
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        var semanticModel = context.SemanticModel;

        // Get target type from MemberAccessExpression
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return null;

        // Get type information
        var typeInfo = semanticModel.GetTypeInfo(memberAccess.Expression);
        if (typeInfo.Type is not INamedTypeSymbol namedType)
            return null;

        // Get TIn from IQueryable<TIn>
        var sourceType = namedType.TypeArguments.FirstOrDefault();
        if (sourceType is null)
            return null;

        // Get TResult (second type parameter) - this is the explicit DTO name
        var typeArguments = genericName.TypeArgumentList.Arguments;
        if (typeArguments.Count < 2)
            return null;

        var tResultType = semanticModel.GetTypeInfo(typeArguments[1]).Type;
        if (tResultType is null)
            return null;

        var explicitDtoName = tResultType.Name;

        // Extract parent class names if the DTO type is nested
        var parentClasses = new List<string>();
        var currentContaining = tResultType.ContainingType;
        while (currentContaining is not null)
        {
            parentClasses.Insert(0, currentContaining.Name);
            currentContaining = currentContaining.ContainingType;
        }

        // Get the namespace of the calling code
        var invocationSyntaxTree = invocation.SyntaxTree;
        var root = invocationSyntaxTree.GetRoot();
        var namespaceDecl = invocation
            .Ancestors()
            .OfType<BaseNamespaceDeclarationSyntax>()
            .FirstOrDefault();
        var targetNamespace = namespaceDecl?.Name.ToString() ?? "";

        return new SelectExprInfoExplicitDto
        {
            SourceType = sourceType,
            AnonymousObject = anonymousObj,
            SemanticModel = semanticModel,
            Invocation = invocation,
            ExplicitDtoName = explicitDtoName,
            TargetNamespace = targetNamespace,
            LambdaParameterName = lambdaParameterName,
            CallerNamespace = targetNamespace,
            ParentClasses = parentClasses,
            TResultType = tResultType,
            CaptureParameterName = null, // No longer used - capture is via closure
            CaptureArgumentExpression = captureArgumentExpression,
            CaptureArgumentType = captureArgumentType,
        };
    }
}
