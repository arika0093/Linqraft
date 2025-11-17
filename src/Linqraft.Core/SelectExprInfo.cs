using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Linqraft.Core;

/// <summary>
/// Base class for SelectExpr information, providing common functionality for
/// analyzing LINQ Select expressions and generating corresponding DTO structures
/// </summary>
public abstract record SelectExprInfo
{
    /// <summary>
    /// The source type being selected from (e.g., T in IQueryable&lt;T&gt;)
    /// </summary>
    public required ITypeSymbol SourceType { get; init; }

    /// <summary>
    /// The semantic model for type resolution and analysis
    /// </summary>
    public required SemanticModel SemanticModel { get; init; }

    /// <summary>
    /// The invocation expression syntax for the SelectExpr call
    /// </summary>
    public required InvocationExpressionSyntax Invocation { get; init; }

    /// <summary>
    /// The name of the lambda parameter (e.g., "s" in s => new { ... })
    /// </summary>
    public required string LambdaParameterName { get; init; }

    /// <summary>
    /// The namespace where the SelectExpr is invoked
    /// </summary>
    public required string CallerNamespace { get; init; }

    /// <summary>
    /// The Linqraft configuration settings
    /// </summary>
    public LinqraftConfiguration Configuration { get; set; } = new();

    /// <summary>
    /// Generates DTO class information (including nested DTOs)
    /// </summary>
    public abstract List<GenerateDtoClassInfo> GenerateDtoClasses();

    /// <summary>
    /// Generates the DTO structure for analysis and unique ID generation
    /// </summary>
    protected abstract DtoStructure GenerateDtoStructure();

    /// <summary>
    /// Gets the class name for a DTO structure
    /// </summary>
    protected abstract string GetClassName(DtoStructure structure);

    /// <summary>
    /// Gets the parent (root) DTO class name
    /// </summary>
    protected abstract string GetParentDtoClassName(DtoStructure structure);

    /// <summary>
    /// Gets the namespace where DTOs will be placed
    /// </summary>
    protected abstract string GetDtoNamespace();

    // Get expression type string (for documentation)
    protected abstract string GetExprTypeString();

    // Get the full name for a nested DTO class (can be overridden for nested class support)
    protected virtual string GetNestedDtoFullName(string nestedClassName)
    {
        return $"global::{GetDtoNamespace()}.{nestedClassName}";
    }

    /// <summary>
    /// Generates the SelectExpr method code
    /// </summary>
    protected abstract string GenerateSelectExprMethod(
        string dtoName,
        DtoStructure structure,
        InterceptableLocation location
    );

    /// <summary>
    /// Generates SelectExpr code for a given interceptable location
    /// </summary>
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

    /// <summary>
    /// Gets the namespace string (returns the namespace where SelectExpr is invoked)
    /// </summary>
    public string GetNamespaceString()
    {
        return CallerNamespace;
    }

    /// <summary>
    /// Checks if the invocation source is IEnumerable (not IQueryable)
    /// </summary>
    protected bool IsEnumerableInvocation()
    {
        // Get the type of the expression on which SelectExpr is called
        if (Invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return false;

        var typeInfo = SemanticModel.GetTypeInfo(memberAccess.Expression);
        if (typeInfo.Type is not INamedTypeSymbol namedType)
            return false;

        // Check if the type is IEnumerable<T> (not IQueryable<T>)
        var typeDisplayString = namedType.ToDisplayString();

        // If it's IQueryable, return false
        if (typeDisplayString.Contains("IQueryable"))
            return false;

        // If it's IEnumerable (and not a more specific interface like IQueryable), return true
        if (typeDisplayString.Contains("IEnumerable"))
            return true;

        // Check base interfaces
        foreach (var baseInterface in namedType.AllInterfaces)
        {
            if (baseInterface.Name == "IQueryable")
                return false;
            if (baseInterface.Name == "IEnumerable")
                return true;
        }

        return false;
    }

    /// <summary>
    /// Gets the return type prefix based on whether it's IQueryable or IEnumerable
    /// </summary>
    protected string GetReturnTypePrefix()
    {
        return IsEnumerableInvocation() ? "IEnumerable" : "IQueryable";
    }

    /// <summary>
    /// Generates a unique ID (including location information)
    /// </summary>
    protected string GetUniqueId()
    {
        var structureId = GenerateDtoStructure().GetUniqueId();
        var locationId = GetLocationId();
        return $"{structureId}_{locationId}";
    }

    /// <summary>
    /// Gets a unique location identifier
    /// </summary>
    protected string GetLocationId()
    {
        var location =
            SemanticModel.GetInterceptableLocation(Invocation)
            ?? throw new InvalidOperationException("Failed to get interceptable location.");
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var hash = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(location.Data));
        return BitConverter.ToString(hash).Replace("-", "")[..8]; // Use first 8 characters
    }

