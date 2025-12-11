using System;
using System.Collections.Generic;
using System.Linq;
using Linqraft.Core;
using Linqraft.Core.Formatting;
using Linqraft.Core.RoslynHelpers;
using Linqraft.Core.SyntaxHelpers;
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
        context.RegisterPostInitializationOutput(
            GenerateSourceCodeSnippets.ExportAllConstantSnippets
        );

        // Read MSBuild properties for configuration
        var configurationProvider = context.AnalyzerConfigOptionsProvider.Select(
            static (provider, _) => LinqraftConfiguration.GenerateFromGlobalOptions(provider)
        );

        // Provider to detect reverse converter attribute usages
        var reverseConverters = context
            .SyntaxProvider.CreateSyntaxProvider(
                predicate: static (node, _) => IsReverseConverterCandidate(node),
                transform: static (ctx, _) => GetReverseConverterRequest(ctx)
            )
            .Where(static info => info is not null)
            .Select(static (info, _) => info!)
            .Collect();

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
        var pipeline = invocationsWithConfig.Combine(reverseConverters);

        // Code generation
        context.RegisterSourceOutput(
            pipeline,
            (spc, data) =>
            {
                var ((infos, config), reverseConverterRequests) = data;
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

                // Generate reverse converters for DTOs marked with LinqraftReverseConvertionAttribute
                var dtoLookup = BuildDtoLookup(allDtoClassInfos);
                var reverseIndex = 0;
                foreach (var request in reverseConverterRequests)
                {
                    if (!TryFindDtoInfo(dtoLookup, request.DtoType, out var dtoStructure))
                    {
                        continue;
                    }

                    var reverseCode = GenerateReverseConverterCode(request, dtoStructure);
                    if (!string.IsNullOrEmpty(reverseCode))
                    {
                        var hint = request.ConverterSymbol.Name;
                        spc.AddSource(
                            $"GeneratedReverseConverter_{hint}_{reverseIndex++}.g.cs",
                            reverseCode
                        );
                    }
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

    private static bool IsReverseConverterCandidate(SyntaxNode node)
    {
        return node is ClassDeclarationSyntax { AttributeLists.Count: > 0 };
    }

    private static ReverseConverterRequest? GetReverseConverterRequest(GeneratorSyntaxContext context)
    {
        var classDecl = (ClassDeclarationSyntax)context.Node;
        var semanticModel = context.SemanticModel;
        var classSymbol = semanticModel.GetDeclaredSymbol(classDecl) as INamedTypeSymbol;
        if (classSymbol is null)
            return null;

        foreach (var attributeList in classDecl.AttributeLists)
        {
            foreach (var attribute in attributeList.Attributes)
            {
                if (attribute.Name is not GenericNameSyntax genericName)
                    continue;
                if (genericName.TypeArgumentList.Arguments.Count != 1)
                    continue;

                var attributeName = attribute.Name switch
                {
                    IdentifierNameSyntax id => id.Identifier.Text,
                    GenericNameSyntax gn => gn.Identifier.Text,
                    QualifiedNameSyntax qn => qn.Right.Identifier.Text,
                    AliasQualifiedNameSyntax an => an.Name.Identifier.Text,
                    _ => string.Empty,
                };

                if (
                    attributeName != "LinqraftReverseConvertion"
                    && attributeName != "LinqraftReverseConvertionAttribute"
                )
                    continue;

                var dtoTypeSyntax = genericName.TypeArgumentList.Arguments[0];
                var dtoTypeInfo = semanticModel.GetTypeInfo(dtoTypeSyntax);
                var dtoType = dtoTypeInfo.Type ?? dtoTypeInfo.ConvertedType;
                if (dtoType is null)
                    continue;

                var isStatic = false;
                var arguments = attribute.ArgumentList?.Arguments ?? default;
                foreach (var arg in arguments)
                {
                    if (arg.NameEquals?.Name.Identifier.Text == "IsStatic")
                    {
                        var constValue = semanticModel.GetConstantValue(arg.Expression);
                        if (constValue.HasValue && constValue.Value is bool b)
                        {
                            isStatic = b;
                        }
                    }
                }

                return new ReverseConverterRequest
                {
                    ConverterSymbol = classSymbol,
                    DtoType = dtoType,
                    IsStatic = isStatic,
                };
            }
        }

        return null;
    }

    private static Dictionary<string, DtoStructure> BuildDtoLookup(
        List<GenerateDtoClassInfo> dtoInfos
    )
    {
        var dict = new Dictionary<string, DtoStructure>();
        foreach (var dto in dtoInfos)
        {
            var key = NormalizeFullName(dto.FullName);
            if (!dict.ContainsKey(key))
            {
                dict[key] = dto.Structure;
            }
        }
        return dict;
    }

    private static bool TryFindDtoInfo(
        Dictionary<string, DtoStructure> dtoLookup,
        ITypeSymbol dtoType,
        out DtoStructure dtoStructure
    )
    {
        var dtoName = NormalizeFullName(
            dtoType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
        );
        if (dtoLookup.TryGetValue(dtoName, out dtoStructure!))
        {
            return true;
        }

        var simpleName = dtoType.Name;
        var fallback = dtoLookup.FirstOrDefault(kvp =>
            kvp.Key.EndsWith($".{simpleName}", StringComparison.Ordinal)
            || kvp.Key.EndsWith($"::{simpleName}", StringComparison.Ordinal)
        );
        if (!string.IsNullOrEmpty(fallback.Key))
        {
            dtoStructure = fallback.Value;
            return true;
        }

        dtoStructure = default!;
        return false;
    }

    private static string NormalizeFullName(string name)
    {
        return name.StartsWith("global::", StringComparison.Ordinal) ? name : $"global::{name}";
    }

    private static string GenerateReverseConverterCode(
        ReverseConverterRequest request,
        DtoStructure structure
    )
    {
        var sourceTypeName = structure.SourceTypeFullName;
        var dtoTypeName = request.DtoType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var @namespace = request.ConverterSymbol.ContainingNamespace?.ToDisplayString() ?? "";
        var accessibility = GetAccessibilityString(request.ConverterSymbol);
        var staticClassKeyword = request.ConverterSymbol.IsStatic ? " static" : "";
        var className = request.ConverterSymbol.Name;
        var methodStaticModifier = request.IsStatic ? "static " : "";
        var indent1 = CodeFormatter.Indent(1);
        var indent2 = CodeFormatter.Indent(2);
        var indent3 = CodeFormatter.Indent(3);

        var assignments = GenerateReverseAssignments(structure, "dto");
        var assignmentLines = assignments.Count == 0
            ? ""
            : CodeFormatter.IndentCode(
                string.Join(CodeFormatter.DefaultNewLine, assignments),
                CodeFormatter.IndentSize * 2
            ) + CodeFormatter.DefaultNewLine;

        var classBody = $$"""
            {{indent1}}public {{methodStaticModifier}}{{sourceTypeName}} FromDto({{dtoTypeName}} dto)
            {{indent1}}{
            {{indent2}}if (dto is null) return default!;
            {{indent2}}var entity = new {{sourceTypeName}}();
            {{assignmentLines}}{{indent2}}return entity;
            {{indent1}}}

            {{indent1}}public {{methodStaticModifier}}IEnumerable<{{sourceTypeName}}> FromDtoProjection(IEnumerable<{{dtoTypeName}}> source)
            {{indent1}}{
            {{indent2}}if (source is null) yield break;
            {{indent2}}foreach (var item in source)
            {{indent2}}{
            {{indent3}}yield return FromDto(item);
            {{indent2}}}
            {{indent1}}}
            """;

        var classContent = $$"""
            {{indent1}}{{accessibility}}{{staticClassKeyword}} partial class {{className}}
            {{indent1}}{
            {{classBody}}
            {{indent1}}}
            """;

        var bodyWithNamespace = string.IsNullOrEmpty(@namespace)
            ? classContent
            : $$"""
                namespace {{@namespace}}
                {
                {{classContent}}
                }
                """;

        return $$"""
            // <auto-generated>
            // This file is auto-generated by Linqraft.
            // </auto-generated>
            #nullable enable
            #pragma warning disable IDE0060
            #pragma warning disable CS8601
            #pragma warning disable CS8602
            #pragma warning disable CS8603
            #pragma warning disable CS8604
            #pragma warning disable CS8618
            using System;
            using System.Linq;
            using System.Collections.Generic;

            {{bodyWithNamespace}}
            """;
    }

    private static List<string> GenerateReverseAssignments(DtoStructure structure, string dtoName)
    {
        var assignments = new List<string>();

        foreach (var property in structure.Properties)
        {
            if (!TryGetMemberPath(property.OriginalSyntax, out var path) || path.Count == 0)
            {
                continue;
            }

            var currentType = structure.SourceType;
            var currentExpr = "entity";
            var sb = new List<string>();
            var skip = false;

            for (int i = 0; i < path.Count - 1; i++)
            {
                var memberName = path[i];
                var memberSymbol = FindMemberSymbol(currentType, memberName);
                if (memberSymbol is null)
                {
                    skip = true;
                    break;
                }

                var memberType = memberSymbol switch
                {
                    IPropertySymbol propSym => propSym.Type,
                    IFieldSymbol fieldSym => fieldSym.Type,
                    _ => null
                };

                if (memberType is null || !CanInstantiate(memberType))
                {
                    skip = true;
                    break;
                }

                var targetTypeName = memberType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                targetTypeName = targetTypeName.EndsWith("?")
                    ? targetTypeName[..^1]
                    : targetTypeName;

                sb.Add($"{currentExpr}.{memberName} ??= new {targetTypeName}();");
                currentExpr = $"{currentExpr}.{memberName}";
                currentType = memberType;
            }

            if (skip)
            {
                continue;
            }

            var lastMember = path[^1];
            var lastSymbol = FindMemberSymbol(currentType, lastMember);
            if (lastSymbol is IPropertySymbol prop && prop.SetMethod is null)
            {
                continue;
            }
            if (lastSymbol is null)
            {
                continue;
            }

            sb.Add($"{currentExpr}.{lastMember} = {dtoName}.{property.Name};");
            assignments.AddRange(sb);
        }

        return assignments;
    }

    private static ISymbol? FindMemberSymbol(ITypeSymbol typeSymbol, string name)
    {
        return typeSymbol
            .GetMembers(name)
            .FirstOrDefault(m => m is IPropertySymbol || m is IFieldSymbol);
    }

    private static bool TryGetMemberPath(ExpressionSyntax syntax, out List<string> path)
    {
        path = new List<string>();

        if (syntax is IdentifierNameSyntax identifier)
        {
            path.Add(identifier.Identifier.Text);
            return true;
        }

        if (syntax is not MemberAccessExpressionSyntax memberAccess)
        {
            return false;
        }

        var current = memberAccess;
        while (true)
        {
            path.Insert(0, current.Name.Identifier.Text);
            if (current.Expression is MemberAccessExpressionSyntax innerAccess)
            {
                current = innerAccess;
                continue;
            }

            if (current.Expression is IdentifierNameSyntax)
            {
                // root parameter, ignore its name
                return path.Count > 0;
            }

            return false;
        }
    }

    private static bool CanInstantiate(ITypeSymbol typeSymbol)
    {
        var nonNullable = RoslynTypeHelper.GetNonNullableType(typeSymbol) ?? typeSymbol;
        if (nonNullable is not INamedTypeSymbol namedType)
            return false;
        if (!nonNullable.IsReferenceType)
            return false;
        if (namedType.IsAbstract)
            return false;
        if (namedType.SpecialType == SpecialType.System_String)
            return false;

        return namedType.InstanceConstructors.Any(ctor =>
            ctor.Parameters.Length == 0 && ctor.DeclaredAccessibility is not Accessibility.Private
        );
    }

    private static string GetAccessibilityString(INamedTypeSymbol typeSymbol)
    {
        return typeSymbol.DeclaredAccessibility switch
        {
            Accessibility.Public => "public",
            Accessibility.Internal => "internal",
            Accessibility.Private => "private",
            Accessibility.Protected => "protected",
            Accessibility.ProtectedAndInternal => "private protected",
            Accessibility.ProtectedOrInternal => "protected internal",
            _ => "public",
        };
    }
}

internal record ReverseConverterRequest
{
    public required INamedTypeSymbol ConverterSymbol { get; init; }
    public required ITypeSymbol DtoType { get; init; }
    public required bool IsStatic { get; init; }
}
