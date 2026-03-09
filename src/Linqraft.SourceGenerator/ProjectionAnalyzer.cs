using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Linqraft.Core.Configuration;
using Linqraft.Core.Documentation;
using Linqraft.Core.Utilities;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Linqraft.SourceGenerator;

internal sealed class ProjectionAnalyzer
{
    private readonly Compilation _compilation;
    private readonly LinqraftConfiguration _configuration;
    private readonly SourceProductionContext _context;
    private readonly CancellationToken _cancellationToken;
    private readonly Dictionary<string, GeneratedDtoModel> _generatedDtos = new(StringComparer.Ordinal);
    private readonly List<ProjectionRequest> _projectionRequests = new();
    private readonly List<MappingRequest> _mappingRequests = new();

    public ProjectionAnalyzer(
        Compilation compilation,
        LinqraftConfiguration configuration,
        SourceProductionContext context,
        CancellationToken cancellationToken
    )
    {
        _compilation = compilation;
        _configuration = configuration;
        _context = context;
        _cancellationToken = cancellationToken;
    }

    public IReadOnlyCollection<GeneratedDtoModel> GeneratedDtos => _generatedDtos.Values;

    public IReadOnlyList<ProjectionRequest> ProjectionRequests => _projectionRequests;

    public IReadOnlyList<MappingRequest> MappingRequests => _mappingRequests;

    public void AnalyzeSelectInvocations(ImmutableArray<InvocationExpressionSyntax> invocations)
    {
        foreach (var invocation in invocations.Distinct())
        {
            var request = AnalyzeProjectionInvocation(invocation, allowInterceptor: true);
            if (request is not null)
            {
                _projectionRequests.Add(request);
            }
        }
    }

    public void AnalyzeMappingClasses(ImmutableArray<ClassDeclarationSyntax> classes)
    {
        foreach (var declaration in classes.Distinct())
        {
            AnalyzeMappingClass(declaration);
        }
    }

    public void AnalyzeMappingMethods(ImmutableArray<MethodDeclarationSyntax> methods)
    {
        foreach (var declaration in methods.Distinct())
        {
            AnalyzeMappingMethod(declaration);
        }
    }

    private ProjectionRequest? AnalyzeProjectionInvocation(
        InvocationExpressionSyntax invocation,
        bool allowInterceptor
    )
    {
        _cancellationToken.ThrowIfCancellationRequested();

        if (!IsSelectExprInvocation(invocation))
        {
            return null;
        }

        if (allowInterceptor && IsInsideMappingDeclaration(invocation))
        {
            return null;
        }

        var semanticModel = _compilation.GetSemanticModel(invocation.SyntaxTree);
        var receiver = GetReceiverExpression(invocation);
        if (receiver is null)
        {
            return ReportInvalid(invocation, "Linqraft could not determine the SelectExpr receiver expression.");
        }

        var receiverType = semanticModel.GetTypeInfo(receiver, _cancellationToken).ConvertedType
            ?? semanticModel.GetTypeInfo(receiver, _cancellationToken).Type;
        var receiverKind = ResolveReceiverKind(receiverType);
        if (receiverKind is null)
        {
            return ReportInvalid(invocation, "SelectExpr is only supported on IQueryable<T> and IEnumerable<T> receivers.");
        }

        var lambda = invocation.ArgumentList.Arguments
            .Select(argument => argument.Expression)
            .OfType<LambdaExpressionSyntax>()
            .FirstOrDefault();

        if (lambda is null)
        {
            return ReportInvalid(invocation, "SelectExpr requires a lambda selector.");
        }

        var selectorBody = GetLambdaBodyExpression(lambda);
        if (selectorBody is null)
        {
            return ReportInvalid(invocation, "SelectExpr requires an expression-bodied selector.");
        }

        var nameSyntax = GetInvocationNameSyntax(invocation.Expression);
        var typeArguments = nameSyntax is GenericNameSyntax genericName
            ? genericName.TypeArgumentList.Arguments
            : default(SeparatedSyntaxList<TypeSyntax>);

        var pattern = ResolveProjectionPattern(selectorBody, typeArguments.Count);
        if (pattern is null)
        {
            return ReportInvalid(invocation, "Linqraft currently supports anonymous-object or object-initializer selectors.");
        }

        var sourceType = ResolveSourceType(semanticModel, receiverType, typeArguments);
        if (sourceType is null)
        {
            return ReportInvalid(invocation, "Linqraft could not determine the source element type for SelectExpr.");
        }

        var sourceTypeName = sourceType.ToFullyQualifiedTypeName();
        var captureEntries = AnalyzeCaptures(invocation, semanticModel);
        var callerNamespace = ResolveCallerNamespace(invocation, semanticModel);

        GeneratedDtoModel? rootDto = null;
        string resultTypeName;
        ProjectionObjectModel rootProjection;
        DocumentationInfo? rootDocumentation = null;

        switch (pattern.Value)
        {
            case ProjectionPattern.Anonymous:
            {
                var buildResult = BuildProjectionObject(
                    selectorBody,
                    semanticModel,
                    replacementTypeName: null,
                    dtoModel: null,
                    existingPropertyNames: null,
                    namedContext: false,
                    defaultNamespace: callerNamespace
                );
                rootProjection = buildResult.Projection;
                resultTypeName = "TResult";
                break;
            }
            case ProjectionPattern.ExplicitDto:
            {
                var resultTypeSyntax = typeArguments.Count >= 2 ? typeArguments[1] : typeArguments[0];
                rootDto = CreateRootDtoModel(resultTypeSyntax, semanticModel, invocation, sourceType as INamedTypeSymbol);
                var existingProperties = new HashSet<string>(
                    rootDto.Properties.Where(property => property.IsSuppressed).Select(property => property.Name),
                    StringComparer.Ordinal
                );
                rootDocumentation = DocumentationExtractor.GetTypeDocumentation(sourceType as INamedTypeSymbol, _configuration.CommentOutput);
                var buildResult = BuildProjectionObject(
                    selectorBody,
                    semanticModel,
                    replacementTypeName: rootDto.FullyQualifiedName,
                    dtoModel: rootDto,
                    existingPropertyNames: existingProperties,
                    namedContext: true,
                    defaultNamespace: rootDto.Namespace
                );
                rootProjection = buildResult.Projection;
                rootDto = buildResult.DtoModel!;
                resultTypeName = rootDto.FullyQualifiedName;
                RegisterDto(rootDto);
                break;
            }
            case ProjectionPattern.PredefinedDto:
            {
                var predefinedTypeSyntax = ((ObjectCreationExpressionSyntax)selectorBody).Type;
                resultTypeName = ResolveNamedType(predefinedTypeSyntax, semanticModel, callerNamespace)
                    ?? predefinedTypeSyntax.ToString();
                var buildResult = BuildProjectionObject(
                    selectorBody,
                    semanticModel,
                    replacementTypeName: null,
                    dtoModel: null,
                    existingPropertyNames: null,
                    namedContext: true,
                    defaultNamespace: callerNamespace
                );
                rootProjection = buildResult.Projection;
                break;
            }
            default:
                throw new InvalidOperationException("Unexpected projection pattern.");
        }

        var interceptableLocation = allowInterceptor
            ? semanticModel.GetInterceptableLocation(invocation, _cancellationToken)
            : null;

        var methodHash = HashingHelper.ComputeHash(
            $"{invocation.SyntaxTree.FilePath}|{invocation.SpanStart}|{selectorBody}",
            16
        );

        return new ProjectionRequest
        {
            HintName = $"SelectExpr_{methodHash}",
            MethodName = $"SelectExpr_{methodHash}",
            ReceiverKind = receiverKind.Value,
            Pattern = pattern.Value,
            Invocation = invocation,
            SourceTypeName = sourceTypeName,
            ResultTypeName = resultTypeName,
            SelectorParameterName = GetLambdaParameterName(lambda),
            UseObjectSelectorSignature = pattern.Value == ProjectionPattern.ExplicitDto,
            CanUsePrebuiltExpression =
                _configuration.UsePrebuildExpression
                && receiverKind == ReceiverKind.IQueryable
                && pattern != ProjectionPattern.Anonymous
                && captureEntries.Count == 0,
            InterceptableLocation = interceptableLocation,
            Captures = captureEntries,
            RootProjection = rootProjection,
            RootDocumentation = rootDocumentation,
        };
    }

