using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Linqraft.Core.Formatting;
using Linqraft.Core.Pipeline.Analysis;
using Linqraft.Core.Pipeline.Parsing;
using Linqraft.Core.RoslynHelpers;
using Linqraft.Core.SyntaxHelpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Linqraft.Core.Pipeline.Generation;

/// <summary>
/// Pipeline processor for SelectExpr code generation.
/// Processes SelectExpr invocations through the pipeline stages and generates code.
/// This is the primary entry point for pipeline-based code generation.
/// </summary>
internal class SelectExprPipelineProcessor
{
    private readonly CodeGenerationPipeline _pipeline;
    private readonly SemanticModel _semanticModel;
    private readonly LinqraftConfiguration _configuration;

    /// <summary>
    /// Creates a new SelectExpr pipeline processor.
    /// </summary>
    public SelectExprPipelineProcessor(SemanticModel semanticModel, LinqraftConfiguration configuration)
    {
        _semanticModel = semanticModel;
        _configuration = configuration;
        _pipeline = new CodeGenerationPipeline(semanticModel, configuration);
    }

    /// <summary>
    /// Gets the underlying pipeline.
    /// </summary>
    public CodeGenerationPipeline Pipeline => _pipeline;

    /// <summary>
    /// Processes an anonymous type SelectExpr and generates code.
    /// </summary>
    public SelectExprProcessingResult ProcessAnonymous(
        InvocationExpressionSyntax invocation,
        AnonymousObjectCreationExpressionSyntax anonymousObj,
        ITypeSymbol sourceType,
        string lambdaParameterName,
        string callerNamespace,
        ExpressionSyntax? captureArgumentExpression = null,
        ITypeSymbol? captureArgumentType = null)
    {
        // Analyze the anonymous type to create a DtoStructure
        var dtoStructure = _pipeline.AnalyzeAnonymousType(anonymousObj, sourceType, null);
        if (dtoStructure is null || dtoStructure.Properties.Count == 0)
        {
            return SelectExprProcessingResult.Empty;
        }

        // Generate DTO classes
        var dtoClassInfos = GenerateDtoClasses(dtoStructure, sourceType, callerNamespace);

        // Get the root DTO class name
        var rootDtoClassName = GetRootDtoClassName(dtoStructure);

        return new SelectExprProcessingResult
        {
            DtoStructure = dtoStructure,
            DtoClassInfos = dtoClassInfos,
            RootDtoClassName = rootDtoClassName,
            IsAnonymous = true,
            SourceType = sourceType,
            LambdaParameterName = lambdaParameterName,
            CallerNamespace = callerNamespace
        };
    }

    /// <summary>
    /// Processes a named type SelectExpr and generates code.
    /// </summary>
    public SelectExprProcessingResult ProcessNamed(
        InvocationExpressionSyntax invocation,
        ObjectCreationExpressionSyntax objectCreation,
        ITypeSymbol sourceType,
        string lambdaParameterName,
        string callerNamespace,
        ExpressionSyntax? captureArgumentExpression = null,
        ITypeSymbol? captureArgumentType = null)
    {
        // Analyze the named type to create a DtoStructure
        var dtoStructure = _pipeline.AnalyzeNamedType(objectCreation, sourceType, null);
        if (dtoStructure is null || dtoStructure.Properties.Count == 0)
        {
            return SelectExprProcessingResult.Empty;
        }

        // For named types, get the actual type name from the semantic model
        var typeInfo = _semanticModel.GetTypeInfo(objectCreation);
        var dtoType = typeInfo.Type ?? typeInfo.ConvertedType;
        var dtoClassName = dtoType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? "UnknownDto";

        // Generate DTO classes for nested structures only
        var dtoClassInfos = GenerateNestedDtoClasses(dtoStructure, callerNamespace);

        return new SelectExprProcessingResult
        {
            DtoStructure = dtoStructure,
            DtoClassInfos = dtoClassInfos,
            RootDtoClassName = dtoClassName,
            IsAnonymous = false,
            SourceType = sourceType,
            LambdaParameterName = lambdaParameterName,
            CallerNamespace = callerNamespace
        };
    }

