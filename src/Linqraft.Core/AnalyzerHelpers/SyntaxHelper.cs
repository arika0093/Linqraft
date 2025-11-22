using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Linqraft.Core.AnalyzerHelpers;

/// <summary>
/// Helper methods for common syntax operations in analyzers
/// </summary>
public static class SyntaxHelper
{
    /// <summary>
    /// Gets the location of the method name in an invocation expression.
    /// For member access expressions (obj.Method()), returns the location of the method name.
    /// Otherwise, returns the location of the entire expression.
    /// </summary>
    /// <param name="expression">The invocation expression</param>
    /// <returns>The location of the method name</returns>
    public static Location GetMethodNameLocation(ExpressionSyntax expression)
    {
        if (expression is MemberAccessExpressionSyntax memberAccess)
        {
            return memberAccess.Name.GetLocation();
        }
        return expression.GetLocation();
    }

    /// <summary>
    /// Determines if an identifier is part of a member access expression.
    /// This includes being the right side of a member access (obj.Property),
    /// member binding (?Property), property name in anonymous objects,
    /// or the left side of an assignment in object initializers.
    /// </summary>
    /// <param name="identifier">The identifier to check</param>
    /// <returns>True if the identifier is part of a member access</returns>
    public static bool IsPartOfMemberAccess(IdentifierNameSyntax identifier)
    {
        var parent = identifier.Parent;

        if (parent is MemberAccessExpressionSyntax memberAccess)
        {
            return memberAccess.Name == identifier;
        }

        if (parent is MemberBindingExpressionSyntax)
        {
            return true;
        }

        // Check if this is inside a NameEquals (property name in anonymous object)
        if (parent is NameEqualsSyntax)
        {
            return true;
        }

        // Check if this is the left side of an assignment in an object initializer
        // e.g., in "new MyClass { Id = s.Id }", the left "Id" is the property being assigned
        if (parent is AssignmentExpressionSyntax assignment && assignment.Left == identifier)
        {
            return true;
        }

        return false;
    }
}
