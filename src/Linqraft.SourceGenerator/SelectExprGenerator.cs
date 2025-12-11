using System.Collections.Generic;
using System.Linq;
using Linqraft.Core;
using Linqraft.Core.SyntaxHelpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Linqraft.Core.SyntaxHelpers.LambdaHelper;

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
        context.RegisterPostInitializationOutput(
            GenerateSourceCodeSnippets.ExportAllConstantSnippets
        );

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

        // Provider to detect methods with [LinqraftMappingGenerate] attribute
        var mappingMethods = context
            .SyntaxProvider.CreateSyntaxProvider(
                predicate: static (node, _) => IsLinqraftMappingGenerateMethod(node),
                transform: static (ctx, _) => GetMappingMethodInfo(ctx)
            )
            .Where(static info => info is not null)
            .Collect();

        // Combine configuration with invocations and mapping methods
        var invocationsWithConfig = invocations.Combine(configurationProvider);
        var mappingMethodsWithConfig = mappingMethods.Combine(configurationProvider);

        // Combine both providers
        var combinedData = invocationsWithConfig.Combine(mappingMethodsWithConfig);

        // Code generation
        context.RegisterSourceOutput(
            combinedData,
            (spc, data) =>
            {
                var ((infos, config), (mappingInfos, _)) = data;
                var infoWithoutNulls = infos.Where(info => info is not null).Select(info => info!);
                var mappingInfoWithoutNulls = mappingInfos
                    .Where(info => info is not null)
                    .Select(info => info!);

                // Process mapping methods to extract their SelectExpr invocations
                var mappingSelectExprInfos = new List<SelectExprInfo>();
                foreach (var mappingInfo in mappingInfoWithoutNulls)
                {
                    var selectExprInfo = ProcessMappingMethod(mappingInfo, config);
                    if (selectExprInfo != null)
                    {
                        mappingSelectExprInfos.Add(selectExprInfo);
                    }
                }

                // Combine regular SelectExpr infos with mapping-generated ones
                var allInfos = infoWithoutNulls.Concat(mappingSelectExprInfos);

                // assign configuration to each SelectExprInfo
                foreach (var info in allInfos)
                {
                    info.Configuration = config;
                }

                // record locations by SelectExprInfo Id
                var exprGroups = allInfos
                    .GroupBy(info => new
                    {
                        Namespace = info.GetNamespaceString(),
                        FileName = info.GetFileNameString() ?? "",
                        // Group mapping methods by containing class to generate code in the same class
                        MappingClass = info.MappingContainingClass?.ToDisplayString(
                            SymbolDisplayFormat.FullyQualifiedFormat
                        ) ?? "",
                    })
                    .Select(g =>
                    {
                        var exprs = g.Select(info =>
                        {
                            var location =
                                info.MappingMethodName == null
                                    ? info.SemanticModel.GetInterceptableLocation(info.Invocation)!
                                    : null; // No location needed for mapping methods
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

                // Collect all DTOs from all groups and deduplicate globally
                var allDtoClassInfos = new List<GenerateDtoClassInfo>();
                foreach (var exprGroup in exprGroups)
                {
                    foreach (var expr in exprGroup.Exprs)
                    {
                        var classInfos = expr.Info.GenerateDtoClasses();
                        allDtoClassInfos.AddRange(classInfos);
                    }
                }

                // Generate all DTOs in a single shared source file
                var dtoCode = GenerateSourceCodeSnippets.BuildGlobalDtoCodeSnippet(
                    allDtoClassInfos,
                    config
                );
                if (!string.IsNullOrEmpty(dtoCode))
                {
                    spc.AddSource("GeneratedDtos.g.cs", dtoCode);
                }

                // Generate code for expression methods (without DTOs)
                foreach (var exprGroup in exprGroups)
                {
                    exprGroup.GenerateCodeWithoutDtos(spc);
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
        if (!SelectExprHelper.IsSelectExprInvocationSyntax(expression))
            return false;

        // Skip if this SelectExpr is nested inside another SelectExpr.
        // When SelectExpr is used inside another SelectExpr (nested SelectExpr),
        // only the outermost SelectExpr should generate an interceptor.
        // The inner SelectExpr will be converted to a regular Select call by the outer one.
        if (SelectExprHelper.IsNestedInsideAnotherSelectExpr(invocation))
            return false;

        return true;
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
        var lambdaParamName = LambdaHelper.GetLambdaParameterName(lambda);

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

    private static bool IsLinqraftMappingGenerateMethod(SyntaxNode node)
    {
        // Detect method declaration with [LinqraftMappingGenerate] attribute
        if (node is not MethodDeclarationSyntax method)
            return false;

        // Check if the method has the LinqraftMappingGenerate attribute
        // Syntax-level check only - full semantic validation happens in GetMappingMethodInfo
        return method
            .AttributeLists.SelectMany(list => list.Attributes)
            .Any(attr =>
            {
                var attrName = attr.Name.ToString();
                return attrName == "LinqraftMappingGenerate"
                    || attrName == "LinqraftMappingGenerateAttribute"
                    || attrName == "Linqraft.LinqraftMappingGenerate"
                    || attrName == "Linqraft.LinqraftMappingGenerateAttribute";
            });
    }

    private static SelectExprMappingInfo? GetMappingMethodInfo(GeneratorSyntaxContext context)
    {
        var method = (MethodDeclarationSyntax)context.Node;
        var semanticModel = context.SemanticModel;

        // Get the method symbol
        var methodSymbol = semanticModel.GetDeclaredSymbol(method);
        if (methodSymbol is null)
            return null;

        // Get the LinqraftMappingGenerate attribute using full name check
        var attributeData = methodSymbol
            .GetAttributes()
            .FirstOrDefault(attr =>
            {
                if (attr.AttributeClass is null)
                    return false;

                var fullName = attr.AttributeClass.ToDisplayString(
                    SymbolDisplayFormat.FullyQualifiedFormat
                );
                return fullName == "global::Linqraft.LinqraftMappingGenerateAttribute";
            });

        if (attributeData is null)
            return null;

        // Get the target method name from the attribute
        var targetMethodName = attributeData.ConstructorArguments.FirstOrDefault().Value as string;
        if (string.IsNullOrEmpty(targetMethodName))
            return null;

        // Get the containing class (must be static partial)
        var containingClass = methodSymbol.ContainingType;
        if (containingClass is null || !containingClass.IsStatic)
            return null;

        // Get the namespace
        var containingNamespace = containingClass.ContainingNamespace?.ToDisplayString() ?? "";

        return new SelectExprMappingInfo
        {
            MethodDeclaration = method,
            TargetMethodName = targetMethodName!,
            ContainingClass = containingClass,
            SemanticModel = semanticModel,
            ContainingNamespace = containingNamespace,
        };
    }

    private static SelectExprInfo? ProcessMappingMethod(
        SelectExprMappingInfo mappingInfo,
        LinqraftConfiguration config
    )
    {
        // Find the SelectExpr invocation inside the method
        var selectExprInvocation = mappingInfo
            .MethodDeclaration.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .FirstOrDefault(inv =>
            {
                var expression = inv.Expression;
                return SelectExprHelper.IsSelectExprInvocationSyntax(expression);
            });

        if (selectExprInvocation is null)
            return null;

        var semanticModel = mappingInfo.SemanticModel;

        // Get lambda expression from arguments
        if (selectExprInvocation.ArgumentList.Arguments.Count == 0)
            return null;

        var lambdaArg = selectExprInvocation.ArgumentList.Arguments[0].Expression;
        if (lambdaArg is not LambdaExpressionSyntax lambda)
            return null;

        // Extract lambda parameter name
        var lambdaParamName = LambdaHelper.GetLambdaParameterName(lambda);

        // Extract capture argument info (if present)
        var (captureArgExpr, captureType) = GetCaptureInfo(selectExprInvocation, semanticModel);

        // check
        // 1. SelectExpr with predefined DTO type
        // 2. SelectExpr with explicit DTO type in generic arguments
        // 3. SelectExpr with anonymous object creation
        var body = lambda.Body;

        SelectExprInfo? selectExprInfo = null;

        // 1. Check if this is a generic invocation with predefined DTO type
        // If generics are used, but the body is an ObjectCreationExpression, this takes precedence.
        if (body is ObjectCreationExpressionSyntax objCreation)
        {
            var ctx = new { Node = selectExprInvocation, SemanticModel = semanticModel };
            selectExprInfo = GetNamedSelectExprInfoInternal(
                selectExprInvocation,
                semanticModel,
                objCreation,
                lambdaParamName,
                captureArgExpr,
                captureType
            );
        }
        // 2. Check for SelectExpr<TIn, TResult>
        else if (
            selectExprInvocation.Expression is MemberAccessExpressionSyntax memberAccess
            && memberAccess.Name is GenericNameSyntax genericName
            && genericName.TypeArgumentList.Arguments.Count >= 2
            && body is AnonymousObjectCreationExpressionSyntax anonSyntax
        )
        {
            selectExprInfo = GetExplicitDtoSelectExprInfoInternal(
                selectExprInvocation,
                semanticModel,
                anonSyntax,
                genericName,
                lambdaParamName,
                captureArgExpr,
                captureType
            );
        }
        // 3. Check for anonymous object creation
        else if (body is AnonymousObjectCreationExpressionSyntax anon)
        {
            selectExprInfo = GetAnonymousSelectExprInfoInternal(
                selectExprInvocation,
                semanticModel,
                anon,
                lambdaParamName,
                captureArgExpr,
                captureType
            );
        }

        if (selectExprInfo is null)
            return null;

        // Add mapping information to the SelectExprInfo
        return selectExprInfo with
        {
            MappingMethodName = mappingInfo.TargetMethodName,
            MappingContainingClass = mappingInfo.ContainingClass,
        };
    }

    // Helper methods to extract SelectExprInfo without GeneratorSyntaxContext
    private static SelectExprInfoNamed? GetNamedSelectExprInfoInternal(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        ObjectCreationExpressionSyntax obj,
        string lambdaParameterName,
        ExpressionSyntax? captureArgumentExpression,
        ITypeSymbol? captureArgumentType
    )
    {
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
            CaptureParameterName = null,
            CaptureArgumentExpression = captureArgumentExpression,
            CaptureArgumentType = captureArgumentType,
        };
    }

    private static SelectExprInfoAnonymous? GetAnonymousSelectExprInfoInternal(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        AnonymousObjectCreationExpressionSyntax anonymousObj,
        string lambdaParameterName,
        ExpressionSyntax? captureArgumentExpression,
        ITypeSymbol? captureArgumentType
    )
    {
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
            CaptureParameterName = null,
            CaptureArgumentExpression = captureArgumentExpression,
            CaptureArgumentType = captureArgumentType,
        };
    }

    private static SelectExprInfoExplicitDto? GetExplicitDtoSelectExprInfoInternal(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        AnonymousObjectCreationExpressionSyntax anonymousObj,
        GenericNameSyntax genericName,
        string lambdaParameterName,
        ExpressionSyntax? captureArgumentExpression,
        ITypeSymbol? captureArgumentType
    )
    {
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
            CaptureParameterName = null,
            CaptureArgumentExpression = captureArgumentExpression,
            CaptureArgumentType = captureArgumentType,
        };
    }
}
