using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Linqraft.Core.Formatting;
using Linqraft.Core.RoslynHelpers;
using Linqraft.Core.SyntaxHelpers;
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
    /// The name of the capture parameter (e.g., "capture" in (x, capture) => new { ... })
    /// Null if no capture parameter is used
    /// </summary>
    public string? CaptureParameterName { get; init; }

    /// <summary>
    /// The expression syntax for the capture argument
    /// Null if no capture parameter is used
    /// </summary>
    public ExpressionSyntax? CaptureArgumentExpression { get; init; }

    /// <summary>
    /// The type symbol of the capture argument
    /// Null if no capture parameter is used
    /// </summary>
    public ITypeSymbol? CaptureArgumentType { get; init; }

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
        var dtoNamespace = GetDtoNamespace();
        if (string.IsNullOrEmpty(dtoNamespace))
        {
            // Global namespace: no namespace prefix, just use global:: with class name
            return $"global::{nestedClassName}";
        }
        return $"global::{dtoNamespace}.{nestedClassName}";
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
    /// Gets the file name string where SelectExpr is invoked
    /// </summary>
    public string? GetFileNameString()
    {
        var filepath = Invocation.GetLocation()?.SourceTree?.FilePath;
        var fileName = System.IO.Path.GetFileNameWithoutExtension(filepath);
        return fileName;
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

        var compilation = SemanticModel.Compilation;

        // If it's IQueryable, return false
        if (RoslynTypeHelper.ImplementsIQueryable(namedType, compilation))
            return false;

        // If it's IEnumerable (and not a more specific interface like IQueryable), return true
        if (RoslynTypeHelper.ImplementsIEnumerable(namedType, compilation))
            return true;

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
        return HashUtility.GenerateSha256Hash(location.Data);
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

        // For nested structure cases
        if (property.NestedStructure is not null)
        {
            // Check if this contains SelectMany first (it's more specific)
            if (RoslynTypeHelper.ContainsSelectManyInvocation(syntax))
            {
                // For nested SelectMany (collection flattening) case
                var convertedSelectMany = ConvertNestedSelectManyWithRoslyn(
                    syntax,
                    property,
                    indents
                );
                // Debug: Check if conversion was performed correctly
                if (
                    convertedSelectMany == expression
                    && RoslynTypeHelper.ContainsSelectManyInvocation(syntax)
                )
                {
                    // If conversion was not performed, leave the original expression as a comment
                    return $"{convertedSelectMany} /* CONVERSION FAILED: {property.Name} */";
                }
                return convertedSelectMany;
            }

            // Check if this contains Select - use dedicated formatting for better output
            if (RoslynTypeHelper.ContainsSelectInvocation(syntax))
            {
                // For nested Select (collection) case - handles both anonymous and named types
                var convertedSelect = ConvertNestedSelectWithRoslyn(syntax, property, indents);
                // If conversion was successful, return the result
                if (convertedSelect != expression)
                {
                    return convertedSelect;
                }
                // If conversion failed (returned original expression), fall through to
                // anonymous type handling below. This handles cases like ternary operators
                // containing Select calls where ConvertNestedSelectWithRoslyn can't process
                // the outer structure.
            }

            // For other cases with anonymous types (e.g., ternary operators, direct anonymous types)
            // Replace any anonymous type creation in the expression with the DTO
            var anonymousCreation = syntax
                .DescendantNodesAndSelf()
                .OfType<AnonymousObjectCreationExpressionSyntax>()
                .FirstOrDefault();

            if (anonymousCreation != null)
            {
                // Convert the expression by replacing the anonymous type with the DTO
                return ConvertExpressionWithAnonymousTypeToDto(
                    syntax,
                    anonymousCreation,
                    property.NestedStructure,
                    indents
                );
            }

            // Fallback: return the original expression
            return expression;
        }
        // If nullable operator is used, convert to explicit null check
        if (
            property.IsNullable
            && syntax.DescendantNodesAndSelf().OfType<ConditionalAccessExpressionSyntax>().Any()
        )
        {
            return ConvertNullableAccessToExplicitCheckWithRoslyn(syntax, property.TypeSymbol);
        }
        // if static/constant expression, return expression with full-name resolution
        if (
            syntax is MemberAccessExpressionSyntax memberAccess
            && memberAccess.Kind() == SyntaxKind.SimpleMemberAccessExpression
        )
        {
            var symbolInfo = SemanticModel.GetSymbolInfo(memberAccess);

            // Check if it's a static field or const field
            if (
                symbolInfo.Symbol is IFieldSymbol fieldSymbol
                && (fieldSymbol.IsStatic || fieldSymbol.IsConst)
            )
            {
                var containingType = fieldSymbol.ContainingType;
                var fullTypeName = containingType.ToDisplayString(
                    SymbolDisplayFormat.FullyQualifiedFormat
                );
                var memberName = fieldSymbol.Name;
                return $"{fullTypeName}.{memberName}";
            }
            // Check if it's a static property
            else if (symbolInfo.Symbol is IPropertySymbol propertySymbol && propertySymbol.IsStatic)
            {
                var containingType = propertySymbol.ContainingType;
                var fullTypeName = containingType.ToDisplayString(
                    SymbolDisplayFormat.FullyQualifiedFormat
                );
                var memberName = propertySymbol.Name;
                return $"{fullTypeName}.{memberName}";
            }
        }

        // if simple identifier, check if it's a static/const member that needs full qualification
        if (syntax is IdentifierNameSyntax identifierName)
        {
            var symbolInfo = SemanticModel.GetSymbolInfo(identifierName);

            // Check if it's a static field or const field
            if (symbolInfo.Symbol is IFieldSymbol fieldSymbol)
            {
                // Check if it's an enum value
                if (fieldSymbol.ContainingType?.TypeKind == TypeKind.Enum)
                {
                    var containingType = fieldSymbol.ContainingType;
                    var fullTypeName = containingType.ToDisplayString(
                        SymbolDisplayFormat.FullyQualifiedFormat
                    );
                    var memberName = fieldSymbol.Name;
                    return $"{fullTypeName}.{memberName}";
                }

                // Check if it's a public/internal const field
                if (fieldSymbol.IsConst && fieldSymbol.ContainingType is not null)
                {
                    if (
                        fieldSymbol.DeclaredAccessibility == Accessibility.Public
                        || fieldSymbol.DeclaredAccessibility == Accessibility.Internal
                    )
                    {
                        var containingType = fieldSymbol.ContainingType;
                        var fullTypeName = containingType.ToDisplayString(
                            SymbolDisplayFormat.FullyQualifiedFormat
                        );
                        var memberName = fieldSymbol.Name;
                        return $"{fullTypeName}.{memberName}";
                    }
                }

                // Check if it's a public/internal static field (non-const)
                if (fieldSymbol.IsStatic && fieldSymbol.ContainingType is not null)
                {
                    if (
                        fieldSymbol.DeclaredAccessibility == Accessibility.Public
                        || fieldSymbol.DeclaredAccessibility == Accessibility.Internal
                    )
                    {
                        var containingType = fieldSymbol.ContainingType;
                        var fullTypeName = containingType.ToDisplayString(
                            SymbolDisplayFormat.FullyQualifiedFormat
                        );
                        var memberName = fieldSymbol.Name;
                        return $"{fullTypeName}.{memberName}";
                    }
                }
            }
            // Check if it's a public/internal static property
            else if (symbolInfo.Symbol is IPropertySymbol propertySymbol)
            {
                if (propertySymbol.IsStatic)
                {
                    if (
                        propertySymbol.DeclaredAccessibility == Accessibility.Public
                        || propertySymbol.DeclaredAccessibility == Accessibility.Internal
                    )
                    {
                        var containingType = propertySymbol.ContainingType;
                        var fullTypeName = containingType.ToDisplayString(
                            SymbolDisplayFormat.FullyQualifiedFormat
                        );
                        var memberName = propertySymbol.Name;
                        return $"{fullTypeName}.{memberName}";
                    }
                }
            }
        }

        // if object creation expression, convert type names to fully qualified names
        if (syntax is ObjectCreationExpressionSyntax objectCreation)
        {
            return ConvertObjectCreationToFullyQualified(objectCreation);
        }

        // Regular property access
        return expression;
    }

    /// <summary>
    /// Converts any expression containing an anonymous type to use the generated DTO instead.
    /// This is a general approach that works for direct anonymous types, ternary operators,
    /// method calls, and any other expression structure.
    /// </summary>
    protected string ConvertExpressionWithAnonymousTypeToDto(
        ExpressionSyntax syntax,
        AnonymousObjectCreationExpressionSyntax anonymousCreation,
        DtoStructure nestedStructure,
        int indents
    )
    {
        var spaces = CodeFormatter.IndentSpaces(indents);
        var innerSpaces = CodeFormatter.IndentSpaces(indents + CodeFormatter.IndentSize);
        var nestedClassName = GetClassName(nestedStructure);
        var nestedDtoName = string.IsNullOrEmpty(nestedClassName)
            ? ""
            : GetNestedDtoFullName(nestedClassName);

        // Generate the DTO object creation to replace the anonymous type with proper formatting
        var propertyAssignments = new List<string>();
        foreach (var prop in nestedStructure.Properties)
        {
            var assignment = GeneratePropertyAssignment(prop, indents + CodeFormatter.IndentSize);
            propertyAssignments.Add($"{innerSpaces}{prop.Name} = {assignment}");
        }
        var propertiesCode = string.Join($",{CodeFormatter.DefaultNewLine}", propertyAssignments);

        // Build the DTO creation with proper formatting
        var dtoCreation = $$"""
            new {{nestedDtoName}}
            {{spaces}}{
            {{propertiesCode}}
            {{spaces}}}
            """;

        // Remove comments from syntax before processing
        var cleanSyntax = RemoveComments(syntax);
        var cleanAnonymousCreation = RemoveComments(anonymousCreation);

        // Replace the anonymous type creation with the DTO creation in the original expression
        var originalText = cleanSyntax.ToString();
        var anonymousText = cleanAnonymousCreation.ToString();

        // Simple string replacement approach
        var convertedText = originalText.Replace(anonymousText, dtoCreation);

        return convertedText;
    }

    /// <summary>
    /// Converts a direct anonymous type expression to a nested DTO object creation
    /// </summary>
    protected string ConvertDirectAnonymousTypeToDto(
        ExpressionSyntax syntax,
        DtoStructure nestedStructure,
        int indents
    )
    {
        var spaces = CodeFormatter.IndentSpaces(indents);
        var innerSpaces = CodeFormatter.IndentSpaces(indents + CodeFormatter.IndentSize);
        var nestedClassName = GetClassName(nestedStructure);
        // For anonymous types (empty class name), don't use namespace qualification
        var nestedDtoName = string.IsNullOrEmpty(nestedClassName)
            ? ""
            : GetNestedDtoFullName(nestedClassName);

        // Generate property assignments for nested DTO with proper formatting
        var propertyAssignments = new List<string>();
        foreach (var prop in nestedStructure.Properties)
        {
            var assignment = GeneratePropertyAssignment(
                prop,
                indents + CodeFormatter.IndentSize * 2
            );
            propertyAssignments.Add($"{innerSpaces}{prop.Name} = {assignment}");
        }
        var propertiesCode = string.Join($",{CodeFormatter.DefaultNewLine}", propertyAssignments);

        // Build the new DTO object creation expression with proper formatting
        var code = $$"""
            new {{nestedDtoName}}
            {{spaces}}{
            {{propertiesCode}}
            {{spaces}}}
            """;
        return code;
    }

    /// <summary>
    /// Converts nested Select expressions using Roslyn syntax analysis
    /// </summary>
    protected string ConvertNestedSelectWithRoslyn(
        ExpressionSyntax syntax,
        DtoProperty property,
        int indents
    )
    {
        var nestedStructure = property.NestedStructure!;
        var spaces = CodeFormatter.IndentSpaces(indents);
        var innerSpaces = CodeFormatter.IndentSpaces(indents + CodeFormatter.IndentSize);
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

        var (
            baseExpression,
            paramName,
            chainedMethods,
            hasNullableAccess,
            coalescingDefaultValue,
            nullCheckExpression
        ) = selectInfo.Value;

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

        // Normalize nullCheckExpression if present
        if (nullCheckExpression is not null)
        {
            nullCheckExpression = System.Text.RegularExpressions.Regex.Replace(
                nullCheckExpression.Trim(),
                @"\s+",
                " "
            );
            nullCheckExpression = System.Text.RegularExpressions.Regex.Replace(
                nullCheckExpression,
                @"\s*\.\s*",
                "."
            );
        }

        // Generate property assignments for nested DTO with proper formatting
        // Properties should be indented two levels from the base (one for Select block, one for properties)
        var propertyIndentSpaces = CodeFormatter.IndentSpaces(
            indents + CodeFormatter.IndentSize * 2
        );
        var propertyAssignments = new List<string>();
        foreach (var prop in nestedStructure.Properties)
        {
            var assignment = GeneratePropertyAssignment(
                prop,
                indents + CodeFormatter.IndentSize * 2
            );
            propertyAssignments.Add($"{propertyIndentSpaces}{prop.Name} = {assignment}");
        }
        var propertiesCode = string.Join($",{CodeFormatter.DefaultNewLine}", propertyAssignments);

        // Format chained methods with proper indentation (one level from base)
        var formattedChainedMethods = FormatChainedMethods(chainedMethods, innerSpaces);

        // Build the Select expression with proper formatting
        // The .Select, {, }, and chained methods should all be indented one level from the property assignment
        if (hasNullableAccess)
        {
            // Use nullCheckExpression for the null check (defaults to baseExpression if not provided)
            var checkExpr = nullCheckExpression ?? baseExpression;

            // Determine default value based on the expression's type
            // If it's a collection type, use empty enumerable/list; otherwise use null
            string defaultValue;
            if (coalescingDefaultValue is not null)
            {
                defaultValue = coalescingDefaultValue;
            }
            else if (
                string.IsNullOrEmpty(nestedDtoName)
                || !RoslynTypeHelper.IsCollectionType(property.TypeSymbol)
            )
            {
                // For single element results or anonymous types, default is null
                defaultValue = "null";
            }
            else
            {
                // For collections, determine the appropriate empty collection type
                // Check if the result type is a List<T> (chained with ToList())
                defaultValue = GetEmptyCollectionExpression(
                    property.TypeSymbol,
                    nestedDtoName,
                    chainedMethods
                );
            }

            var code = $$"""
                {{checkExpr}} != null ? {{baseExpression}}
                {{innerSpaces}}.Select({{paramName}} => new {{nestedDtoName}}
                {{innerSpaces}}{
                {{propertiesCode}}
                {{innerSpaces}}}){{formattedChainedMethods}} : {{defaultValue}}
                """;
            return code;
        }
        else
        {
            var code = $$"""
                {{baseExpression}}
                {{innerSpaces}}.Select({{paramName}} => new {{nestedDtoName}}
                {{innerSpaces}}{
                {{propertiesCode}}
                {{innerSpaces}}}){{formattedChainedMethods}}
                """;
            return code;
        }
    }

    /// <summary>
    /// Gets the appropriate empty collection expression based on the target type.
    /// For List types, returns "new List&lt;T&gt;()" to match the expected type.
    /// For IEnumerable types, returns "System.Linq.Enumerable.Empty&lt;T&gt;()".
    /// </summary>
    private static string GetEmptyCollectionExpression(
        ITypeSymbol? typeSymbol,
        string elementTypeName,
        string chainedMethods
    )
    {
        // Check if the target type is a List<T> (either explicitly or via ToList())
        var isListType = IsListType(typeSymbol) || chainedMethods.Contains(".ToList()");
        if (isListType)
        {
            return $"new System.Collections.Generic.List<{elementTypeName}>()";
        }

        // Check if the target type is an array (via ToArray())
        if (chainedMethods.Contains(".ToArray()"))
        {
            return $"System.Array.Empty<{elementTypeName}>()";
        }

        // Default to Enumerable.Empty for IEnumerable<T> types
        return $"System.Linq.Enumerable.Empty<{elementTypeName}>()";
    }

    /// <summary>
    /// Checks if a type symbol represents a List type
    /// </summary>
    private static bool IsListType(ITypeSymbol? typeSymbol)
    {
        if (typeSymbol is not INamedTypeSymbol namedType)
            return false;

        // Get the underlying type if it's nullable
        var nonNullableType = RoslynTypeHelper.GetNonNullableType(namedType) ?? namedType;
        if (nonNullableType is not INamedTypeSymbol nonNullableNamedType)
            return false;

        // Check if the type is List<T>
        var typeName = nonNullableNamedType.Name;
        var containingNamespace = nonNullableNamedType.ContainingNamespace?.ToDisplayString();
        return typeName == "List" && containingNamespace == "System.Collections.Generic";
    }

    /// <summary>
    /// Formats chained method calls (like .ToList()) with proper indentation
    /// </summary>
    private static string FormatChainedMethods(string chainedMethods, string spaces)
    {
        if (string.IsNullOrEmpty(chainedMethods))
            return "";

        // Normalize whitespace from chained method calls
        var normalized = System.Text.RegularExpressions.Regex.Replace(
            chainedMethods.Trim(),
            @"\s+",
            ""
        );

        // Each method call should be on a new line with proper indentation
        var result = new StringBuilder();
        var currentMethod = new StringBuilder();
        var parenDepth = 0;

        foreach (var c in normalized)
        {
            currentMethod.Append(c);

            if (c == '(')
                parenDepth++;
            else if (c == ')')
            {
                parenDepth--;
                if (parenDepth == 0)
                {
                    // Complete method call with parentheses
                    result.Append(CodeFormatter.DefaultNewLine);
                    result.Append(spaces);
                    result.Append(currentMethod);
                    currentMethod.Clear();
                }
            }
        }

        // Handle any remaining content (e.g., property access without parentheses like .Count)
        if (currentMethod.Length > 0)
        {
            result.Append(CodeFormatter.DefaultNewLine);
            result.Append(spaces);
            result.Append(currentMethod);
        }

        return result.ToString();
    }

    /// <summary>
    /// Converts nested SelectMany expressions using Roslyn syntax analysis
    /// </summary>
    protected string ConvertNestedSelectManyWithRoslyn(
        ExpressionSyntax syntax,
        DtoProperty? property,
        int indents
    )
    {
        var nestedStructure = property?.NestedStructure;
        var spaces = CodeFormatter.IndentSpaces(indents);
        var innerSpaces = CodeFormatter.IndentSpaces(indents + CodeFormatter.IndentSize);

        // Use Roslyn to extract SelectMany information
        var selectManyInfo = ExtractSelectManyInfoFromSyntax(syntax);
        if (selectManyInfo is null)
        {
            // Fallback to string representation
            return syntax.ToString();
        }

        var (
            baseExpression,
            paramName,
            chainedMethods,
            hasNullableAccess,
            coalescingDefaultValue,
            nullCheckExpression
        ) = selectManyInfo.Value;

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

        // Normalize nullCheckExpression if present
        if (nullCheckExpression is not null)
        {
            nullCheckExpression = System.Text.RegularExpressions.Regex.Replace(
                nullCheckExpression.Trim(),
                @"\s+",
                " "
            );
            nullCheckExpression = System.Text.RegularExpressions.Regex.Replace(
                nullCheckExpression,
                @"\s*\.\s*",
                "."
            );
        }

        // If there's a nested structure, generate the Select with projection
        if (nestedStructure is not null)
        {
            var nestedClassName = GetClassName(nestedStructure);
            var nestedDtoName = string.IsNullOrEmpty(nestedClassName)
                ? ""
                : GetNestedDtoFullName(nestedClassName);

            // Generate property assignments for nested DTO with proper formatting
            // Properties should be indented two levels from the base (one for SelectMany block, one for properties)
            var propertyIndentSpaces = CodeFormatter.IndentSpaces(
                indents + CodeFormatter.IndentSize * 2
            );
            var propertyAssignments = new List<string>();
            foreach (var prop in nestedStructure.Properties)
            {
                var assignment = GeneratePropertyAssignment(
                    prop,
                    indents + CodeFormatter.IndentSize * 2
                );
                propertyAssignments.Add($"{propertyIndentSpaces}{prop.Name} = {assignment}");
            }
            var propertiesCode = string.Join(
                $",{CodeFormatter.DefaultNewLine}",
                propertyAssignments
            );

            // Format chained methods with proper indentation (one level from base)
            var formattedChainedMethods = FormatChainedMethods(chainedMethods, innerSpaces);

            // Build the SelectMany expression with projection and proper formatting
            // The .SelectMany, {, }, and chained methods should all be indented one level from the property assignment
            if (hasNullableAccess)
            {
                // Use nullCheckExpression for the null check (defaults to baseExpression if not provided)
                var checkExpr = nullCheckExpression ?? baseExpression;

                // Determine default value based on the expression's type
                // If it's a collection type, use empty enumerable/list; otherwise use null
                string defaultValue;
                if (coalescingDefaultValue is not null)
                {
                    defaultValue = coalescingDefaultValue;
                }
                else if (
                    string.IsNullOrEmpty(nestedDtoName)
                    || (
                        property is not null
                        && !RoslynTypeHelper.IsCollectionType(property.TypeSymbol)
                    )
                )
                {
                    // For single element results or anonymous types, default is null
                    defaultValue = "null";
                }
                else
                {
                    // For collections, determine the appropriate empty collection type
                    defaultValue = GetEmptyCollectionExpression(
                        property?.TypeSymbol,
                        nestedDtoName,
                        chainedMethods
                    );
                }

                var code = $$"""
                    {{checkExpr}} != null ? {{baseExpression}}
                    {{innerSpaces}}.SelectMany({{paramName}} => new {{nestedDtoName}}
                    {{innerSpaces}}{
                    {{propertiesCode}}
                    {{innerSpaces}}}){{formattedChainedMethods}} : {{defaultValue}}
                    """;
                return code;
            }
            else
            {
                var code = $$"""
                    {{baseExpression}}
                    {{innerSpaces}}.SelectMany({{paramName}} => new {{nestedDtoName}}
                    {{innerSpaces}}{
                    {{propertiesCode}}
                    {{innerSpaces}}}){{formattedChainedMethods}}
                    """;
                return code;
            }
        }
        else
        {
            // No nested structure, just use the SelectMany as-is
            // This handles cases like: c => c.GrandChildren (simple member access)
            if (hasNullableAccess)
            {
                var defaultValue = coalescingDefaultValue ?? "System.Linq.Enumerable.Empty()";
                return $"{baseExpression} != null ? {syntax} : {defaultValue}";
            }
            else
            {
                return syntax.ToString();
            }
        }
    }

    /// <summary>
    /// Removes all comment trivia from a syntax node
    /// </summary>
    private static T RemoveComments<T>(T node)
        where T : SyntaxNode
    {
        return (T)
            node.ReplaceTrivia(
                node.DescendantTrivia(descendIntoTrivia: true),
                (originalTrivia, _) =>
                {
                    // Remove single-line and multi-line comments
                    if (
                        originalTrivia.IsKind(SyntaxKind.SingleLineCommentTrivia)
                        || originalTrivia.IsKind(SyntaxKind.MultiLineCommentTrivia)
                    )
                    {
                        return default;
                    }
                    return originalTrivia;
                }
            );
    }

    private (
        string baseExpression,
        string paramName,
        string chainedMethods,
        bool hasNullableAccess,
        string? coalescingDefaultValue,
        string? nullCheckExpression
    )? ExtractSelectManyInfoFromSyntax(ExpressionSyntax syntax)
    {
        var info = LinqMethodHelper.ExtractLinqInvocationInfo(syntax, "SelectMany");
        if (info is null)
            return null;

        // Apply comment removal to base expression
        var cleanedBaseExpression = RemoveComments(
                SyntaxFactory.ParseExpression(info.BaseExpression)
            )
            .ToString();

        // Apply comment removal to null check expression if present
        string? cleanedNullCheckExpression = null;
        if (info.NullCheckExpression is not null)
        {
            cleanedNullCheckExpression = RemoveComments(
                    SyntaxFactory.ParseExpression(info.NullCheckExpression)
                )
                .ToString();
        }

        return (
            cleanedBaseExpression,
            info.ParameterName,
            info.ChainedMethods,
            info.HasNullableAccess,
            info.CoalescingDefaultValue,
            cleanedNullCheckExpression
        );
    }

    private (
        string baseExpression,
        string paramName,
        string chainedMethods,
        bool hasNullableAccess,
        string? coalescingDefaultValue,
        string? nullCheckExpression
    )? ExtractSelectInfoFromSyntax(ExpressionSyntax syntax)
    {
        var info = LinqMethodHelper.ExtractLinqInvocationInfo(syntax, "Select");
        if (info is null)
            return null;

        // Apply comment removal to base expression
        var cleanedBaseExpression = RemoveComments(
                SyntaxFactory.ParseExpression(info.BaseExpression)
            )
            .ToString();

        // Apply comment removal to null check expression if present
        string? cleanedNullCheckExpression = null;
        if (info.NullCheckExpression is not null)
        {
            cleanedNullCheckExpression = RemoveComments(
                    SyntaxFactory.ParseExpression(info.NullCheckExpression)
                )
                .ToString();
        }

        return (
            cleanedBaseExpression,
            info.ParameterName,
            info.ChainedMethods,
            info.HasNullableAccess,
            info.CoalescingDefaultValue,
            cleanedNullCheckExpression
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

        // Remove comments from syntax before processing
        var cleanSyntax = RemoveComments(syntax);
        var cleanExpression = cleanSyntax.ToString();

        // Build the access path without ?. operators
        var accessPath = cleanExpression.Replace("?.", ".");

        // Build null checks using string manipulation (proven to work)
        var checks = new List<string>();
        var parts = cleanExpression.Split(["?."], StringSplitOptions.None);

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

    /// <summary>
    /// Converts an object creation expression to use fully qualified type names
    /// </summary>
    protected string ConvertObjectCreationToFullyQualified(
        ObjectCreationExpressionSyntax objectCreation
    )
    {
        var typeInfo = SemanticModel.GetTypeInfo(objectCreation);
        if (typeInfo.Type is not INamedTypeSymbol typeSymbol)
        {
            // Fallback to original expression if type cannot be resolved
            return objectCreation.ToString();
        }

        // Get the fully qualified type name
        var fullyQualifiedTypeName = typeSymbol.ToDisplayString(
            SymbolDisplayFormat.FullyQualifiedFormat
        );

        // Build the new object creation expression with fully qualified type name
        var result = new StringBuilder();
        result.Append("new ");
        result.Append(fullyQualifiedTypeName);

        // Preserve argument list if present
        if (objectCreation.ArgumentList != null)
        {
            result.Append(objectCreation.ArgumentList.ToString());
        }

        // Preserve initializer if present
        if (objectCreation.Initializer != null)
        {
            result.Append(' ');
            result.Append(objectCreation.Initializer.ToString());
        }

        return result.ToString();
    }
}
