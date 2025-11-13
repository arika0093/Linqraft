using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EFCore.ExprGenerator;

/// <summary>
/// Base record for SelectExpr information
/// </summary>
internal abstract record SelectExprInfo
{
    public required ITypeSymbol SourceType { get; init; }
    public required SemanticModel SemanticModel { get; init; }
    public required InvocationExpressionSyntax Invocation { get; init; }

    // Generate DTO classes (including nested DTOs)
    public abstract List<GenerateDtoClassInfo> GenerateDtoClasses();

    // Generate DTO structure for unique ID generation
    protected abstract DtoStructure GenerateDtoStructure();

    // Get DTO class name
    protected abstract string GetClassName(DtoStructure structure);

    // Get parent DTO class name
    protected abstract string GetParentDtoClassName(DtoStructure structure);

    // Generate SelectExpr method
    protected abstract string GenerateSelectExprMethod(
        string dtoName,
        DtoStructure structure,
        InterceptableLocation location
    );

    // Generate SelectExpr codes for a given interceptable location
    public List<string> GenerateSelectExprCodes(InterceptableLocation location)
    {
        // Analyze anonymous type structure
        var dtoStructure = GenerateDtoStructure();

        // Skip if properties are empty
        if (dtoStructure.Properties.Count == 0)
            return [];

        // Get root DTO class name
        var mainDtoName = GetParentDtoClassName(dtoStructure);

        // Generate SelectExpr method with interceptor attribute
        var selectExprMethod = GenerateSelectExprMethod(mainDtoName, dtoStructure, location);

        return [selectExprMethod];
    }

    // Get namespace string
    public string GetNamespaceString()
    {
        var namespaceSymbol = SourceType.ContainingNamespace;
        return namespaceSymbol?.ToDisplayString() ?? "Generated";
    }

    // Generate unique ID (including location information)
    protected string GetUniqueId()
    {
        var structureId = GenerateDtoStructure().GetUniqueId();
        var locationId = GetLocationId();
        return $"{structureId}_{locationId}";
    }

