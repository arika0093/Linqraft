using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Linqraft.Core.Formatting;
using Linqraft.Core.RoslynHelpers;
using Microsoft.CodeAnalysis;

namespace Linqraft.Core.Pipeline.Generation;

/// <summary>
/// Generates DTO class source code from DtoStructure using the pipeline architecture.
/// This is the central generator for all DTO-related code output.
/// </summary>
internal class DtoGenerator
{
    private readonly SemanticModel _semanticModel;
    private readonly LinqraftConfiguration _configuration;

    /// <summary>
    /// Creates a new DTO generator.
    /// </summary>
    public DtoGenerator(SemanticModel semanticModel, LinqraftConfiguration configuration)
    {
        _semanticModel = semanticModel;
        _configuration = configuration;
    }

    /// <summary>
    /// Generates a GenerateDtoClassInfo from a DtoStructure.
    /// This is the primary method for converting analyzed structures to class definitions.
    /// </summary>
    public GenerateDtoClassInfo GenerateClassInfo(
        DtoStructure structure,
        string className,
        string namespaceName,
        string accessibility = "public",
        List<string>? parentClasses = null,
        List<string>? parentAccessibilities = null,
        HashSet<string>? existingProperties = null,
        bool isExplicitRootDto = false)
    {
        // Generate nested class infos for nested structures
        var nestedClassInfos = GenerateNestedClassInfos(structure, namespaceName);

        return new GenerateDtoClassInfo
        {
            Structure = structure,
            Accessibility = accessibility,
            ClassName = className,
            Namespace = namespaceName,
            NestedClasses = nestedClassInfos.ToImmutableList(),
            ParentClasses = parentClasses ?? [],
            ParentAccessibilities = parentAccessibilities ?? [],
            ExistingProperties = existingProperties ?? [],
            IsExplicitRootDto = isExplicitRootDto
        };
    }

    /// <summary>
    /// Generates nested class infos for all nested structures in the DTO.
    /// </summary>
    private List<GenerateDtoClassInfo> GenerateNestedClassInfos(
        DtoStructure structure,
        string parentNamespace)
    {
        var nestedClassInfos = new List<GenerateDtoClassInfo>();

        foreach (var prop in structure.Properties)
        {
            if (prop.NestedStructure is null || prop.IsNestedFromNamedType)
                continue;

            var nestedStructure = prop.NestedStructure;
            var nestedClassName = GetNestedClassName(nestedStructure);
            var nestedNamespace = GetNestedNamespace(nestedStructure, parentNamespace);

            var nestedClassInfo = GenerateClassInfo(
                nestedStructure,
                nestedClassName,
                nestedNamespace);

            nestedClassInfos.Add(nestedClassInfo);
        }

        return nestedClassInfos;
    }

    /// <summary>
    /// Gets the class name for a nested DTO structure.
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
    /// Gets the namespace for a nested DTO structure.
    /// </summary>
    private string GetNestedNamespace(DtoStructure structure, string parentNamespace)
    {
        if (_configuration.NestedDtoUseHashNamespace)
        {
            var hash = structure.GetUniqueId();
            return string.IsNullOrEmpty(parentNamespace)
                ? $"LinqraftGenerated_{hash}"
                : $"{parentNamespace}.LinqraftGenerated_{hash}";
        }
        return parentNamespace;
    }

    /// <summary>
    /// Generates the full DTO class name including namespace.
    /// </summary>
    public string GetFullDtoClassName(DtoStructure structure, string baseNamespace)
    {
        var className = GetNestedClassName(structure);
        var namespaceName = GetNestedNamespace(structure, baseNamespace);

        return string.IsNullOrEmpty(namespaceName)
            ? className
            : $"{namespaceName}.{className}";
    }

    /// <summary>
    /// Gets the property type string for a DtoProperty, handling nested structures.
    /// </summary>
    public string GetPropertyTypeString(DtoProperty prop, ImmutableList<GenerateDtoClassInfo> nestedClasses)
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
            return ApplyNestedStructureType(prop, propertyType, nestedClasses);
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
            typeWithoutArray = typeWithoutNullable[..^2];
        }

        string resultType;
        if (RoslynTypeHelper.IsAnonymousTypeByString(typeWithoutArray))
        {
            resultType = explicitDtoName;
        }
        else if (RoslynTypeHelper.IsGenericTypeByString(typeWithoutArray))
        {
            var baseType = typeWithoutArray[..typeWithoutArray.IndexOf("<")];
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
        ImmutableList<GenerateDtoClassInfo> nestedClasses)
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
                nestedDtoFullName = $"LinqraftGenerated_{hash}.{nestedClassName}";
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
            var baseType = typeWithoutNullable[..typeWithoutNullable.IndexOf("<")];
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
    /// Determines if a property type represents an array type.
    /// </summary>
    private static bool IsArrayType(DtoProperty prop, string typeString)
    {
        return prop.TypeSymbol is IArrayTypeSymbol
            || typeString.EndsWith("[]")
            || prop.OriginalExpression.Trim().EndsWith(".ToArray()");
    }
}
