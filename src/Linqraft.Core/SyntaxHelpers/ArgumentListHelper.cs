using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Linqraft.Core.SyntaxHelpers;

/// <summary>
/// Helper methods for manipulating ArgumentList while preserving trivia and structure
/// </summary>
public static class ArgumentListHelper
{
    /// <summary>
    /// Adds a new argument to an argument list while preserving all trivia and formatting
    /// </summary>
    /// <param name="argumentList">The original argument list</param>
    /// <param name="newArgument">The new argument to add</param>
    /// <returns>A new ArgumentListSyntax with the argument added and trivia preserved</returns>
    public static ArgumentListSyntax AddArgument(
        ArgumentListSyntax argumentList,
        ArgumentSyntax newArgument
    )
    {
        var arguments = argumentList.Arguments.ToList();
        var separators = argumentList.Arguments.GetSeparators().ToList();

        if (arguments.Count > 0)
        {
            // Get the trailing trivia from the last argument
            var lastArg = arguments[arguments.Count - 1];
            var lastArgTrailingTrivia = lastArg.GetTrailingTrivia();
            
            // Remove trailing trivia from the last argument
            arguments[arguments.Count - 1] = lastArg.WithoutTrailingTrivia();

            // Add comma separator with just a space (no other trivia)
            separators.Add(SyntaxFactory.Token(SyntaxKind.CommaToken).WithTrailingTrivia(SyntaxFactory.Space));

            // Add the new argument, removing any leading trivia and using the original trailing trivia
            arguments.Add(newArgument.WithoutLeadingTrivia().WithTrailingTrivia(lastArgTrailingTrivia));
        }
        else
        {
            // First argument
            arguments.Add(newArgument);
        }

        var newSeparatedList = SyntaxFactory.SeparatedList(arguments, separators);

        return SyntaxFactory.ArgumentList(
            argumentList.OpenParenToken,
            newSeparatedList,
            argumentList.CloseParenToken
        );
    }

    /// <summary>
    /// Replaces an argument at a specific index while preserving all trivia
    /// </summary>
    /// <param name="argumentList">The original argument list</param>
    /// <param name="index">The index of the argument to replace</param>
    /// <param name="newArgument">The new argument</param>
    /// <returns>A new ArgumentListSyntax with the argument replaced and trivia preserved</returns>
    public static ArgumentListSyntax ReplaceArgument(
        ArgumentListSyntax argumentList,
        int index,
        ArgumentSyntax newArgument
    )
    {
        if (index < 0 || index >= argumentList.Arguments.Count)
        {
            return argumentList;
        }

        var arguments = argumentList.Arguments.ToList();
        var separators = argumentList.Arguments.GetSeparators().ToList();

        // Preserve trivia from the original argument
        var originalArg = arguments[index];
        var leadingTrivia = originalArg.GetLeadingTrivia();
        var trailingTrivia = originalArg.GetTrailingTrivia();
        
        // If the original argument has leading trivia with newlines/whitespace,
        // it means it was on a new line. We should move that to the separator before it.
        if (index > 0 && leadingTrivia.Any(t => t.IsKind(SyntaxKind.EndOfLineTrivia) || t.IsKind(SyntaxKind.WhitespaceTrivia)))
        {
            // Check if the separator before this argument has trailing newline trivia
            var separatorIndex = index - 1;
            if (separatorIndex < separators.Count)
            {
                var separator = separators[separatorIndex];
                var separatorTrailingTrivia = separator.TrailingTrivia;
                
                // If the separator has newline trivia, replace it with just a space
                if (separatorTrailingTrivia.Any(t => t.IsKind(SyntaxKind.EndOfLineTrivia) || t.IsKind(SyntaxKind.WhitespaceTrivia)))
                {
                    separators[separatorIndex] = SyntaxFactory.Token(SyntaxKind.CommaToken).WithTrailingTrivia(SyntaxFactory.Space);
                }
            }
            
            // Remove leading newline/whitespace trivia from the argument
            leadingTrivia = SyntaxTriviaList.Empty;
        }
        
        arguments[index] = newArgument
            .WithLeadingTrivia(leadingTrivia)
            .WithTrailingTrivia(trailingTrivia);

        var newSeparatedList = SyntaxFactory.SeparatedList(arguments, separators);

        return SyntaxFactory.ArgumentList(
            argumentList.OpenParenToken,
            newSeparatedList,
            argumentList.CloseParenToken
        );
    }

    /// <summary>
    /// Removes an argument at a specific index while preserving all trivia
    /// </summary>
    /// <param name="argumentList">The original argument list</param>
    /// <param name="index">The index of the argument to remove</param>
    /// <returns>A new ArgumentListSyntax with the argument removed and trivia preserved</returns>
    public static ArgumentListSyntax RemoveArgument(
        ArgumentListSyntax argumentList,
        int index
    )
    {
        if (index < 0 || index >= argumentList.Arguments.Count)
        {
            return argumentList;
        }

        var arguments = argumentList.Arguments.ToList();
        var separators = argumentList.Arguments.GetSeparators().ToList();

        // If removing the last argument and there's a separator before it, remove that separator
        if (index == arguments.Count - 1 && separators.Count > 0)
        {
            // Move trailing trivia from the removed argument to the new last argument
            var removedArgTrailingTrivia = arguments[index].GetTrailingTrivia();
            if (index > 0)
            {
                arguments[index - 1] = arguments[index - 1].WithTrailingTrivia(removedArgTrailingTrivia);
            }

            arguments.RemoveAt(index);
            separators.RemoveAt(separators.Count - 1);
        }
        else if (index < arguments.Count - 1)
        {
            // Removing an argument that's not the last one
            arguments.RemoveAt(index);
            if (index < separators.Count)
            {
                separators.RemoveAt(index);
            }
        }
        else
        {
            arguments.RemoveAt(index);
        }

        var newSeparatedList = SyntaxFactory.SeparatedList(arguments, separators);

        return SyntaxFactory.ArgumentList(
            argumentList.OpenParenToken,
            newSeparatedList,
            argumentList.CloseParenToken
        );
    }

    /// <summary>
    /// Updates multiple arguments in an argument list while preserving trivia
    /// </summary>
    /// <param name="argumentList">The original argument list</param>
    /// <param name="updates">Dictionary mapping indices to new arguments</param>
    /// <returns>A new ArgumentListSyntax with arguments updated and trivia preserved</returns>
    public static ArgumentListSyntax UpdateArguments(
        ArgumentListSyntax argumentList,
        Dictionary<int, ArgumentSyntax> updates
    )
    {
        var arguments = argumentList.Arguments.ToList();
        var separators = argumentList.Arguments.GetSeparators().ToList();

        foreach (var kvp in updates.OrderBy(x => x.Key))
        {
            if (kvp.Key >= 0 && kvp.Key < arguments.Count)
            {
                var originalArg = arguments[kvp.Key];
                arguments[kvp.Key] = kvp.Value
                    .WithLeadingTrivia(originalArg.GetLeadingTrivia())
                    .WithTrailingTrivia(originalArg.GetTrailingTrivia());
            }
        }

        var newSeparatedList = SyntaxFactory.SeparatedList(arguments, separators);

        return SyntaxFactory.ArgumentList(
            argumentList.OpenParenToken,
            newSeparatedList,
            argumentList.CloseParenToken
        );
    }
}
