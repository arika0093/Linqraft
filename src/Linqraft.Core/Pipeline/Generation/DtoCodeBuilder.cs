using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Linqraft.Core.Formatting;
using Linqraft.Core.RoslynHelpers;
using Microsoft.CodeAnalysis;

namespace Linqraft.Core.Pipeline.Generation;

/// <summary>
/// Builds DTO class source code using the pipeline architecture.
/// This centralizes the code generation logic for DTO classes.
/// </summary>
internal class DtoCodeBuilder
{
    private readonly LinqraftConfiguration _configuration;

    /// <summary>
    /// Creates a new DTO code builder.
    /// </summary>
    public DtoCodeBuilder(LinqraftConfiguration configuration)
    {
        _configuration = configuration;
    }

    /// <summary>
    /// Builds the C# source code for a DTO class.
    /// </summary>
    /// <param name="classInfo">The DTO class info</param>
    /// <returns>The generated C# code</returns>
    public string BuildDtoClassCode(GenerateDtoClassInfo classInfo)
    {
        // Delegate to the existing implementation for now
        // This provides a clean interface for the pipeline
        return classInfo.BuildCode(_configuration);
    }

    /// <summary>
    /// Builds DTO classes grouped by namespace.
    /// </summary>
    /// <param name="dtoClassInfos">The list of DTO class infos</param>
    /// <returns>The generated code with classes grouped by namespace</returns>
    public string BuildDtoCodeSnippetsGroupedByNamespace(List<GenerateDtoClassInfo> dtoClassInfos)
    {
        return GenerateSourceCodeSnippets.BuildDtoCodeSnippetsGroupedByNamespace(
            dtoClassInfos,
            _configuration);
    }

    /// <summary>
    /// Builds a global DTO code snippet for all DTOs.
    /// </summary>
    /// <param name="allDtoClassInfos">All DTO class infos</param>
    /// <returns>The complete source file content</returns>
    public string BuildGlobalDtoCodeSnippet(List<GenerateDtoClassInfo> allDtoClassInfos)
    {
        return GenerateSourceCodeSnippets.BuildGlobalDtoCodeSnippet(
            allDtoClassInfos,
            _configuration);
    }

    /// <summary>
    /// Builds expression code snippets with headers.
    /// </summary>
    public string BuildExprCodeSnippetsWithHeaders(
        List<string> expressions,
        List<string> staticFields,
        string? dtoCode = null)
    {
        return GenerateSourceCodeSnippets.BuildExprCodeSnippetsWithHeaders(
            expressions,
            staticFields,
            dtoCode);
    }

    /// <summary>
    /// Gets the property type string for a DtoProperty, handling nested structures.
    /// </summary>
    public string GetPropertyTypeString(
        DtoProperty prop,
        ImmutableList<GenerateDtoClassInfo> nestedClasses,
        string parentNamespace)
    {
        var propertyType = prop.TypeName ?? "object";

        // Handle explicit nested DTO types from SelectExpr<TIn, TResult>
        if (!string.IsNullOrEmpty(prop.ExplicitNestedDtoTypeName) && prop.NestedStructure is not null)
        {
            return ApplyExplicitNestedDtoType(prop, propertyType);
        }

        // Handle auto-generated nested structures
        if (prop.NestedStructure is not null && !prop.IsNestedFromNamedType)
        {
            return ApplyNestedStructureType(prop, propertyType, nestedClasses, parentNamespace);
        }

        return propertyType;
    }