    /// <summary>
    /// Processes an explicit DTO SelectExpr (SelectExpr&lt;TIn, TResult&gt;) and generates code.
    /// </summary>
    public SelectExprProcessingResult ProcessExplicitDto(
        InvocationExpressionSyntax invocation,
        AnonymousObjectCreationExpressionSyntax anonymousObj,
        ITypeSymbol sourceType,
        ITypeSymbol? tResultType,
        string explicitDtoName,
        string targetNamespace,
        string lambdaParameterName,
        string callerNamespace,
        List<string> parentClasses,
        ExpressionSyntax? captureArgumentExpression = null,
        ITypeSymbol? captureArgumentType = null)
    {
        // Analyze the anonymous type to create a DtoStructure
        var dtoStructure = _pipeline.AnalyzeAnonymousType(anonymousObj, sourceType, explicitDtoName);
        if (dtoStructure is null || dtoStructure.Properties.Count == 0)
        {
            return SelectExprProcessingResult.Empty;
        }

        // Generate DTO classes with explicit name
        var dtoClassInfos = GenerateExplicitDtoClasses(
            dtoStructure,
            explicitDtoName,
            targetNamespace,
            parentClasses,
            tResultType);

        // Build the fully qualified DTO name
        var rootDtoClassName = BuildFullyQualifiedDtoName(
            explicitDtoName,
            targetNamespace,
            parentClasses);

        return new SelectExprProcessingResult
        {
            DtoStructure = dtoStructure,
            DtoClassInfos = dtoClassInfos,
            RootDtoClassName = rootDtoClassName,
            IsAnonymous = false,
            IsExplicitDto = true,
            SourceType = sourceType,
            LambdaParameterName = lambdaParameterName,
            CallerNamespace = callerNamespace
        };
    }

    /// <summary>
    /// Generates property assignment code for a DtoProperty.
    /// </summary>
    public string GeneratePropertyAssignment(
        DtoProperty property,
        int indents,
        string lambdaParameterName,
        string callerNamespace,
        DtoStructure rootStructure)
    {
        // Delegate to PropertyAssignmentGenerator
        return _pipeline.PropertyAssignmentGenerator.GeneratePropertyAssignment(
            property, indents, lambdaParameterName, callerNamespace);
    }

    /// <summary>
    /// Generates the Select expression body for an interceptor method.
    /// </summary>
    public string GenerateSelectBody(
        DtoStructure structure,
        string dtoClassName,
        string lambdaParameterName,
        int baseIndent)
    {
        var spaces = CodeFormatter.IndentSpaces(baseIndent);
        var innerSpaces = CodeFormatter.IndentSpaces(baseIndent + CodeFormatter.IndentSize);

        var sb = new StringBuilder();
        sb.AppendLine($"new {dtoClassName}");
        sb.AppendLine($"{spaces}{{");

        foreach (var prop in structure.Properties)
        {
            var assignment = _pipeline.PropertyAssignmentGenerator.GeneratePropertyAssignment(
                prop, baseIndent + CodeFormatter.IndentSize, lambdaParameterName, "");
            sb.AppendLine($"{innerSpaces}{prop.Name} = {assignment},");
        }

        sb.Append($"{spaces}}}");
        return sb.ToString();
    }

    /// <summary>
    /// Generates DTO classes from a structure (for anonymous types).
    /// </summary>
    private List<GenerateDtoClassInfo> GenerateDtoClasses(
        DtoStructure structure,
        ITypeSymbol sourceType,
        string callerNamespace)
    {
        var result = new List<GenerateDtoClassInfo>();

        // Generate the root DTO class
        var className = GetDtoClassName(structure);
        var namespaceName = GetDtoNamespace(structure, callerNamespace);

        var rootClassInfo = _pipeline.DtoGenerator.GenerateClassInfo(
            structure,
            className,
            namespaceName);

        result.Add(rootClassInfo);

        // Add nested DTO classes
        result.AddRange(rootClassInfo.NestedClasses);

        return result;
    }

    /// <summary>
    /// Generates nested DTO classes only (for named types).
    /// </summary>
    private List<GenerateDtoClassInfo> GenerateNestedDtoClasses(
        DtoStructure structure,
        string callerNamespace)
    {
        var result = new List<GenerateDtoClassInfo>();

        // Only generate DTOs for nested structures
        foreach (var prop in structure.Properties)
        {
            if (prop.NestedStructure is null || prop.IsNestedFromNamedType)
                continue;

            var nestedClassName = GetDtoClassName(prop.NestedStructure);
            var nestedNamespace = GetDtoNamespace(prop.NestedStructure, callerNamespace);

            var nestedClassInfo = _pipeline.DtoGenerator.GenerateClassInfo(
                prop.NestedStructure,
                nestedClassName,
                nestedNamespace);

            result.Add(nestedClassInfo);
            result.AddRange(nestedClassInfo.NestedClasses);
        }

        return result;
    }