    private void AnalyzeMappingClass(ClassDeclarationSyntax declaration)
    {
        var semanticModel = _compilation.GetSemanticModel(declaration.SyntaxTree);
        var classSymbol = semanticModel.GetDeclaredSymbol(declaration, _cancellationToken);
        if (classSymbol is null)
        {
            return;
        }

        if (!InheritsFromMappingDeclare(classSymbol))
        {
            return;
        }

        var methodName = GetMappingMethodName(classSymbol.GetAttributes());
        var defineMapping = declaration.Members.OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(method => method.Identifier.ValueText == "DefineMapping");

        if (defineMapping is null)
        {
            return;
        }

        var selectExpr = defineMapping.DescendantNodes().OfType<InvocationExpressionSyntax>().FirstOrDefault(IsSelectExprInvocation);
        if (selectExpr is null)
        {
            return;
        }

        var projection = AnalyzeProjectionInvocation(selectExpr, allowInterceptor: false);
        if (projection is null)
        {
            return;
        }

        if (projection.Captures.Count != 0)
        {
            // TODO: The public docs describe mapping-method generation but do not define how capture variables
            // should surface in the generated reusable method signature, so this clone currently rejects them.
            _context.ReportDiagnostic(
                Diagnostic.Create(
                    GeneratorDiagnostics.InvalidSelectExpr,
                    selectExpr.GetLocation(),
                    "Mapping generation currently requires SelectExpr declarations without capture variables."
                )
            );
            return;
        }

        _mappingRequests.Add(
            new MappingRequest
            {
                HintName = $"Mapping_{HashingHelper.ComputeHash(classSymbol.ToDisplayString(), 16)}",
                SourceNode = declaration,
                Namespace = SymbolNameHelper.GetNamespace(classSymbol.ContainingNamespace),
                ContainingTypeName = $"{classSymbol.Name}_{HashingHelper.ComputeHash(classSymbol.ToDisplayString(), 8)}",
                AccessibilityKeyword = SymbolNameHelper.GetAccessibilityKeyword(classSymbol.DeclaredAccessibility),
                MethodName = string.IsNullOrWhiteSpace(methodName) ? $"ProjectTo{classSymbol.BaseType?.TypeArguments.FirstOrDefault()?.Name}" : methodName!,
                ReceiverKind = projection.ReceiverKind,
                SourceTypeName = projection.SourceTypeName,
                ResultTypeName = projection.ResultTypeName,
                SelectorParameterName = projection.SelectorParameterName,
                CanUsePrebuiltExpression = projection.CanUsePrebuiltExpression,
                Captures = projection.Captures,
                RootProjection = projection.RootProjection,
            }
        );
    }

