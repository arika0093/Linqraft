using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Linqraft.Core;

/// <summary>
/// Helper class for generating DTO class names
/// </summary>
public static class DtoNamingHelper
{
    /// <summary>
    /// Generates a DTO class name based on the invocation context and anonymous type structure
    /// </summary>
    /// <param name="invocation">The SelectExpr invocation expression</param>
    /// <param name="anonymousType">The anonymous type creation expression</param>
    /// <returns>A generated DTO class name</returns>
    public static string GenerateDtoName(
        InvocationExpressionSyntax invocation,
        AnonymousObjectCreationExpressionSyntax anonymousType
    )
    {
        string baseName;

        // Try to infer a name from the context
        // Walk up the tree to find the relevant context
        var current = invocation.Parent;
        while (current != null)
        {
            switch (current)
            {
                // Check for variable declaration: var name = query.SelectExpr(...)
                case EqualsValueClauseSyntax equalsValue
                    when equalsValue.Parent is VariableDeclaratorSyntax declarator:
                    var varName = declarator.Identifier.Text;
                    baseName = ToPascalCase(varName) + "Dto";
                    return baseName + "_" + GenerateHash(anonymousType);

                // Check for assignment: name = query.SelectExpr(...)
                case AssignmentExpressionSyntax assignment
                    when assignment.Left is IdentifierNameSyntax identifier:
                    baseName = ToPascalCase(identifier.Identifier.Text) + "Dto";
                    return baseName + "_" + GenerateHash(anonymousType);

                // Stop at statement level
                case StatementSyntax:
                    break;

                default:
                    current = current.Parent;
                    continue;
            }
            break;
        }

        // Check for return statement with method name
        var methodDecl = invocation.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
        if (methodDecl != null)
        {
            var methodName = methodDecl.Identifier.Text;
            // Remove Get prefix if present
            if (methodName.StartsWith("Get"))
            {
                methodName = methodName.Substring(3);
            }
            baseName = methodName + "Dto";
            return baseName + "_" + GenerateHash(anonymousType);
        }

        // Default fallback
        return "ResultDto_" + GenerateHash(anonymousType);
    }

    /// <summary>
    /// Generates a hash based on the anonymous type's property names
    /// </summary>
    /// <param name="anonymousType">The anonymous type creation expression</param>
    /// <returns>An 8-character hash string</returns>
    private static string GenerateHash(AnonymousObjectCreationExpressionSyntax anonymousType)
    {
        // Generate hash based on property names
        var sb = new StringBuilder();
        foreach (var initializer in anonymousType.Initializers)
        {
            string propertyName;
            if (initializer.NameEquals != null)
            {
                propertyName = initializer.NameEquals.Name.Identifier.Text;
            }
            else
            {
                propertyName = GetPropertyNameFromExpression(initializer.Expression);
            }
            sb.Append(propertyName);
            sb.Append(';');
        }

        // Use common hash utility
        return HashUtility.GenerateAlphanumericHash(sb.ToString());
    }

    /// <summary>
    /// Extracts the property name from an expression
    /// </summary>
    /// <param name="expression">The expression to extract the property name from</param>
    /// <returns>The extracted property name or "Property" if unable to extract</returns>
    public static string GetPropertyNameFromExpression(ExpressionSyntax expression)
    {
        return expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.Text,
            IdentifierNameSyntax identifier => identifier.Identifier.Text,
            ConditionalAccessExpressionSyntax conditionalAccess
                when conditionalAccess.WhenNotNull is MemberBindingExpressionSyntax memberBinding =>
                memberBinding.Name.Identifier.Text,
            _ => "Property",
        };
    }

    /// <summary>
    /// Converts a string to PascalCase
    /// </summary>
    /// <param name="name">The string to convert</param>
    /// <returns>The PascalCase version of the string</returns>
    public static string ToPascalCase(string name)
    {
        if (string.IsNullOrEmpty(name))
            return name;
        return char.ToUpperInvariant(name[0]) + name.Substring(1);
    }

    /// <summary>
    /// Generates a DTO class name based on the anonymous object context (without hash)
    /// </summary>
    /// <param name="anonymousObject">The anonymous object creation expression</param>
    /// <returns>A generated DTO class name</returns>
    public static string GenerateDtoClassName(
        AnonymousObjectCreationExpressionSyntax anonymousObject
    )
    {
        // Try to infer a name from the context
        var parent = anonymousObject.Parent;

        // Check for variable declaration: var name = new { ... }
        if (parent is EqualsValueClauseSyntax equalsValue)
        {
            if (equalsValue.Parent is VariableDeclaratorSyntax declarator)
            {
                var varName = declarator.Identifier.Text;
                return ToPascalCase(varName) + "Dto";
            }
        }

        // Check for assignment: name = new { ... }
        if (parent is AssignmentExpressionSyntax assignment)
        {
            if (assignment.Left is IdentifierNameSyntax identifier)
            {
                return ToPascalCase(identifier.Identifier.Text) + "Dto";
            }
        }

        // Check for return statement with method name
        var methodDecl = anonymousObject
            .Ancestors()
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault();
        if (methodDecl != null)
        {
            var methodName = methodDecl.Identifier.Text;
            // Remove Get prefix if present
            if (methodName.StartsWith("Get"))
            {
                methodName = methodName.Substring(3);
            }
            return methodName + "Dto";
        }

        // Default fallback
        return "GeneratedDto";
    }
}