    protected string GetLocationId()
    {
        var location =
            SemanticModel.GetInterceptableLocation(Invocation)
            ?? throw new InvalidOperationException("Failed to get interceptable location.");
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var hash = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(location.Data));
        return BitConverter.ToString(hash).Replace("-", "")[..8]; // Use first 8 characters
    }

    protected string GenerateMethodHeaderPart(string dtoName, InterceptableLocation location)
    {
        return $"""
            /// <summary>
            /// generated select expression method {dtoName} <br/>
            /// at {location.GetDisplayLocation()}
            /// </summary>
            {location.GetInterceptsLocationAttributeSyntax()}
            """;
    }

    protected string GeneratePropertyAssignment(DtoProperty property, int indents)
    {
        var expression = property.OriginalExpression;
        // For nested Select (collection) case
        if (property.NestedStructure is not null)
        {
            var converted = ConvertNestedSelect(expression, property.NestedStructure, indents);
            // Debug: Check if conversion was performed correctly
            if (converted == expression && expression.Contains("Select"))
            {
                // If conversion was not performed, leave the original expression as a comment
                return $"{converted} /* CONVERSION FAILED: {property.Name} */";
            }
            return converted;
        }
        // If nullable operator is used, convert to explicit null check
        if (property.IsNullable && expression.Contains("?."))
        {
            return ConvertNullableAccessToExplicitCheck(expression, property.TypeSymbol);
        }
        // Regular property access
        return expression;
    }

    protected string ConvertNestedSelect(
        string expression,
        DtoStructure nestedStructure,
        int indents
    )
    {
        var spaces = new string(' ', indents);
        // Example: s.Childs.Select(c => new { ... }) or s.Childs.Select(c => new { ... }).ToList()
        // Extract parameter name (e.g., "c")
        // Consider the possibility of whitespace or generic type parameters after .Select
        var selectIndex = expression.IndexOf(".Select");
        if (selectIndex == -1)
            return expression;
        // Find '(' after Select (start of lambda)
        var lambdaStart = expression.IndexOf("(", selectIndex);
        if (lambdaStart == -1)
            return expression;
        var lambdaArrow = expression.IndexOf("=>", lambdaStart);
        if (lambdaArrow == -1 || lambdaArrow <= lambdaStart + 1)
            return expression;
        var paramName = expression.Substring(lambdaStart + 1, lambdaArrow - lambdaStart - 1).Trim();
        if (string.IsNullOrEmpty(paramName))
            paramName = "x"; // Default parameter name
        var baseExpression = expression[..selectIndex];
        var nestedDtoName = GetClassName(nestedStructure);

        // Find the closing paren for Select(...) to detect any chained methods like .ToList()
        var parenDepth = 0;
        var selectEnd = lambdaStart;
        for (int i = lambdaStart; i < expression.Length; i++)
        {
            if (expression[i] == '(')
                parenDepth++;
            else if (expression[i] == ')')
            {
                parenDepth--;
                if (parenDepth == 0)
                {
                    selectEnd = i + 1;
                    break;
                }
            }
        }
        // Extract any chained method calls after Select(...) (e.g., ".ToList()")
        var chainedMethods = selectEnd < expression.Length ? expression[selectEnd..] : "";

        var propertyAssignments = new List<string>();
        foreach (var prop in nestedStructure.Properties)
        {
            var assignment = GeneratePropertyAssignment(prop, indents + 4);
            propertyAssignments.Add($"{spaces}    {prop.Name} = {assignment},");
        }
        var propertiesCode = string.Join("\n", propertyAssignments);
        var code = $$"""
            {{baseExpression}}.Select({{paramName}} => new {{nestedDtoName}} {
            {{propertiesCode}}
            {{spaces}}}){{chainedMethods}}
            """;
        return code;
    }

    protected string ConvertNullableAccessToExplicitCheck(string expression, ITypeSymbol typeSymbol)
    {
        // Example: c.Child?.Id → c.Child != null ? (int?)c.Child.Id : null
        // Example: s.Child3?.Child?.Id → s.Child3 != null && s.Child3.Child != null ? (int?)s.Child3.Child.Id : null
        if (!expression.Contains("?."))
            return expression;
        // Replace ?. with . to create the actual access path
        var accessPath = expression.Replace("?.", ".");
        // Find where ?. occurs and build null checks
        var checks = new List<string>();
        var parts = expression.Split(["?."], StringSplitOptions.None);
        if (parts.Length < 2)
            return expression;
        // All parts except the first require null checks
        var currentPath = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            checks.Add($"{currentPath} != null");
            // Get the first token (property name) of the next part
            var nextPart = parts[i];
            var dotIndex = nextPart.IndexOf('.');
            var propertyName = dotIndex > 0 ? nextPart[..dotIndex] : nextPart;
            currentPath = $"{currentPath}.{propertyName}";
        }
        if (checks.Count == 0)
            return expression;
        // Build null checks
        var nullCheckPart = string.Join(" && ", checks);
        var typeSymbolValue = typeSymbol.ToDisplayString();
        var nullableTypeName = typeSymbolValue != "?" ? $"({typeSymbolValue})" : "";
        var defaultValue = GetDefaultValueForType(typeSymbol);
        return $"{nullCheckPart} ? {nullableTypeName}{accessPath} : {defaultValue}";
    }

    protected string? GetImplicitPropertyName(ExpressionSyntax expression)
    {
        // Get property name from member access (e.g., s.Id)
        if (expression is MemberAccessExpressionSyntax memberAccess)
        {
            return memberAccess.Name.Identifier.Text;
        }
        // Get property name from identifier (e.g., id)
        if (expression is IdentifierNameSyntax identifier)
        {
            return identifier.Identifier.Text;
        }
        // Do not process other complex expressions
        return null;
    }

    protected string GetDefaultValueForType(ITypeSymbol typeSymbol)
    {
        if (
            typeSymbol.IsReferenceType
            || typeSymbol.NullableAnnotation == NullableAnnotation.Annotated
        )
        {
            return "null";
        }
        return typeSymbol.SpecialType switch
        {
            SpecialType.System_Boolean => "false",
            SpecialType.System_Char => "'\\0'",
            SpecialType.System_String => "string.Empty",
            _ => "default",
        };
    }

    protected string GetAccessibilityString(ITypeSymbol typeSymbol)
    {
        return typeSymbol.DeclaredAccessibility switch
        {
            Accessibility.Public => "public",
            Accessibility.Internal => "internal",
            Accessibility.Private => "private",
            Accessibility.Protected => "protected",
            Accessibility.ProtectedAndInternal => "private protected",
            Accessibility.ProtectedOrInternal => "protected internal",
            _ => "internal", // Default to internal for safety
        };
    }
}