    private void AnalyzeMappingMethod(MethodDeclarationSyntax declaration)
    {
        var semanticModel = _compilation.GetSemanticModel(declaration.SyntaxTree);
        var methodSymbol = semanticModel.GetDeclaredSymbol(declaration, _cancellationToken);
        if (methodSymbol is null || methodSymbol.ContainingType is null)
        {
            return;
        }

        if (!methodSymbol.ContainingType.IsStatic || !methodSymbol.ContainingType.DeclaringSyntaxReferences.Any())
        {
            return;
        }

        var selectExpr = declaration.DescendantNodes().OfType<InvocationExpressionSyntax>().FirstOrDefault(IsSelectExprInvocation);
        if (selectExpr is null)
        {
            return;
        }

        var projection = AnalyzeProjectionInvocation(selectExpr, allowInterceptor: false);
        if (projection is null)
        {
            return;
        }

        if (projection.Captures.Count != 0)
        {
            _context.ReportDiagnostic(
                Diagnostic.Create(
                    GeneratorDiagnostics.InvalidSelectExpr,
                    selectExpr.GetLocation(),
                    "Mapping generation currently requires SelectExpr declarations without capture variables."
                )
            );
            return;
        }

        _mappingRequests.Add(
            new MappingRequest
            {
                HintName = $"Mapping_{HashingHelper.ComputeHash(methodSymbol.ToDisplayString(), 16)}",
                SourceNode = declaration,
                Namespace = SymbolNameHelper.GetNamespace(methodSymbol.ContainingType.ContainingNamespace),
                ContainingTypeName = methodSymbol.ContainingType.Name,
                AccessibilityKeyword = SymbolNameHelper.GetAccessibilityKeyword(methodSymbol.ContainingType.DeclaredAccessibility),
                MethodName = GetMappingMethodName(methodSymbol.GetAttributes()) ?? declaration.Identifier.ValueText,
                ReceiverKind = projection.ReceiverKind,
                SourceTypeName = projection.SourceTypeName,
                ResultTypeName = projection.ResultTypeName,
                SelectorParameterName = projection.SelectorParameterName,
                CanUsePrebuiltExpression = projection.CanUsePrebuiltExpression,
                Captures = projection.Captures,
                RootProjection = projection.RootProjection,
            }
        );
    }

    private ProjectionBuildResult BuildProjectionObject(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        string? replacementTypeName,
        GeneratedDtoModel? dtoModel,
        HashSet<string>? existingPropertyNames,
        bool namedContext,
        string defaultNamespace
    )
    {
        var members = GetProjectionMembers(expression).ToList();
        var replacementTypes = new Dictionary<SyntaxNode, string>();
        if (replacementTypeName is not null && expression is AnonymousObjectCreationExpressionSyntax anonymousRoot)
        {
            replacementTypes[anonymousRoot] = replacementTypeName;
        }

        var projectedMembers = new List<ProjectionMemberModel>(members.Count);

        foreach (var member in members)
        {
            var memberReplacements = new Dictionary<SyntaxNode, string>();
            var typeName = AnalyzeMemberType(
                member.Expression,
                member.Name,
                semanticModel,
                memberReplacements,
                namedContext,
                defaultNamespace
            );
            var documentation = DocumentationExtractor.GetExpressionDocumentation(
                member.Expression,
                semanticModel,
                _configuration.CommentOutput,
                _cancellationToken
            );
            var useEmptyCollectionFallback =
                dtoModel is not null
                && _configuration.ArrayNullabilityRemoval
                && QualifiesForCollectionNullabilityRemoval(member.Expression, semanticModel);

            foreach (var pair in memberReplacements)
            {
                replacementTypes[pair.Key] = pair.Value;
            }

            projectedMembers.Add(
                new ProjectionMemberModel
                {
                    Name = member.Name,
                    Expression = member.Expression,
                    TypeName = useEmptyCollectionFallback ? RemoveNullableAnnotation(typeName) : typeName,
                    UseEmptyCollectionFallback = useEmptyCollectionFallback,
                    Documentation = documentation,
                    ReplacementTypes = memberReplacements,
                }
            );
        }

        var projection = new ProjectionObjectModel
        {
            Syntax = expression,
            ReplacementTypeName = replacementTypeName,
            Members = projectedMembers,
            ReplacementTypes = replacementTypes,
        };

        if (dtoModel is null)
        {
            return new ProjectionBuildResult
            {
                Projection = projection,
                DtoModel = null,
            };
        }

        var properties = projectedMembers.Select(
            member =>
                new GeneratedPropertyModel
                {
                    Name = member.Name,
                    TypeName = member.TypeName,
                    Documentation = member.Documentation,
                    IsSuppressed = existingPropertyNames?.Contains(member.Name) == true,
                }
        ).ToList();

        return new ProjectionBuildResult
        {
            Projection = projection,
            DtoModel = dtoModel with
            {
                Properties = properties,
                ShapeSignature =
                    $"{dtoModel.FullyQualifiedName}|{string.Join(";", properties.Where(property => !property.IsSuppressed).Select(property => $"{property.Name}:{property.TypeName}"))}",
            },
        };
    }

