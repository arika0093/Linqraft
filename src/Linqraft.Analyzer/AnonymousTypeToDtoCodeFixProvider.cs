using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Linqraft.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Linqraft.Analyzer;

/// <summary>
/// Code fix provider that converts anonymous types to DTO classes
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(AnonymousTypeToDtoCodeFixProvider))]
[Shared]
public class AnonymousTypeToDtoCodeFixProvider : CodeFixProvider
{
    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(AnonymousTypeToDtoAnalyzer.DiagnosticId);

    public sealed override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root == null) return;

        var diagnostic = context.Diagnostics.First();
        var diagnosticSpan = diagnostic.Location.SourceSpan;

        var anonymousObject = root.FindToken(diagnosticSpan.Start)
            .Parent
            ?.AncestorsAndSelf()
            .OfType<AnonymousObjectCreationExpressionSyntax>()
            .First();

        if (anonymousObject == null) return;

        // Option 1: Add DTO to end of current file (appears first/on top)
        context.RegisterCodeFix(
            CodeAction.Create(
                title: "Convert to DTO class (add to current file)",
                createChangedDocument: c => ConvertToDtoInSameFileAsync(context.Document, anonymousObject, c),
                equivalenceKey: "ConvertToDtoSameFile"
            ),
            diagnostic
        );

        // Option 2: Create new file for DTO
        context.RegisterCodeFix(
            CodeAction.Create(
                title: "Convert to DTO class (new file)",
                createChangedSolution: c => ConvertToDtoNewFileAsync(context.Document, anonymousObject, c),
                equivalenceKey: "ConvertToDtoNewFile"
            ),
            diagnostic
        );
    }

    private async Task<Solution> ConvertToDtoAsync(
        Document document,
        AnonymousObjectCreationExpressionSyntax anonymousObject,
        CancellationToken cancellationToken
    )
    {
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (semanticModel == null) return document.Project.Solution;

        // Get type info for the anonymous object
        var typeInfo = semanticModel.GetTypeInfo(anonymousObject, cancellationToken);
        var anonymousType = typeInfo.Type;
        if (anonymousType == null) return document.Project.Solution;

        // Analyze the anonymous type structure
        var dtoStructure = DtoStructure.AnalyzeAnonymousType(
            anonymousObject,
            semanticModel,
            anonymousType
        );
        if (dtoStructure == null) return document.Project.Solution;

        // Generate DTO class name
        var dtoClassName = GenerateDtoClassName(anonymousObject);

        // Get the namespace from the document
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var namespaceDecl = root?.DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>().FirstOrDefault();
        var namespaceName = namespaceDecl?.Name.ToString() ?? "Generated";

        // Create DTO class info
        var dtoClassInfo = new GenerateDtoClassInfo
        {
            Structure = dtoStructure,
            Accessibility = "public",
            ClassName = dtoClassName,
            Namespace = namespaceName,
            NestedClasses = ImmutableList<GenerateDtoClassInfo>.Empty
        };

        // Generate configuration
        var configuration = new LinqraftConfiguration();

        // Build DTO code
        var dtoCode = BuildDtoFile(dtoClassInfo, configuration);

        // Create new document for the DTO
        var project = document.Project;
        var dtoFileName = $"{dtoClassName}.cs";

        // Check if file already exists
        var existingDoc = project.Documents.FirstOrDefault(d => d.Name == dtoFileName);
        if (existingDoc != null)
        {
            // Update existing document
            project = project.RemoveDocument(existingDoc.Id);
        }

        var dtoDocument = project.AddDocument(dtoFileName, dtoCode);
        project = dtoDocument.Project;

        // Replace anonymous object with DTO instantiation
        var newRoot = await ReplaceAnonymousWithDto(
            document,
            anonymousObject,
            dtoClassName,
            namespaceName,
            cancellationToken
        ).ConfigureAwait(false);

        if (newRoot != null)
        {
            var updatedDocument = project.GetDocument(document.Id);
            if (updatedDocument != null)
            {
                project = updatedDocument.WithSyntaxRoot(newRoot).Project;
            }
        }

        return project.Solution;
    }

    private static string GenerateDtoClassName(AnonymousObjectCreationExpressionSyntax anonymousObject)
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
        var methodDecl = anonymousObject.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
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

    private static string ToPascalCase(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        return char.ToUpperInvariant(name[0]) + name.Substring(1);
    }

    private static string BuildDtoFile(GenerateDtoClassInfo dtoClassInfo, LinqraftConfiguration configuration)
    {
        var code = dtoClassInfo.BuildCode(configuration);

        return $@"namespace {dtoClassInfo.Namespace};

{code}";
    }

    private static async Task<SyntaxNode?> ReplaceAnonymousWithDto(
        Document document,
        AnonymousObjectCreationExpressionSyntax anonymousObject,
        string dtoClassName,
        string namespaceName,
        CancellationToken cancellationToken
    )
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null) return null;

        // Build the new object creation expression
        var initializers = new List<ExpressionSyntax>();
        foreach (var init in anonymousObject.Initializers)
        {
            string propertyName;
            ExpressionSyntax valueExpression;

            if (init.NameEquals != null)
            {
                // Explicit: Name = value
                propertyName = init.NameEquals.Name.Identifier.Text;
                valueExpression = init.Expression;
            }
            else
            {
                // Implicit: need to extract property name from expression
                propertyName = GetPropertyNameFromExpression(init.Expression);
                valueExpression = init.Expression;
            }

            // Create assignment expression: PropertyName = value
            var assignment = SyntaxFactory.AssignmentExpression(
                SyntaxKind.SimpleAssignmentExpression,
                SyntaxFactory.IdentifierName(propertyName),
                valueExpression
            );
            initializers.Add(assignment);
        }

        // Create object creation: new DtoClassName { ... }
        var newObjectCreation = SyntaxFactory.ObjectCreationExpression(
            SyntaxFactory.IdentifierName(dtoClassName)
        )
        .WithInitializer(
            SyntaxFactory.InitializerExpression(
                SyntaxKind.ObjectInitializerExpression,
                SyntaxFactory.SeparatedList(initializers)
            )
        );

        // Replace the anonymous object with the new object creation
        var newRoot = root.ReplaceNode(anonymousObject, newObjectCreation);
        return newRoot;
    }

    private static string GetPropertyNameFromExpression(ExpressionSyntax expression)
    {
        return expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.Text,
            IdentifierNameSyntax identifier => identifier.Identifier.Text,
            ConditionalAccessExpressionSyntax conditionalAccess when
                conditionalAccess.WhenNotNull is MemberBindingExpressionSyntax memberBinding
                    => memberBinding.Name.Identifier.Text,
            _ => "Property"
        };
    }
}
