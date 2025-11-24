using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Linqraft.Core.SyntaxHelpers;

/// <summary>
/// Helper methods for converting between object creation expressions while preserving trivia and structure.
/// 
/// <para>This helper centralizes the trivia-preserving replacement logic that was originally implemented
/// in AnonymousTypeToDtoCodeFixProvider. All conversions maintain:</para>
/// 
/// <list type="bullet">
/// <item><description>Original separators (commas with their trivia) using GetSeparators()</description></item>
/// <item><description>Brace tokens and their trivia (OpenBraceToken, CloseBraceToken)</description></item>
/// <item><description>Leading and trailing trivia on expressions and members</description></item>
/// <item><description>Trivia on the 'new' keyword</description></item>
/// <item><description>Trivia from the type node that appears between 'new' and the opening brace</description></item>
/// </list>
/// 
/// <para>This ensures that code formatting, indentation, comments, and whitespace are preserved
/// when performing syntax transformations in code fix providers.</para>
/// </summary>
/// <remarks>
/// Example usage in a code fix provider:
/// <code>
/// // Convert anonymous to named type
/// var namedObject = ObjectCreationHelper.ConvertToNamedType(
///     anonymousObject,
///     "MyDto",
///     convertMemberCallback: member => /* custom transformation */
/// );
/// 
/// // Convert named to anonymous type
/// var anonymousObject = ObjectCreationHelper.ConvertToAnonymousType(objectCreation);
/// 
/// // Recursively convert nested objects
/// var anonymousObject = ObjectCreationHelper.ConvertToAnonymousTypeRecursive(objectCreation);
/// </code>
/// </remarks>
public static class ObjectCreationHelper
{
    /// <summary>
    /// Converts an anonymous object creation to a named object creation, preserving all trivia and structure.
    /// This is the reverse of <see cref="ConvertToAnonymousType"/>.
    /// </summary>
    /// <param name="anonymousObject">The anonymous object creation expression to convert</param>
    /// <param name="typeName">The name of the type to create</param>
    /// <param name="convertMemberCallback">Optional callback to transform each member during conversion</param>
    /// <returns>An object creation expression with preserved trivia</returns>
    public static ObjectCreationExpressionSyntax ConvertToNamedType(
        AnonymousObjectCreationExpressionSyntax anonymousObject,
        string typeName,
        System.Func<AnonymousObjectMemberDeclaratorSyntax, ExpressionSyntax>? convertMemberCallback = null
    )
    {
        // Convert anonymous object initializers to regular object initializers
        var newInitializers = new List<ExpressionSyntax>();

        foreach (var initializer in anonymousObject.Initializers)
        {
            var newInitializer = ConvertAnonymousMemberToAssignment(initializer, convertMemberCallback);
            newInitializers.Add(newInitializer);
        }

        // Preserve the original separators (commas with their trivia)
        var originalSeparators = anonymousObject.Initializers.GetSeparators().ToList();
        var newSeparatedList = SyntaxFactory.SeparatedList(newInitializers, originalSeparators);

        // Create new initializer expression preserving the original braces and their trivia
        var newInitializerExpression = SyntaxFactory.InitializerExpression(
            SyntaxKind.ObjectInitializerExpression,
            anonymousObject.OpenBraceToken,
            newSeparatedList,
            anonymousObject.CloseBraceToken
        );

        // Create the object creation expression, replacing "new" with "new TypeName"
        var newObjectCreation = SyntaxFactory
            .ObjectCreationExpression(
                SyntaxFactory
                    .Token(SyntaxKind.NewKeyword)
                    .WithLeadingTrivia(anonymousObject.NewKeyword.LeadingTrivia)
                    .WithTrailingTrivia(SyntaxFactory.Space),
                SyntaxFactory.IdentifierName(typeName),
                null, // no argument list
                newInitializerExpression
            )
            .WithTrailingTrivia(anonymousObject.GetTrailingTrivia());

        return newObjectCreation;
    }