    /// <summary>
    /// Applies explicit nested DTO type name to the property type.
    /// </summary>
    private string ApplyExplicitNestedDtoType(DtoProperty prop, string propertyType)
    {
        var explicitDtoName = prop.ExplicitNestedDtoTypeName!;
        var isTypeNullable = RoslynTypeHelper.IsNullableTypeByString(propertyType);
        var typeWithoutNullable = isTypeNullable
            ? RoslynTypeHelper.RemoveNullableSuffixFromString(propertyType)
            : propertyType;
        var shouldReapplyNullable = isTypeNullable && prop.IsNullable;
        var isArrayType = IsArrayType(prop, typeWithoutNullable);
        var typeWithoutArray = typeWithoutNullable;
        if (typeWithoutNullable.EndsWith("[]"))
        {
            typeWithoutArray = typeWithoutNullable.Substring(0, typeWithoutNullable.Length - 2);
        }

        string resultType;
        if (RoslynTypeHelper.IsAnonymousTypeByString(typeWithoutArray))
        {
            resultType = explicitDtoName;
        }
        else if (RoslynTypeHelper.IsGenericTypeByString(typeWithoutArray))
        {
            var baseType = typeWithoutArray.Substring(0, typeWithoutArray.IndexOf("<"));
            resultType = $"{baseType}<{explicitDtoName}>";
        }
        else
        {
            resultType = explicitDtoName;
        }

        if (isArrayType) resultType = $"{resultType}[]";
        if (shouldReapplyNullable) resultType = $"{resultType}?";

        return resultType;
    }

    /// <summary>
    /// Applies nested structure DTO type to the property type.
    /// </summary>
    private string ApplyNestedStructureType(
        DtoProperty prop,
        string propertyType,
        ImmutableList<GenerateDtoClassInfo> nestedClasses,
        string parentNamespace)
    {
        var nestStructure = prop.NestedStructure!;
        var nestedClassName = GetNestedClassName(nestStructure);
        var containedNestClasses = nestedClasses.FirstOrDefault(nc => nc.ClassName == nestedClassName);

        string nestedDtoFullName;
        if (containedNestClasses != null)
        {
            nestedDtoFullName = containedNestClasses.FullName;
        }
        else
        {
            // Fallback: construct the full name
            if (_configuration.NestedDtoUseHashNamespace)
            {
                var hash = nestStructure.GetUniqueId();
                nestedDtoFullName = $"{PipelineConstants.HashNamespacePrefix}{hash}.{nestedClassName}";
            }
            else
            {
                nestedDtoFullName = nestedClassName;
            }
        }

        var isTypeNullable = RoslynTypeHelper.IsNullableTypeByString(propertyType);
        var typeWithoutNullable = isTypeNullable
            ? RoslynTypeHelper.RemoveNullableSuffixFromString(propertyType)
            : propertyType;
        var shouldReapplyNullable = isTypeNullable && prop.IsNullable;

        string resultType;
        if (typeWithoutNullable.StartsWith("global::<anonymous"))
        {
            resultType = $"global::{nestedDtoFullName}";
        }
        else if (RoslynTypeHelper.IsGenericTypeByString(typeWithoutNullable))
        {
            var baseType = typeWithoutNullable.Substring(0, typeWithoutNullable.IndexOf("<"));
            resultType = $"{baseType}<{nestedDtoFullName}>";
        }
        else
        {
            resultType = $"global::{nestedDtoFullName}";
        }

        if (shouldReapplyNullable) resultType = $"{resultType}?";

        return resultType;
    }

    /// <summary>
    /// Gets the nested class name for a structure.
    /// </summary>
    private string GetNestedClassName(DtoStructure structure)
    {
        if (_configuration.NestedDtoUseHashNamespace)
        {
            return $"{structure.BestName}Dto";
        }
        return $"{structure.BestName}Dto_{structure.GetUniqueId()}";
    }

    /// <summary>
    /// Determines if a property type represents an array type.
    /// </summary>
    private static bool IsArrayType(DtoProperty prop, string typeString)
    {
        return prop.TypeSymbol is IArrayTypeSymbol
            || typeString.EndsWith("[]")
            || prop.OriginalExpression.Trim().EndsWith(".ToArray()");
    }

    /// <summary>
    /// Gets the property accessor string for the given accessor type.
    /// </summary>
    public static string GetPropertyAccessorString(PropertyAccessor accessor)
    {
        return accessor switch
        {
            PropertyAccessor.GetAndSet => "get; set;",
            PropertyAccessor.GetAndInit => "get; init;",
            PropertyAccessor.GetAndInternalSet => "get; internal set;",
            _ => "get; set;",
        };
    }

    /// <summary>
    /// Gets the DTO code attributes for generated DTO classes.
    /// </summary>
    public static string GetDtoCodeAttributes()
    {
        return GenerateSourceCodeSnippets.BuildDtoCodeAttributes();
    }
}
