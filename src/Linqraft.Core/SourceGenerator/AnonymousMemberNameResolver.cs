using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Linqraft.SourceGenerator;

/// <summary>
/// Resolves anonymous member name.
/// </summary>
internal static class AnonymousMemberNameResolver
{
    /// <summary>
    /// Handles get.
    /// </summary>
    public static string Get(ExpressionSyntax expression)
    {
        return TryGet(expression, out var name) ? name : expression.ToString();
    }

    /// <summary>
    /// Attempts to handle get.
    /// </summary>
    private static bool TryGet(ExpressionSyntax expression, out string name)
    {
        switch (expression)
        {
            case IdentifierNameSyntax identifier:
                name = identifier.Identifier.ValueText;
                return true;
            case MemberAccessExpressionSyntax memberAccess:
                name = memberAccess.Name.Identifier.ValueText;
                return true;
            case MemberBindingExpressionSyntax memberBinding:
                name = memberBinding.Name.Identifier.ValueText;
                return true;
            case GenericNameSyntax genericName:
                name = genericName.Identifier.ValueText;
                return true;
            case InvocationExpressionSyntax invocation:
                return TryGet(invocation.Expression, out name);
            case ConditionalAccessExpressionSyntax conditionalAccess
                when conditionalAccess.WhenNotNull is ExpressionSyntax whenNotNull:
                return TryGet(whenNotNull, out name);
            default:
                name = expression.ToString();
                return false;
        }
    }
}