    private string AnalyzeMemberType(
        ExpressionSyntax expression,
        string memberName,
        SemanticModel semanticModel,
        Dictionary<SyntaxNode, string> replacementTypes,
        bool namedContext,
        string defaultNamespace
    )
    {
        if (expression is ConditionalExpressionSyntax conditionalExpression && TryGetNullGuardBranch(conditionalExpression, out var nonNullBranch))
        {
            var branchType = AnalyzeMemberType(
                nonNullBranch,
                memberName,
                semanticModel,
                replacementTypes,
                namedContext,
                defaultNamespace
            );
            return MakeNullable(branchType);
        }

        if (expression is AnonymousObjectCreationExpressionSyntax anonymousObject)
        {
            if (!namedContext)
            {
                var inferred = semanticModel.GetTypeInfo(expression, _cancellationToken).Type;
                return inferred?.ToFullyQualifiedTypeName() ?? "object";
            }

            var nestedDto = CreateNestedDtoModel(memberName, anonymousObject, defaultNamespace);
            replacementTypes[anonymousObject] = nestedDto.FullyQualifiedName;
            var nestedBuildResult = BuildProjectionObject(
                anonymousObject,
                semanticModel,
                nestedDto.FullyQualifiedName,
                nestedDto,
                existingPropertyNames: null,
                namedContext: true,
                defaultNamespace: nestedDto.Namespace
            );
            nestedDto = nestedBuildResult.DtoModel!;
            RegisterDto(nestedDto);
            return nestedDto.FullyQualifiedName;
        }

        if (expression is ObjectCreationExpressionSyntax namedObject)
        {
            if (namedObject.Initializer is not null)
            {
                foreach (var nestedMember in GetProjectionMembers(namedObject))
                {
                    AnalyzeMemberType(
                        nestedMember.Expression,
                        nestedMember.Name,
                        semanticModel,
                        replacementTypes,
                        namedContext: true,
                        defaultNamespace
                    );
                }
            }

            var typeSymbol = semanticModel.GetTypeInfo(namedObject.Type, _cancellationToken).Type;
            return ResolveNamedType(namedObject.Type, semanticModel, defaultNamespace)
                ?? typeSymbol?.ToFullyQualifiedTypeName()
                ?? namedObject.Type.ToString();
        }

        if (TryGetCollectionProjection(expression, semanticModel, out var collectionProjection))
        {
            if (collectionProjection.IsNestedExplicitDto)
            {
                var nestedResultType = ResolveNamedType(collectionProjection.NestedResultTypeSyntax!, semanticModel, defaultNamespace);
                if (nestedResultType is not null)
                {
                    if (collectionProjection.LambdaBody is AnonymousObjectCreationExpressionSyntax nestedAnonymous)
                    {
                        var nestedDto = CreateRootDtoModel(
                            collectionProjection.NestedResultTypeSyntax!,
                            semanticModel,
                            collectionProjection.Invocation,
                            semanticModel.GetTypeInfo(collectionProjection.SourceTypeSyntax!, _cancellationToken).Type
                                as INamedTypeSymbol
                        );
                        replacementTypes[nestedAnonymous] = nestedDto.FullyQualifiedName;
                        var nestedBuildResult = BuildProjectionObject(
                            nestedAnonymous,
                            semanticModel,
                            nestedDto.FullyQualifiedName,
                            nestedDto,
                            new HashSet<string>(),
                            namedContext: true,
                            defaultNamespace: nestedDto.Namespace
                        );
                        nestedDto = nestedBuildResult.DtoModel!;
                        RegisterDto(nestedDto);
                        return BuildCollectionTypeName(collectionProjection.ExpressionType, nestedDto.FullyQualifiedName);
                    }

                    return BuildCollectionTypeName(collectionProjection.ExpressionType, nestedResultType);
                }
            }

            if (collectionProjection.LambdaBody is AnonymousObjectCreationExpressionSyntax anonymousBody && namedContext)
            {
                var nestedDto = CreateNestedDtoModel(memberName, anonymousBody, defaultNamespace);
                replacementTypes[anonymousBody] = nestedDto.FullyQualifiedName;
                var nestedBuildResult = BuildProjectionObject(
                    anonymousBody,
                    semanticModel,
                    nestedDto.FullyQualifiedName,
                    nestedDto,
                    existingPropertyNames: null,
                    namedContext: true,
                    defaultNamespace: nestedDto.Namespace
                );
                nestedDto = nestedBuildResult.DtoModel!;
                RegisterDto(nestedDto);
                return BuildCollectionTypeName(collectionProjection.ExpressionType, nestedDto.FullyQualifiedName);
            }

            if (collectionProjection.LambdaBody is ObjectCreationExpressionSyntax namedBody)
            {
                foreach (var nestedMember in GetProjectionMembers(namedBody))
                {
                    AnalyzeMemberType(
                        nestedMember.Expression,
                        nestedMember.Name,
                        semanticModel,
                        replacementTypes,
                        namedContext: true,
                        defaultNamespace
                    );
                }

                var elementType = semanticModel.GetTypeInfo(namedBody.Type, _cancellationToken).Type;
                return BuildCollectionTypeName(
                    collectionProjection.ExpressionType,
                    ResolveNamedType(namedBody.Type, semanticModel, defaultNamespace)
                        ?? elementType?.ToFullyQualifiedTypeName()
                        ?? namedBody.Type.ToString()
                );
            }

            var lambdaBodyType = semanticModel.GetTypeInfo(collectionProjection.LambdaBody, _cancellationToken).Type;
            return BuildCollectionTypeName(
                collectionProjection.ExpressionType,
                lambdaBodyType?.ToFullyQualifiedTypeName() ?? "object"
            );
        }

        var type = semanticModel.GetTypeInfo(expression, _cancellationToken).ConvertedType
            ?? semanticModel.GetTypeInfo(expression, _cancellationToken).Type;
        return type?.ToFullyQualifiedTypeName() ?? "object";
    }