    /// <summary>
    /// Converts an object creation to an anonymous object creation, preserving all trivia and structure.
    /// This is the reverse of <see cref="ConvertToNamedType"/>.
    /// </summary>
    /// <param name="objectCreation">The object creation expression to convert</param>
    /// <param name="convertExpressionCallback">Optional callback to transform expressions during conversion</param>
    /// <returns>An anonymous object creation expression with preserved trivia</returns>
    public static AnonymousObjectCreationExpressionSyntax ConvertToAnonymousType(
        ObjectCreationExpressionSyntax objectCreation,
        System.Func<ExpressionSyntax, ExpressionSyntax>? convertExpressionCallback = null
    )
    {
        // Convert object initializer to anonymous object creation
        if (objectCreation.Initializer == null)
        {
            return SyntaxFactory.AnonymousObjectCreationExpression();
        }

        var members = new List<AnonymousObjectMemberDeclaratorSyntax>();

        foreach (var expression in objectCreation.Initializer.Expressions)
        {
            if (expression is AssignmentExpressionSyntax assignment)
            {
                // Convert assignment like "Id = x.Id" to anonymous member
                if (assignment.Left is IdentifierNameSyntax identifier)
                {
                    var processedRight = convertExpressionCallback?.Invoke(assignment.Right) ?? assignment.Right;

                    // Create the name equals with preserved assignment leading trivia
                    var nameEquals = SyntaxFactory
                        .NameEquals(SyntaxFactory.IdentifierName(identifier.Identifier))
                        .WithLeadingTrivia(assignment.GetLeadingTrivia());

                    // Create the member with preserved trivia from the assignment
                    var member = SyntaxFactory
                        .AnonymousObjectMemberDeclarator(nameEquals, processedRight)
                        .WithTrailingTrivia(assignment.GetTrailingTrivia());

                    members.Add(member);
                }
            }
        }

        // Create the separated list preserving separators from the original initializer
        var separatedMembers = SyntaxFactory.SeparatedList(
            members,
            objectCreation.Initializer.Expressions.GetSeparators()
        );

        // Collect trivia from the type node that appears between 'new' and the opening brace
        var triviaBeforeOpenBrace = SyntaxTriviaList.Empty;
        if (objectCreation.Type != null)
        {
            triviaBeforeOpenBrace = triviaBeforeOpenBrace
                .AddRange(objectCreation.Type.GetLeadingTrivia())
                .AddRange(objectCreation.Type.GetTrailingTrivia());
        }

        // Create anonymous object creation preserving trivia
        var result = SyntaxFactory
            .AnonymousObjectCreationExpression(
                SyntaxFactory
                    .Token(SyntaxKind.NewKeyword)
                    .WithLeadingTrivia(objectCreation.GetLeadingTrivia())
                    .WithTrailingTrivia(triviaBeforeOpenBrace),
                SyntaxFactory
                    .Token(SyntaxKind.OpenBraceToken)
                    .WithTrailingTrivia(objectCreation.Initializer.OpenBraceToken.TrailingTrivia),
                separatedMembers,
                SyntaxFactory
                    .Token(SyntaxKind.CloseBraceToken)
                    .WithLeadingTrivia(objectCreation.Initializer.CloseBraceToken.LeadingTrivia)
                    .WithTrailingTrivia(objectCreation.Initializer.CloseBraceToken.TrailingTrivia)
            )
            .WithTrailingTrivia(objectCreation.GetTrailingTrivia());

        return result;
    }

    /// <summary>
    /// Converts an anonymous object creation to an anonymous object creation recursively,
    /// converting all nested object creations to anonymous objects as well.
    /// </summary>
    /// <param name="objectCreation">The object creation expression to convert</param>
    /// <returns>An anonymous object creation expression with all nested objects also converted</returns>
    public static AnonymousObjectCreationExpressionSyntax ConvertToAnonymousTypeRecursive(
        ObjectCreationExpressionSyntax objectCreation
    )
    {
        return ConvertToAnonymousType(
            objectCreation,
            convertExpressionCallback: ProcessExpressionForNestedConversions
        );
    }