    /// <summary>
    /// Generates the method header part with documentation and interceptor attribute
    /// </summary>
    protected string GenerateMethodHeaderPart(string dtoName, InterceptableLocation location)
    {
        var typeString = GetExprTypeString();
        var displayLocationRaw = location.GetDisplayLocation();
        var locationFileOnly = displayLocationRaw.Split(['/', '\\']).Last();
        return $"""
            /// <summary>
            /// generated select expression method {dtoName} ({typeString}) <br/>
            /// at {locationFileOnly}
            /// </summary>
            {location.GetInterceptsLocationAttributeSyntax()}
            """;
    }

    /// <summary>
    /// Generates property assignment code for a DTO property
    /// </summary>
    protected string GeneratePropertyAssignment(DtoProperty property, int indents)
    {
        var expression = property.OriginalExpression;
        var syntax = property.OriginalSyntax;

        // For nested Select (collection) case
        if (property.NestedStructure is not null)
        {
            var converted = ConvertNestedSelectWithRoslyn(
                syntax,
                property.NestedStructure,
                indents
            );
            // Debug: Check if conversion was performed correctly
            if (converted == expression && expression.Contains("Select"))
            {
                // If conversion was not performed, leave the original expression as a comment
                return $"{converted} /* CONVERSION FAILED: {property.Name} */";
            }
            return converted;
        }
        // If nullable operator is used, convert to explicit null check
        if (
            property.IsNullable
            && syntax.DescendantNodesAndSelf().OfType<ConditionalAccessExpressionSyntax>().Any()
        )
        {
            return ConvertNullableAccessToExplicitCheckWithRoslyn(syntax, property.TypeSymbol);
        }
        // Regular property access
        return expression;
    }

    /// <summary>
    /// Converts nested Select expressions using Roslyn syntax analysis
    /// </summary>
    protected string ConvertNestedSelectWithRoslyn(
        ExpressionSyntax syntax,
        DtoStructure nestedStructure,
        int indents
    )
    {
        var spaces = new string(' ', indents);
        var nestedClassName = GetClassName(nestedStructure);
        // For anonymous types (empty class name), don't use namespace qualification
        var nestedDtoName = string.IsNullOrEmpty(nestedClassName)
            ? ""
            : GetNestedDtoFullName(nestedClassName);

        // Use Roslyn to extract Select information
        var selectInfo = ExtractSelectInfoFromSyntax(syntax);
        if (selectInfo is null)
        {
            // Fallback to string representation
            return syntax.ToString();
        }

        var (baseExpression, paramName, chainedMethods, hasNullableAccess, coalescingDefaultValue) =
            selectInfo.Value;

        // Normalize baseExpression: remove unnecessary whitespace and newlines
        baseExpression = System.Text.RegularExpressions.Regex.Replace(
            baseExpression.Trim(),
            @"\s+",
            " "
        );
        // Remove spaces around dots (property access)
        baseExpression = System.Text.RegularExpressions.Regex.Replace(
            baseExpression,
            @"\s*\.\s*",
            "."
        );

        // Generate property assignments for nested DTO
        var propertyAssignments = new List<string>();
        foreach (var prop in nestedStructure.Properties)
        {
            var assignment = GeneratePropertyAssignment(prop, indents + 4);
            propertyAssignments.Add($"{spaces}    {prop.Name} = {assignment},");
        }
        var propertiesCode = string.Join("\n", propertyAssignments);

        // Build the Select expression
        if (hasNullableAccess)
        {
            // Determine default value
            var defaultValue =
                coalescingDefaultValue
                ?? (
                    string.IsNullOrEmpty(nestedDtoName)
                        ? "null"
                        : $"System.Linq.Enumerable.Empty<{nestedDtoName}>()"
                );

            var code = $$"""
                {{baseExpression}} != null ? {{baseExpression}}.Select({{paramName}} => new {{nestedDtoName}} {
                {{propertiesCode}}
                {{spaces}}}){{chainedMethods}} : {{defaultValue}}
                """;
            return code;
        }
        else
        {
            var code = $$"""
                {{baseExpression}}.Select({{paramName}} => new {{nestedDtoName}} {
                {{propertiesCode}}
                {{spaces}}}){{chainedMethods}}
                """;
            return code;
        }
    }

