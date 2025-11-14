using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Linqraft;

/// <summary>
/// Base record for SelectExpr information
/// </summary>
internal abstract record SelectExprInfo
{
    public required ITypeSymbol SourceType { get; init; }
    public required SemanticModel SemanticModel { get; init; }
    public required InvocationExpressionSyntax Invocation { get; init; }
    public required string LambdaParameterName { get; init; }
    public required string CallerNamespace { get; init; }

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

    // Get namespace string (returns the namespace where SelectExpr is invoked)
    public string GetNamespaceString()
    {
        return CallerNamespace;
    }

    // Check if the invocation source is IEnumerable (not IQueryable)
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

    // Get the return type string based on whether it's IQueryable or IEnumerable
    protected string GetReturnTypePrefix()
    {
        return IsEnumerableInvocation() ? "IEnumerable" : "IQueryable";
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

    protected string ConvertNestedSelectWithRoslyn(
        ExpressionSyntax syntax,
        DtoStructure nestedStructure,
        int indents
    )
    {
        var spaces = new string(' ', indents);
        var nestedDtoName = GetClassName(nestedStructure);

        // Use Roslyn to extract Select information
        var selectInfo = ExtractSelectInfoFromSyntax(syntax);
        if (selectInfo is null)
        {
            // Fallback to string representation
            return syntax.ToString();
        }

        var (baseExpression, paramName, chainedMethods, hasNullableAccess, coalescingDefaultValue) =
            selectInfo.Value;

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

    protected string ConvertNestedSelect(
        string expression,
        DtoStructure nestedStructure,
        int indents
    )
    {
        var spaces = new string(' ', indents);
        // Example: s.Childs.Select(c => new { ... }) or s.Childs.Select(c => new { ... }).ToList()
        // Also handle: s.Childs?.Select(c => new { ... }) ?? []

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

        // Check if there's a ?. before .Select and remove the ?
        if (baseExpression.EndsWith("?"))
        {
            baseExpression = baseExpression[..^1];
        }

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
        // Extract any chained method calls after Select(...) (e.g., ".ToList()", " ?? []")
        var chainedMethods = selectEnd < expression.Length ? expression[selectEnd..] : "";

        var propertyAssignments = new List<string>();
        foreach (var prop in nestedStructure.Properties)
        {
            var assignment = GeneratePropertyAssignment(prop, indents + 4);
            propertyAssignments.Add($"{spaces}    {prop.Name} = {assignment},");
        }
        var propertiesCode = string.Join("\n", propertyAssignments);

        // Check if the base expression (before .Select) uses ?. (nullable access)
        // Only check the part before .Select, not inside the lambda
        var originalBaseExpression = expression[..selectIndex];
        // Check if it contains ?. OR ends with ? (which means ?.Select)
        var hasNullableAccess =
            originalBaseExpression.Contains("?.") || originalBaseExpression.EndsWith("?");
        if (hasNullableAccess)
        {
            // Convert s.OrderItems?.Select(...) ?? [] to:
            // s.OrderItems != null ? s.OrderItems.Select(...) : []

            // Build null checks for all nullable parts
            var checks = new List<string>();
            var accessPath = baseExpression; // baseExpression is already without the ?

            // Check if originalBaseExpression ends with ? (from ?.)
            if (originalBaseExpression.EndsWith("?"))
            {
                // Simple case: s.OrderItems?
                checks.Add($"{baseExpression} != null");
            }
            else
            {
                // Complex case with nested ?.
                var parts = originalBaseExpression.Split(["?."], StringSplitOptions.None);
                if (parts.Length >= 2)
                {
                    var currentPath = parts[0];
                    for (int i = 1; i < parts.Length; i++)
                    {
                        checks.Add($"{currentPath} != null");
                        var nextPart = parts[i];
                        var dotIndex = nextPart.IndexOf('.');
                        var propertyName = dotIndex > 0 ? nextPart[..dotIndex] : nextPart;
                        currentPath = $"{currentPath}.{propertyName}";
                    }
                }
            }

            var nullCheckPart = string.Join(" && ", checks);

            // Extract the default value from chained methods (e.g., "?? []")
            var defaultValue = "default";
            if (chainedMethods.Contains("??"))
            {
                var coalesceIndex = chainedMethods.IndexOf("??");
                var rawDefault = chainedMethods[(coalesceIndex + 2)..].Trim();
                // Replace [] with appropriate default for expression trees
                if (rawDefault == "[]")
                {
                    if (string.IsNullOrEmpty(nestedDtoName))
                    {
                        // For anonymous types, we cannot use Enumerable.Empty with anonymous type
                        // Instead, we need to use Array.Empty or new[]{} but that also doesn't work
                        // Best approach: use the Select expression itself to return the right type
                        // We'll wrap the whole Select in a cast to ensure type safety
                        defaultValue = "null";
                    }
                    else
                    {
                        defaultValue = "System.Linq.Enumerable.Empty<" + nestedDtoName + ">()";
                    }
                }
                else
                {
                    defaultValue = rawDefault;
                }
                chainedMethods = chainedMethods[..coalesceIndex].Trim();
            }

            var code = $$"""
                {{nullCheckPart}} ? {{accessPath}}.Select({{paramName}} => new {{nestedDtoName}} {
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