    private GeneratedDtoModel CreateNestedDtoModel(
        string memberName,
        AnonymousObjectCreationExpressionSyntax syntax,
        string defaultNamespace
    )
    {
        var dtoBaseName = memberName.EndsWith("Dto", StringComparison.Ordinal)
            ? memberName
            : $"{memberName}Dto";
        var signature = $"{defaultNamespace}|{dtoBaseName}|{NormalizeWhitespace(syntax)}";
        var hash = HashingHelper.ComputeHash(signature, 8);
        var dtoNamespace = _configuration.NestedDtoUseHashNamespace
            ? string.IsNullOrWhiteSpace(defaultNamespace)
                ? $"LinqraftGenerated_{hash}"
                : $"{defaultNamespace}.LinqraftGenerated_{hash}"
            : defaultNamespace;
        var dtoName = _configuration.NestedDtoUseHashNamespace ? dtoBaseName : $"{dtoBaseName}_{hash}";
        var fullyQualifiedName = string.IsNullOrWhiteSpace(dtoNamespace)
            ? $"global::{dtoName}"
            : $"global::{dtoNamespace}.{dtoName}";

        return new GeneratedDtoModel
        {
            Key = fullyQualifiedName,
            Namespace = dtoNamespace,
            Name = dtoName,
            FullyQualifiedName = fullyQualifiedName,
            AccessibilityKeyword = "public",
            IsRecord = _configuration.RecordGenerate,
            IsRoot = false,
            IsAutoGeneratedNested = true,
            Documentation = null,
            ShapeSignature = signature,
            ContainingTypes = Array.Empty<ContainingTypeInfo>(),
            Properties = new List<GeneratedPropertyModel>(),
        };
    }

    private GeneratedDtoModel CreateRootDtoModel(
        TypeSyntax resultTypeSyntax,
        SemanticModel semanticModel,
        InvocationExpressionSyntax invocation,
        INamedTypeSymbol? sourceType
    )
    {
        var resultType = semanticModel.GetTypeInfo(resultTypeSyntax, _cancellationToken).Type as INamedTypeSymbol;
        if (resultType is IErrorTypeSymbol)
        {
            resultType = null;
        }
        var namespaceName = resultType is not null && !resultType.ContainingNamespace.IsGlobalNamespace
            ? SymbolNameHelper.GetNamespace(resultType.ContainingNamespace)
            : ResolveCallerNamespace(invocation, semanticModel);

        if (string.IsNullOrWhiteSpace(namespaceName))
        {
            namespaceName = _configuration.GlobalNamespace;
        }

        var dtoName = resultType?.Name ?? GetUnqualifiedTypeName(resultTypeSyntax);
        var containingTypePrefix = resultType?.ContainingType is null
            ? string.Empty
            : $"{string.Join(".", GetContainingTypes(resultType).Select(type => type.Name))}.";
        var fullyQualifiedName = string.IsNullOrWhiteSpace(namespaceName)
            ? $"global::{containingTypePrefix}{dtoName}"
            : $"global::{namespaceName}.{containingTypePrefix}{dtoName}";
        var existingPropertyNames = resultType?.GetMembers()
            .OfType<IPropertySymbol>()
            .Select(property => property.Name)
            .Distinct(StringComparer.Ordinal)
            .ToList()
            ?? new List<string>();

        var dto = new GeneratedDtoModel
        {
            Key = fullyQualifiedName,
            Namespace = namespaceName,
            Name = dtoName,
            FullyQualifiedName = fullyQualifiedName,
            AccessibilityKeyword = resultType is null
                ? "public"
                : SymbolNameHelper.GetAccessibilityKeyword(resultType.DeclaredAccessibility),
            IsRecord = resultType?.IsRecord == true || (resultType is null && _configuration.RecordGenerate),
            IsRoot = true,
            IsAutoGeneratedNested = false,
            Documentation = DocumentationExtractor.GetTypeDocumentation(sourceType, _configuration.CommentOutput),
            ShapeSignature = $"{fullyQualifiedName}|{GetUnqualifiedTypeName(resultTypeSyntax)}",
            ContainingTypes = GetContainingTypes(resultType),
            Properties = existingPropertyNames
                .Select(
                    name =>
                        new GeneratedPropertyModel
                        {
                            Name = name,
                            TypeName = string.Empty,
                            Documentation = null,
                            IsSuppressed = true,
                        }
                )
                .ToList(),
        };

        return dto;
    }