    private (
        string baseExpression,
        string paramName,
        string chainedMethods,
        bool hasNullableAccess,
        string? coalescingDefaultValue
    )? ExtractSelectInfoFromSyntax(ExpressionSyntax syntax)
    {
        string? coalescingDefaultValue = null;
        var currentSyntax = syntax;

        // Check for coalescing operator (??)
        if (
            syntax is BinaryExpressionSyntax
            {
                RawKind: (int)SyntaxKind.CoalesceExpression
            } binaryExpr
        )
        {
            var rightSide = binaryExpr.Right.ToString().Trim();
            coalescingDefaultValue = rightSide == "[]" ? null : rightSide;
            currentSyntax = binaryExpr.Left;
        }

        // Check for conditional access (?.Select)
        bool hasNullableAccess = false;
        ConditionalAccessExpressionSyntax? conditionalAccess = null;
        if (currentSyntax is ConditionalAccessExpressionSyntax condAccess)
        {
            hasNullableAccess = true;
            conditionalAccess = condAccess;
            currentSyntax = condAccess.WhenNotNull;
        }

        // Find the Select invocation
        InvocationExpressionSyntax? selectInvocation = null;
        string chainedMethods = "";

        if (currentSyntax is InvocationExpressionSyntax invocation)
        {
            // Check if this is .Select() or .Select().ToList()
            if (
                invocation.Expression is MemberAccessExpressionSyntax memberAccess
                && memberAccess.Name.Identifier.Text == "Select"
            )
            {
                selectInvocation = invocation;
            }
            else if (
                invocation.Expression is MemberBindingExpressionSyntax memberBinding
                && memberBinding.Name.Identifier.Text == "Select"
            )
            {
                selectInvocation = invocation;
            }
            else if (invocation.Expression is MemberAccessExpressionSyntax chainedMember)
            {
                // This is a chained method like .ToList()
                chainedMethods = $".{chainedMember.Name}{invocation.ArgumentList}";
                if (chainedMember.Expression is InvocationExpressionSyntax innerInvocation)
                {
                    if (
                        innerInvocation.Expression is MemberAccessExpressionSyntax innerMember
                        && innerMember.Name.Identifier.Text == "Select"
                    )
                    {
                        selectInvocation = innerInvocation;
                    }
                    else if (
                        innerInvocation.Expression is MemberBindingExpressionSyntax innerBinding
                        && innerBinding.Name.Identifier.Text == "Select"
                    )
                    {
                        selectInvocation = innerInvocation;
                    }
                }
            }
        }

        if (selectInvocation is null)
            return null;

        // Extract lambda parameter name using Roslyn
        string paramName = "x"; // Default
        if (selectInvocation.ArgumentList.Arguments.Count > 0)
        {
            var arg = selectInvocation.ArgumentList.Arguments[0].Expression;
            if (arg is SimpleLambdaExpressionSyntax simpleLambda)
            {
                paramName = simpleLambda.Parameter.Identifier.Text;
            }
            else if (
                arg is ParenthesizedLambdaExpressionSyntax parenLambda
                && parenLambda.ParameterList.Parameters.Count > 0
            )
            {
                paramName = parenLambda.ParameterList.Parameters[0].Identifier.Text;
            }
        }

        // Extract base expression (the collection being selected from)
        string baseExpression;
        if (hasNullableAccess && conditionalAccess is not null)
        {
            baseExpression = conditionalAccess.Expression.ToString();
        }
        else if (selectInvocation.Expression is MemberAccessExpressionSyntax selectMember)
        {
            baseExpression = selectMember.Expression.ToString();
        }
        else if (
            selectInvocation.Expression is MemberBindingExpressionSyntax
            && conditionalAccess is not null
        )
        {
            baseExpression = conditionalAccess.Expression.ToString();
        }
        else
        {
            return null;
        }

        return (
            baseExpression,
            paramName,
            chainedMethods,
            hasNullableAccess,
            coalescingDefaultValue
        );
    }

    /// <summary>
    /// Converts nullable access expressions to explicit null checks using Roslyn
    /// </summary>
    protected string ConvertNullableAccessToExplicitCheckWithRoslyn(
        ExpressionSyntax syntax,
        ITypeSymbol typeSymbol
    )
    {
        // Example: c.Child?.Id → c.Child != null ? (int?)c.Child.Id : null
        // Example: s.Child3?.Child?.Id → s.Child3 != null && s.Child3.Child != null ? (int?)s.Child3.Child.Id : null

        // Use Roslyn to verify this uses conditional access
        var hasConditionalAccess = syntax
            .DescendantNodesAndSelf()
            .OfType<ConditionalAccessExpressionSyntax>()
            .Any();

        if (!hasConditionalAccess)
            return syntax.ToString();

        // For now, use the original string-based implementation since it works
        // The Roslyn check above ensures we only call this when appropriate
        var expression = syntax.ToString();

        // Build the access path without ?. operators
        var accessPath = expression.Replace("?.", ".");

        // Build null checks using string manipulation (proven to work)
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

    /// <summary>
    /// Gets the implicit property name from an expression
    /// </summary>
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

    /// <summary>
    /// Gets the default value for a type symbol
    /// </summary>
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

    /// <summary>
    /// Gets the accessibility string from a type symbol
    /// </summary>
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
            _ => "public", // Default to public
        };
    }
}