    /// <summary>
    /// Generates DTO classes with explicit name (for SelectExpr&lt;TIn, TResult&gt;).
    /// </summary>
    private List<GenerateDtoClassInfo> GenerateExplicitDtoClasses(
        DtoStructure structure,
        string explicitDtoName,
        string targetNamespace,
        List<string> parentClasses,
        ITypeSymbol? tResultType)
    {
        var result = new List<GenerateDtoClassInfo>();

        // Check if the explicit DTO already exists
        var existingProperties = new HashSet<string>();
        List<string> parentAccessibilities = [];

        if (tResultType is INamedTypeSymbol namedResultType)
        {
            // Get existing properties from the type
            existingProperties = new HashSet<string>(namedResultType
                .GetMembers()
                .OfType<IPropertySymbol>()
                .Select(p => p.Name));

            // Get parent class accessibilities
            parentAccessibilities = GetParentAccessibilities(namedResultType);
        }

        // Generate the root DTO class with explicit name
        var rootClassInfo = _pipeline.DtoGenerator.GenerateClassInfo(
            structure,
            explicitDtoName,
            targetNamespace,
            "public",
            parentClasses,
            parentAccessibilities,
            existingProperties,
            isExplicitRootDto: true);

        result.Add(rootClassInfo);

        // Add nested DTO classes
        result.AddRange(rootClassInfo.NestedClasses);

        return result;
    }

    /// <summary>
    /// Gets the DTO class name for a structure.
    /// </summary>
    private string GetDtoClassName(DtoStructure structure)
    {
        if (_configuration.NestedDtoUseHashNamespace)
        {
            return $"{structure.BestName}Dto";
        }
        return $"{structure.BestName}Dto_{structure.GetUniqueId()}";
    }

    /// <summary>
    /// Gets the DTO namespace for a structure.
    /// </summary>
    private string GetDtoNamespace(DtoStructure structure, string baseNamespace)
    {
        if (_configuration.NestedDtoUseHashNamespace)
        {
            var hash = structure.GetUniqueId();
            return string.IsNullOrEmpty(baseNamespace)
                ? $"{PipelineConstants.HashNamespacePrefix}{hash}"
                : $"{baseNamespace}.{PipelineConstants.HashNamespacePrefix}{hash}";
        }
        return baseNamespace;
    }

    /// <summary>
    /// Gets the root DTO class name with namespace.
    /// </summary>
    private string GetRootDtoClassName(DtoStructure structure)
    {
        var className = GetDtoClassName(structure);
        return $"global::{className}";
    }

    /// <summary>
    /// Builds a fully qualified DTO name from components.
    /// </summary>
    private string BuildFullyQualifiedDtoName(
        string dtoName,
        string namespaceName,
        List<string> parentClasses)
    {
        var parts = new List<string>();

        if (!string.IsNullOrEmpty(namespaceName))
        {
            parts.Add(namespaceName);
        }

        parts.AddRange(parentClasses);
        parts.Add(dtoName);

        return $"global::{string.Join(".", parts)}";
    }

    /// <summary>
    /// Gets parent class accessibilities from a type symbol.
    /// </summary>
    private static List<string> GetParentAccessibilities(INamedTypeSymbol typeSymbol)
    {
        var result = new List<string>();
        var current = typeSymbol.ContainingType;

        while (current is not null)
        {
            result.Insert(0, GetAccessibilityString(current.DeclaredAccessibility));
            current = current.ContainingType;
        }

        return result;
    }

    /// <summary>
    /// Gets the accessibility string from an accessibility value.
    /// </summary>
    private static string GetAccessibilityString(Accessibility accessibility)
    {
        return accessibility switch
        {
            Accessibility.Public => "public",
            Accessibility.Internal => "internal",
            Accessibility.Private => "private",
            Accessibility.Protected => "protected",
            Accessibility.ProtectedAndInternal => "private protected",
            Accessibility.ProtectedOrInternal => "protected internal",
            _ => "public"
        };
    }
}

/// <summary>
/// Result of processing a SelectExpr through the pipeline.
/// </summary>
internal class SelectExprProcessingResult
{
    /// <summary>
    /// The analyzed DTO structure.
    /// </summary>
    public DtoStructure? DtoStructure { get; init; }

    /// <summary>
    /// The generated DTO class infos.
    /// </summary>
    public List<GenerateDtoClassInfo> DtoClassInfos { get; init; } = [];

    /// <summary>
    /// The root DTO class name (fully qualified).
    /// </summary>
    public string RootDtoClassName { get; init; } = "";

    /// <summary>
    /// Whether this is an anonymous type SelectExpr.
    /// </summary>
    public bool IsAnonymous { get; init; }

    /// <summary>
    /// Whether this is an explicit DTO SelectExpr.
    /// </summary>
    public bool IsExplicitDto { get; init; }

    /// <summary>
    /// The source type.
    /// </summary>
    public ITypeSymbol? SourceType { get; init; }

    /// <summary>
    /// The lambda parameter name.
    /// </summary>
    public string LambdaParameterName { get; init; } = "";

    /// <summary>
    /// The caller namespace.
    /// </summary>
    public string CallerNamespace { get; init; } = "";

    /// <summary>
    /// Empty result singleton.
    /// </summary>
    public static SelectExprProcessingResult Empty { get; } = new();

    /// <summary>
    /// Whether the result is valid (has a structure with properties).
    /// </summary>
    public bool IsValid => DtoStructure is not null && DtoStructure.Properties.Count > 0;
}