    private void RegisterDto(GeneratedDtoModel dtoModel)
    {
        if (_generatedDtos.TryGetValue(dtoModel.Key, out var existing))
        {
            if (!string.Equals(existing.ShapeSignature, dtoModel.ShapeSignature, StringComparison.Ordinal))
            {
                _context.ReportDiagnostic(
                    Diagnostic.Create(
                        GeneratorDiagnostics.ConflictingDtoShape,
                        Location.None,
                        dtoModel.Name,
                        "The public docs do not define how multiple incompatible projections should merge into one named DTO."
                    )
                );
            }

            return;
        }

        _generatedDtos.Add(dtoModel.Key, dtoModel);
    }

    private IReadOnlyList<(string Name, ExpressionSyntax Expression)> GetProjectionMembers(ExpressionSyntax expression)
    {
        return expression switch
        {
            AnonymousObjectCreationExpressionSyntax anonymousObject => anonymousObject.Initializers
                .Select(initializer => (GetAnonymousMemberName(initializer), initializer.Expression))
                .ToList(),
            ObjectCreationExpressionSyntax objectCreation when objectCreation.Initializer is not null => objectCreation.Initializer.Expressions
                .OfType<AssignmentExpressionSyntax>()
                .Select(
                    assignment =>
                    (
                        assignment.Left switch
                        {
                            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
                            _ => assignment.Left.ToString(),
                        },
                        assignment.Right
                    )
                )
                .ToList(),
            _ => Array.Empty<(string Name, ExpressionSyntax Expression)>(),
        };
    }

    private bool TryGetCollectionProjection(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        out CollectionProjectionInfo projectionInfo
    )
    {
        if (!TryFindProjectionInvocation(expression, out var invocation))
        {
            projectionInfo = default;
            return false;
        }

        var lambda = invocation.ArgumentList.Arguments
            .Select(argument => argument.Expression)
            .OfType<LambdaExpressionSyntax>()
            .FirstOrDefault();
        if (lambda is null)
        {
            projectionInfo = default;
            return false;
        }

        var body = GetLambdaBodyExpression(lambda);
        if (body is null)
        {
            projectionInfo = default;
            return false;
        }

        var nameSyntax = GetInvocationNameSyntax(invocation.Expression);
        var genericArgs = nameSyntax is GenericNameSyntax genericName
            ? genericName.TypeArgumentList.Arguments
            : default(SeparatedSyntaxList<TypeSyntax>);

        projectionInfo = new CollectionProjectionInfo
        {
            Invocation = invocation,
            LambdaBody = body,
            ExpressionType = semanticModel.GetTypeInfo(expression, _cancellationToken).Type,
            IsNestedExplicitDto = IsSelectExprInvocation(invocation) && genericArgs.Count >= 2 && body is AnonymousObjectCreationExpressionSyntax,
            NestedResultTypeSyntax = genericArgs.Count >= 2 ? genericArgs[1] : null,
            SourceTypeSyntax = genericArgs.Count >= 1 ? genericArgs[0] : null,
        };

        return true;
    }

    private static bool TryFindProjectionInvocation(
        ExpressionSyntax expression,
        out InvocationExpressionSyntax invocation
    )
    {
        switch (expression)
        {
            case InvocationExpressionSyntax directInvocation when IsProjectionInvocation(directInvocation):
                invocation = directInvocation;
                return true;
            case InvocationExpressionSyntax nestedInvocation
                when nestedInvocation.Expression is MemberAccessExpressionSyntax memberAccess:
                return TryFindProjectionInvocation(memberAccess.Expression, out invocation);
            case MemberAccessExpressionSyntax memberAccess:
                return TryFindProjectionInvocation(memberAccess.Expression, out invocation);
            case ConditionalAccessExpressionSyntax conditionalAccess
                when conditionalAccess.WhenNotNull is InvocationExpressionSyntax whenNotNullInvocation
                    && IsProjectionInvocation(whenNotNullInvocation):
                invocation = whenNotNullInvocation;
                return true;
            case ConditionalAccessExpressionSyntax conditionalAccess:
                if (conditionalAccess.WhenNotNull is ExpressionSyntax whenNotNull
                    && TryFindProjectionInvocation(whenNotNull, out invocation))
                {
                    return true;
                }

                invocation = null!;
                return false;
            default:
                invocation = null!;
                return false;
        }
    }

    private static bool IsProjectionInvocation(InvocationExpressionSyntax invocation)
    {
        var name = GetInvocationName(invocation.Expression);
        return name is "Select" or "SelectMany" or "SelectExpr";
    }

    private static ProjectionPattern? ResolveProjectionPattern(ExpressionSyntax selectorBody, int typeArgumentCount)
    {
        if (selectorBody is AnonymousObjectCreationExpressionSyntax)
        {
            return typeArgumentCount == 0 ? ProjectionPattern.Anonymous : ProjectionPattern.ExplicitDto;
        }

        if (selectorBody is ObjectCreationExpressionSyntax)
        {
            return ProjectionPattern.PredefinedDto;
        }

        return null;
    }

    private static ExpressionSyntax? GetLambdaBodyExpression(LambdaExpressionSyntax lambda)
    {
        return lambda.Body as ExpressionSyntax;
    }