    /// <summary>
    /// Converts an anonymous member declarator to an assignment expression
    /// </summary>
    private static ExpressionSyntax ConvertAnonymousMemberToAssignment(
        AnonymousObjectMemberDeclaratorSyntax initializer,
        System.Func<AnonymousObjectMemberDeclaratorSyntax, ExpressionSyntax>? convertMemberCallback
    )
    {
        // If a callback is provided, use it
        if (convertMemberCallback != null)
        {
            return convertMemberCallback(initializer);
        }

        // Default conversion logic
        if (initializer.NameEquals != null)
        {
            // Explicit name: Name = value
            return CreateAssignmentFromNameEquals(initializer);
        }
        else
        {
            // Implicit name: x.Property becomes Property = x.Property
            return CreateAssignmentFromImplicitProperty(initializer);
        }
    }

    /// <summary>
    /// Creates an assignment expression from an anonymous object initializer with explicit name
    /// </summary>
    private static ExpressionSyntax CreateAssignmentFromNameEquals(
        AnonymousObjectMemberDeclaratorSyntax initializer
    )
    {
        var propertyName = initializer.NameEquals!.Name.Identifier.Text;

        // Create assignment expression without trivia first
        var assignment = SyntaxFactory.AssignmentExpression(
            SyntaxKind.SimpleAssignmentExpression,
            SyntaxFactory.IdentifierName(propertyName).WithTrailingTrivia(SyntaxFactory.Space),
            SyntaxFactory.Token(SyntaxKind.EqualsToken).WithTrailingTrivia(SyntaxFactory.Space),
            initializer.Expression
        );

        // Apply trivia from the original initializer
        return assignment
            .WithLeadingTrivia(initializer.GetLeadingTrivia())
            .WithTrailingTrivia(initializer.GetTrailingTrivia());
    }

    /// <summary>
    /// Creates an assignment expression from an anonymous object initializer with implicit name
    /// </summary>
    private static ExpressionSyntax CreateAssignmentFromImplicitProperty(
        AnonymousObjectMemberDeclaratorSyntax initializer
    )
    {
        var propertyName = ExpressionHelper.GetPropertyNameOrDefault(initializer.Expression, "Property");

        // Create assignment expression without trivia first
        var assignment = SyntaxFactory.AssignmentExpression(
            SyntaxKind.SimpleAssignmentExpression,
            SyntaxFactory.IdentifierName(propertyName).WithTrailingTrivia(SyntaxFactory.Space),
            SyntaxFactory.Token(SyntaxKind.EqualsToken).WithTrailingTrivia(SyntaxFactory.Space),
            initializer.Expression
        );

        // Apply trivia from the original initializer
        return assignment
            .WithLeadingTrivia(initializer.GetLeadingTrivia())
            .WithTrailingTrivia(initializer.GetTrailingTrivia());
    }

    /// <summary>
    /// Processes an expression to recursively convert nested object creations to anonymous objects
    /// </summary>
    private static ExpressionSyntax ProcessExpressionForNestedConversions(ExpressionSyntax expression)
    {
        // Use a recursive rewriter to find and convert all nested object creations
        var rewriter = new NestedObjectCreationRewriter();
        return (ExpressionSyntax)rewriter.Visit(expression);
    }

    /// <summary>
    /// Syntax rewriter that converts ObjectCreationExpressionSyntax to AnonymousObjectCreationExpressionSyntax
    /// </summary>
    private class NestedObjectCreationRewriter : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitObjectCreationExpression(
            ObjectCreationExpressionSyntax node
        )
        {
            // Only convert if it has an initializer with assignment expressions
            // Don't convert things like "new List()" or "new List<T>()"
            if (node.Initializer != null && HasAssignmentExpressions(node.Initializer))
            {
                // Convert this object creation to anonymous type (non-recursively to avoid infinite loop)
                var anonymousType = ConvertToAnonymousType(node);

                // Continue visiting children to convert nested object creations
                return base.Visit(anonymousType) ?? anonymousType;
            }

            // For object creations without proper initializers, don't convert but still visit children
            return base.VisitObjectCreationExpression(node);
        }

        private static bool HasAssignmentExpressions(InitializerExpressionSyntax initializer)
        {
            return initializer.Expressions.Any(e => e is AssignmentExpressionSyntax);
        }
    }
}
