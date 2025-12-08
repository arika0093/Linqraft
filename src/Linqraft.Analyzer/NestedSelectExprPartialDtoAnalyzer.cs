using System.Collections.Generic;
using System.Linq;
using Linqraft.Core;
using Linqraft.Core.AnalyzerHelpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Linqraft.Analyzer;

/// <summary>
/// Analyzer that detects nested SelectExpr calls with explicit DTO types
/// that don't have corresponding partial class declarations.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class NestedSelectExprPartialDtoAnalyzer : BaseLinqraftAnalyzer
{
    public const string AnalyzerId = "LQRS006";

    private static readonly DiagnosticDescriptor RuleInstance = new(
        AnalyzerId,
        "Nested SelectExpr requires partial DTO class declarations",
        "Nested SelectExpr requires partial DTO class declarations. Missing: {0}.",
        "Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "When SelectExpr is used inside another SelectExpr with explicit DTO types, corresponding partial class declarations must exist for all DTO types.",
        helpLinkUri: $"https://github.com/arika0093/Linqraft/blob/main/docs/analyzer/{AnalyzerId}.md"
    );

    protected override string DiagnosticId => AnalyzerId;
    protected override LocalizableString Title => RuleInstance.Title;
    protected override LocalizableString MessageFormat => RuleInstance.MessageFormat;
    protected override LocalizableString Description => RuleInstance.Description;
    protected override string Category => "Usage";
    protected override DiagnosticSeverity Severity => DiagnosticSeverity.Error;
    protected override DiagnosticDescriptor Rule => RuleInstance;

    protected override void RegisterActions(AnalysisContext context)
    {
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        // Check if this is a SelectExpr call using semantic analysis
        if (!SelectExprHelper.IsSelectExprInvocation(invocation, context.SemanticModel))
        {
            return;
        }

        // Only analyze SelectExpr calls with explicit DTO types (2 type arguments)
        // Pattern: SelectExpr<TSource, TDto>(...)
        if (
            invocation.Expression is not MemberAccessExpressionSyntax memberAccess
            || memberAccess.Name is not GenericNameSyntax genericName
            || genericName.TypeArgumentList.Arguments.Count < 2
        )
        {
            return;
        }

        // Collect all DTO type names that should exist (both outer and nested)
        var requiredDtoTypes = new HashSet<string>();

        // Get the outer DTO type name (second type argument)
        var outerDtoTypeSyntax = genericName.TypeArgumentList.Arguments[1];
        var outerDtoTypeName = outerDtoTypeSyntax.ToString();
        requiredDtoTypes.Add(outerDtoTypeName);

        // Find all nested SelectExpr calls with explicit DTO types
        CollectNestedSelectExprDtoTypes(invocation, requiredDtoTypes);

        // If there are no nested SelectExpr calls, no need to check
        if (requiredDtoTypes.Count == 1)
        {
            return;
        }

        // Check if all required DTO types exist in the current file
        var root = invocation.SyntaxTree.GetRoot();
        var existingTypes = GetExistingTypeNames(root);

        // Find missing DTO types
        var missingTypes = requiredDtoTypes.Where(t => !existingTypes.Contains(t)).ToList();

        if (missingTypes.Count > 0)
        {
            // Report diagnostic for the outer SelectExpr invocation
            var diagnostic = Diagnostic.Create(
                RuleInstance,
                invocation.GetLocation(),
                string.Join(", ", missingTypes)
            );
            context.ReportDiagnostic(diagnostic);
        }
    }

    /// <summary>
    /// Collects all nested SelectExpr DTO type names from an invocation expression
    /// </summary>
    private static void CollectNestedSelectExprDtoTypes(
        InvocationExpressionSyntax invocation,
        HashSet<string> dtoTypes
    )
    {
        // Find all nested SelectExpr invocations
        var nestedSelectExprs = invocation
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Where(inv =>
                inv.Expression is MemberAccessExpressionSyntax ma
                && ma.Name.Identifier.Text == SelectExprHelper.MethodName
                && ma.Name is GenericNameSyntax gn
                && gn.TypeArgumentList.Arguments.Count >= 2
            );

        foreach (var nested in nestedSelectExprs)
        {
            if (
                nested.Expression is MemberAccessExpressionSyntax nestedMemberAccess
                && nestedMemberAccess.Name is GenericNameSyntax nestedGenericName
            )
            {
                // Get the second type argument (TDto)
                var dtoTypeSyntax = nestedGenericName.TypeArgumentList.Arguments[1];
                var dtoTypeName = dtoTypeSyntax.ToString();
                dtoTypes.Add(dtoTypeName);
            }
        }
    }

    /// <summary>
    /// Gets all existing type names in the syntax tree (class, struct, interface, record declarations)
    /// </summary>
    private static HashSet<string> GetExistingTypeNames(SyntaxNode root)
    {
        var typeNames = new HashSet<string>();

        // Collect class declarations
        var classDecls = root.DescendantNodes().OfType<ClassDeclarationSyntax>();
        foreach (var classDecl in classDecls)
        {
            typeNames.Add(classDecl.Identifier.Text);
        }

        // Collect struct declarations
        var structDecls = root.DescendantNodes().OfType<StructDeclarationSyntax>();
        foreach (var structDecl in structDecls)
        {
            typeNames.Add(structDecl.Identifier.Text);
        }

        // Collect record declarations
        var recordDecls = root.DescendantNodes().OfType<RecordDeclarationSyntax>();
        foreach (var recordDecl in recordDecls)
        {
            typeNames.Add(recordDecl.Identifier.Text);
        }

        return typeNames;
    }
}