    private static string GetLambdaParameterName(LambdaExpressionSyntax lambda)
    {
        return lambda switch
        {
            SimpleLambdaExpressionSyntax simple => simple.Parameter.Identifier.ValueText,
            ParenthesizedLambdaExpressionSyntax parenthesized => parenthesized.ParameterList.Parameters.FirstOrDefault()?.Identifier.ValueText
                ?? "x",
            _ => "x",
        };
    }

    private static ReceiverKind? ResolveReceiverKind(ITypeSymbol? receiverType)
    {
        if (SymbolNameHelper.IsQueryable(receiverType))
        {
            return ReceiverKind.IQueryable;
        }

        if (SymbolNameHelper.IsEnumerable(receiverType))
        {
            return ReceiverKind.IEnumerable;
        }

        return null;
    }

    private static ITypeSymbol? ResolveSourceType(
        SemanticModel semanticModel,
        ITypeSymbol? receiverType,
        SeparatedSyntaxList<TypeSyntax> typeArguments
    )
    {
        if (typeArguments.Count >= 2)
        {
            return semanticModel.GetTypeInfo(typeArguments[0]).Type;
        }

        if (receiverType is INamedTypeSymbol namedType)
        {
            if (namedType.TypeArguments.Length == 1)
            {
                return namedType.TypeArguments[0];
            }

            var interfaceType = namedType.AllInterfaces.FirstOrDefault(candidate =>
                candidate.TypeArguments.Length == 1
                && (candidate.ConstructedFrom.ToDisplayString() == "System.Linq.IQueryable<T>"
                    || candidate.ConstructedFrom.ToDisplayString() == "System.Collections.Generic.IEnumerable<T>")
            );
            return interfaceType?.TypeArguments[0];
        }

        return null;
    }

    private IReadOnlyList<CaptureEntryModel> AnalyzeCaptures(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel
    )
    {
        var captureArgument = invocation.ArgumentList.Arguments
            .FirstOrDefault(argument =>
                argument.NameColon?.Name.Identifier.ValueText == "capture"
                || (!argument.Equals(invocation.ArgumentList.Arguments.First()) && argument.Expression is AnonymousObjectCreationExpressionSyntax)
            );

        if (captureArgument?.Expression is not AnonymousObjectCreationExpressionSyntax anonymousCapture)
        {
            return Array.Empty<CaptureEntryModel>();
        }

        return anonymousCapture.Initializers
            .Select(
                initializer =>
                {
                    var propertyName = GetAnonymousMemberName(initializer);
                    var localName = initializer.Expression switch
                    {
                        IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
                        _ => propertyName,
                    };
                    var type = semanticModel.GetTypeInfo(initializer.Expression, _cancellationToken).Type;
                    return new CaptureEntryModel
                    {
                        PropertyName = propertyName,
                        LocalName = localName,
                        TypeName = type?.ToFullyQualifiedTypeName() ?? "object",
                    };
                }
            )
            .ToArray();
    }

    private bool QualifiesForCollectionNullabilityRemoval(
        ExpressionSyntax expression,
        SemanticModel semanticModel
    )
    {
        if (expression.DescendantNodesAndSelf().OfType<ConditionalExpressionSyntax>().Any())
        {
            return false;
        }

        if (!expression.DescendantNodesAndSelf().OfType<ConditionalAccessExpressionSyntax>().Any())
        {
            return false;
        }

        if (!expression.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>().Any(IsProjectionInvocation))
        {
            return false;
        }

        var type = semanticModel.GetTypeInfo(expression, _cancellationToken).Type;
        return SymbolNameHelper.IsEnumerable(type);
    }

    private static string BuildCollectionTypeName(ITypeSymbol? collectionType, string elementTypeName)
    {
        if (collectionType is IArrayTypeSymbol arrayType)
        {
            return $"{elementTypeName}[]";
        }

        if (collectionType is INamedTypeSymbol namedType)
        {
            if (namedType.IsGenericType && namedType.TypeArguments.Length == 1)
            {
                var container = namedType.ConstructedFrom.ToFullyQualifiedTypeName();
                var containerName = container[..container.IndexOf('<')];
                return $"{containerName}<{elementTypeName}>{GetNullableSuffix(namedType.NullableAnnotation)}";
            }
        }

        return $"global::System.Collections.Generic.IEnumerable<{elementTypeName}>";
    }

    private static string RemoveNullableAnnotation(string typeName)
    {
        return typeName.EndsWith("?", StringComparison.Ordinal) ? typeName[..^1] : typeName;
    }

    private static string MakeNullable(string typeName)
    {
        return typeName.EndsWith("?", StringComparison.Ordinal) ? typeName : $"{typeName}?";
    }

    private static bool TryGetNullGuardBranch(
        ConditionalExpressionSyntax conditionalExpression,
        out ExpressionSyntax nonNullBranch
    )
    {
        if (conditionalExpression.WhenTrue.IsKind(SyntaxKind.NullLiteralExpression))
        {
            nonNullBranch = conditionalExpression.WhenFalse;
            return true;
        }

        if (conditionalExpression.WhenFalse.IsKind(SyntaxKind.NullLiteralExpression))
        {
            nonNullBranch = conditionalExpression.WhenTrue;
            return true;
        }

        nonNullBranch = null!;
        return false;
    }

    private static string ResolveCallerNamespace(
        SyntaxNode node,
        SemanticModel semanticModel
    )
    {
        var symbol = semanticModel.GetEnclosingSymbol(node.SpanStart);
        var namespaceName = SymbolNameHelper.GetNamespace(symbol?.ContainingNamespace);
        return namespaceName;
    }

