using System.Linq;
using Linqraft.Core.SyntaxHelpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Linqraft.Core.AnalyzerHelpers;

/// <summary>
/// Helper methods for adding using directives in code fixes
/// </summary>
internal static class UsingDirectiveHelper
{
    /// <summary>
    /// Adds a using directive for the namespace containing the specified type symbol,
    /// if it doesn't already exist in the compilation unit.
    /// </summary>
    /// <param name="root">The syntax root (should be a CompilationUnitSyntax)</param>
    /// <param name="typeSymbol">The type symbol whose namespace should be imported</param>
    /// <returns>The updated syntax root with the using directive added, or the original root if not applicable</returns>
    public static SyntaxNode AddUsingDirectiveForType(SyntaxNode root, ITypeSymbol typeSymbol)
    {
        if (root is not CompilationUnitSyntax compilationUnit)
            return root;

        // Get the namespace of the type
        var namespaceName = typeSymbol.ContainingNamespace?.ToDisplayString();
        if (string.IsNullOrEmpty(namespaceName) || namespaceName == "<global namespace>")
            return root;

        // Check if using directive already exists
        var hasUsing = compilationUnit.Usings.Any(u => u.Name?.ToString() == namespaceName);
        if (hasUsing)
            return root;

        // Detect the line ending used in the file by looking at existing using directives
        var endOfLineTrivia = compilationUnit.Usings.Any()
            ? compilationUnit.Usings.Last().GetTrailingTrivia().LastOrDefault()
            : TriviaHelper.EndOfLine();

        // If the detected trivia is not an end of line, use a default
        if (!endOfLineTrivia.IsKind(SyntaxKind.EndOfLineTrivia))
        {
            endOfLineTrivia = TriviaHelper.EndOfLine();
        }

        // Add using directive (namespaceName is guaranteed non-null here due to the check above)
        var usingDirective = SyntaxFactory
            .UsingDirective(SyntaxFactory.ParseName(namespaceName!))
            .NormalizeWhitespace()
            .WithTrailingTrivia(endOfLineTrivia);

        return compilationUnit.AddUsings(usingDirective);
    }
}