    private static string? ResolveNamedType(
        TypeSyntax typeSyntax,
        SemanticModel semanticModel,
        string fallbackNamespace
    )
    {
        var type = semanticModel.GetTypeInfo(typeSyntax).Type;
        if (type is not null)
        {
            return type.ToFullyQualifiedTypeName();
        }

        var name = GetUnqualifiedTypeName(typeSyntax);
        return string.IsNullOrWhiteSpace(fallbackNamespace)
            ? $"global::{name}"
            : $"global::{fallbackNamespace}.{name}";
    }

    private static string GetNullableSuffix(NullableAnnotation nullableAnnotation)
    {
        return nullableAnnotation == NullableAnnotation.Annotated ? "?" : string.Empty;
    }

    private ProjectionRequest? ReportInvalid(InvocationExpressionSyntax invocation, string message)
    {
        _context.ReportDiagnostic(
            Diagnostic.Create(GeneratorDiagnostics.InvalidSelectExpr, invocation.GetLocation(), message)
        );
        return null;
    }

    private static bool IsSelectExprInvocation(InvocationExpressionSyntax invocation)
    {
        return string.Equals(GetInvocationName(invocation.Expression), "SelectExpr", StringComparison.Ordinal);
    }

    private static bool IsInsideMappingDeclaration(SyntaxNode node)
    {
        return node.Ancestors().OfType<MemberDeclarationSyntax>().Any(
            member => member.AttributeLists.SelectMany(list => list.Attributes).Any(
                attribute => attribute.Name.ToString().Contains("LinqraftMappingGenerate", StringComparison.Ordinal)
            )
        );
    }

    private static ExpressionSyntax? GetReceiverExpression(InvocationExpressionSyntax invocation)
    {
        return invocation.Expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Expression,
            MemberBindingExpressionSyntax => null,
            _ => null,
        };
    }

    private static string GetInvocationName(ExpressionSyntax expression)
    {
        return GetInvocationNameSyntax(expression)?.Identifier.ValueText ?? string.Empty;
    }

    private static SimpleNameSyntax? GetInvocationNameSyntax(ExpressionSyntax expression)
    {
        return expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name,
            IdentifierNameSyntax identifier => identifier,
            GenericNameSyntax genericName => genericName,
            _ => null,
        };
    }

    private static string GetAnonymousMemberName(AnonymousObjectMemberDeclaratorSyntax initializer)
    {
        if (initializer.NameEquals is not null)
        {
            return initializer.NameEquals.Name.Identifier.ValueText;
        }

        return initializer.Expression switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText,
            _ => initializer.Expression.ToString(),
        };
    }

    private static bool InheritsFromMappingDeclare(INamedTypeSymbol symbol)
    {
        var current = symbol.BaseType;
        while (current is not null)
        {
            if (current.Name == "LinqraftMappingDeclare")
            {
                return true;
            }

            current = current.BaseType;
        }

        return false;
    }

    private static string? GetMappingMethodName(ImmutableArray<AttributeData> attributes)
    {
        return attributes
            .FirstOrDefault(attribute => attribute.AttributeClass?.Name == "LinqraftMappingGenerateAttribute")
            ?.ConstructorArguments.FirstOrDefault().Value as string;
    }

    private static IReadOnlyList<ContainingTypeInfo> GetContainingTypes(INamedTypeSymbol? symbol)
    {
        if (symbol?.ContainingType is null)
        {
            return Array.Empty<ContainingTypeInfo>();
        }

        var stack = new Stack<ContainingTypeInfo>();
        var current = symbol.ContainingType;
        while (current is not null)
        {
            stack.Push(
                new ContainingTypeInfo
                {
                    AccessibilityKeyword = SymbolNameHelper.GetAccessibilityKeyword(current.DeclaredAccessibility),
                    DeclarationKeyword = current.IsRecord ? "record" : "class",
                    Name = current.Name,
                }
            );
            current = current.ContainingType;
        }

        return stack.ToArray();
    }

    private static string NormalizeWhitespace(SyntaxNode node)
    {
        return string.Join(
            " ",
            node.NormalizeWhitespace().ToFullString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
        );
    }

    private static string GetUnqualifiedTypeName(TypeSyntax typeSyntax)
    {
        return typeSyntax switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            QualifiedNameSyntax qualifiedName => qualifiedName.Right.Identifier.ValueText,
            GenericNameSyntax genericName => genericName.Identifier.ValueText,
            AliasQualifiedNameSyntax aliasQualified => aliasQualified.Name.Identifier.ValueText,
            _ => typeSyntax.ToString().Split('.').Last(),
        };
    }

    private readonly record struct CollectionProjectionInfo
    {
        public required InvocationExpressionSyntax Invocation { get; init; }

        public required ExpressionSyntax LambdaBody { get; init; }

        public required ITypeSymbol? ExpressionType { get; init; }

        public required bool IsNestedExplicitDto { get; init; }

        public required TypeSyntax? NestedResultTypeSyntax { get; init; }

        public required TypeSyntax? SourceTypeSyntax { get; init; }
    }

    private readonly record struct ProjectionBuildResult
    {
        public required ProjectionObjectModel Projection { get; init; }

        public required GeneratedDtoModel? DtoModel { get; init; }
    }
}
