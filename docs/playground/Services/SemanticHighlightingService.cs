using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Basic.Reference.Assemblies;

namespace Linqraft.Playground.Services;

/// <summary>
/// Service for extracting semantic highlighting tokens from C# code using Roslyn.
/// Identifies class names, method names, property names, and other semantic constructs.
/// </summary>
public class SemanticHighlightingService
{
    private static readonly Lazy<MetadataReference[]> LazyReferences = new(() =>
        Net90.References.All.ToArray()
    );

    /// <summary>
    /// Analyzes C# code and returns semantic tokens for syntax highlighting.
    /// </summary>
    public List<SemanticToken> GetSemanticTokens(string code)
    {
        var tokens = new List<SemanticToken>();

        try
        {
            var syntaxTree = CSharpSyntaxTree.ParseText(code);
            var references = LazyReferences.Value;
            var compilation = CSharpCompilation.Create(
                "SemanticAnalysis",
                [syntaxTree],
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
            );

            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var root = syntaxTree.GetRoot();

            // Process all identifier tokens
            foreach (var node in root.DescendantNodesAndSelf())
            {
                ProcessNode(node, semanticModel, code, tokens);
            }
        }
        catch
        {
            // Ignore parsing errors
        }

        return tokens;
    }

    private void ProcessNode(SyntaxNode node, SemanticModel semanticModel, string code, List<SemanticToken> tokens)
    {
        switch (node)
        {
            // Class declarations
            case ClassDeclarationSyntax classDecl:
                AddToken(tokens, classDecl.Identifier, SemanticTokenType.Class, code);
                break;

            // Record declarations
            case RecordDeclarationSyntax recordDecl:
                AddToken(tokens, recordDecl.Identifier, SemanticTokenType.Class, code);
                break;

            // Interface declarations
            case InterfaceDeclarationSyntax interfaceDecl:
                AddToken(tokens, interfaceDecl.Identifier, SemanticTokenType.Interface, code);
                break;

            // Struct declarations
            case StructDeclarationSyntax structDecl:
                AddToken(tokens, structDecl.Identifier, SemanticTokenType.Struct, code);
                break;

            // Enum declarations
            case EnumDeclarationSyntax enumDecl:
                AddToken(tokens, enumDecl.Identifier, SemanticTokenType.Enum, code);
                break;

            // Method declarations
            case MethodDeclarationSyntax methodDecl:
                AddToken(tokens, methodDecl.Identifier, SemanticTokenType.Method, code);
                break;

            // Property declarations (including required modifier)
            case PropertyDeclarationSyntax propDecl:
                AddToken(tokens, propDecl.Identifier, SemanticTokenType.Property, code);
                ProcessPropertyKeywords(propDecl, code, tokens);
                break;

            // Accessor declarations (get, set, init)
            case AccessorDeclarationSyntax accessorDecl:
                ProcessAccessorKeywords(accessorDecl, code, tokens);
                break;

            // Type argument list (for generics like List<Order>)
            case TypeArgumentListSyntax typeArgList:
                ProcessTypeArgumentList(typeArgList, semanticModel, code, tokens);
                break;

            // Generic names (must come before TypeSyntax/IdentifierNameSyntax)
            case GenericNameSyntax genericName:
                ProcessGenericName(genericName, semanticModel, code, tokens);
                break;

            // Identifier names that refer to types or methods
            case IdentifierNameSyntax identifierName:
                ProcessIdentifierName(identifierName, semanticModel, code, tokens);
                break;

            // Invocation expressions (method calls)
            case InvocationExpressionSyntax invocation:
                ProcessInvocation(invocation, semanticModel, code, tokens);
                break;

            // Member access expressions
            case MemberAccessExpressionSyntax memberAccess:
                ProcessMemberAccess(memberAccess, semanticModel, code, tokens);
                break;
        }
    }

    private void ProcessPropertyKeywords(PropertyDeclarationSyntax propDecl, string code, List<SemanticToken> tokens)
    {
        // Check for 'required' modifier
        foreach (var modifier in propDecl.Modifiers)
        {
            if (modifier.IsKind(SyntaxKind.RequiredKeyword))
            {
                AddToken(tokens, modifier, SemanticTokenType.Keyword, code);
            }
        }
    }

    private void ProcessAccessorKeywords(AccessorDeclarationSyntax accessorDecl, string code, List<SemanticToken> tokens)
    {
        // Highlight 'init' keyword (SyntaxKind.InitKeyword in accessor)
        var keyword = accessorDecl.Keyword;
        if (keyword.IsKind(SyntaxKind.InitKeyword))
        {
            AddToken(tokens, keyword, SemanticTokenType.Keyword, code);
        }
    }

    private void ProcessIdentifierName(IdentifierNameSyntax identifierName, SemanticModel semanticModel, string code, List<SemanticToken> tokens)
    {
        // Skip if part of a declaration (already handled)
        if (identifierName.Parent is MethodDeclarationSyntax or 
            ClassDeclarationSyntax or 
            PropertyDeclarationSyntax or 
            VariableDeclaratorSyntax or
            ParameterSyntax)
        {
            return;
        }

        var symbolInfo = semanticModel.GetSymbolInfo(identifierName);
        var symbol = symbolInfo.Symbol;

        if (symbol is INamedTypeSymbol namedType)
        {
            var tokenType = GetTypeTokenType(namedType);
            AddToken(tokens, identifierName.Identifier, tokenType, code);
        }
        else if (symbol is IMethodSymbol)
        {
            AddToken(tokens, identifierName.Identifier, SemanticTokenType.Method, code);
        }
        else if (symbol is IPropertySymbol)
        {
            AddToken(tokens, identifierName.Identifier, SemanticTokenType.Property, code);
        }
        else if (symbol is IFieldSymbol)
        {
            AddToken(tokens, identifierName.Identifier, SemanticTokenType.Field, code);
        }
        else if (symbol is ILocalSymbol)
        {
            AddToken(tokens, identifierName.Identifier, SemanticTokenType.Variable, code);
        }
        else if (symbol is IParameterSymbol)
        {
            AddToken(tokens, identifierName.Identifier, SemanticTokenType.Parameter, code);
        }
        else if (symbol is INamespaceSymbol)
        {
            AddToken(tokens, identifierName.Identifier, SemanticTokenType.Namespace, code);
        }
    }

    private void ProcessTypeArgumentList(TypeArgumentListSyntax typeArgList, SemanticModel semanticModel, string code, List<SemanticToken> tokens)
    {
        // Process each type argument in the generic type (e.g., Order in List<Order>)
        foreach (var typeArg in typeArgList.Arguments)
        {
            ProcessTypeArgument(typeArg, semanticModel, code, tokens);
        }
    }

    private void ProcessTypeArgument(TypeSyntax typeSyntax, SemanticModel semanticModel, string code, List<SemanticToken> tokens)
    {
        switch (typeSyntax)
        {
            case IdentifierNameSyntax identifierName:
                var symbolInfo = semanticModel.GetSymbolInfo(identifierName);
                if (symbolInfo.Symbol is INamedTypeSymbol namedType)
                {
                    var tokenType = GetTypeTokenType(namedType);
                    AddToken(tokens, identifierName.Identifier, tokenType, code);
                }
                break;
            case GenericNameSyntax genericName:
                // Handle nested generics (e.g., List<Dictionary<string, Order>>)
                ProcessGenericName(genericName, semanticModel, code, tokens);
                break;
            case NullableTypeSyntax nullableType:
                // Handle nullable types (e.g., Order?)
                ProcessTypeArgument(nullableType.ElementType, semanticModel, code, tokens);
                break;
            case ArrayTypeSyntax arrayType:
                // Handle array types (e.g., Order[])
                ProcessTypeArgument(arrayType.ElementType, semanticModel, code, tokens);
                break;
            case QualifiedNameSyntax qualifiedName:
                // Handle qualified names (e.g., System.Collections.Generic.List<T>)
                ProcessTypeArgument(qualifiedName.Right, semanticModel, code, tokens);
                break;
        }
    }

    private void ProcessGenericName(GenericNameSyntax genericName, SemanticModel semanticModel, string code, List<SemanticToken> tokens)
    {
        var symbolInfo = semanticModel.GetSymbolInfo(genericName);
        var symbol = symbolInfo.Symbol;

        if (symbol is INamedTypeSymbol namedType)
        {
            var tokenType = GetTypeTokenType(namedType);
            AddToken(tokens, genericName.Identifier, tokenType, code);
        }
        else if (symbol is IMethodSymbol)
        {
            AddToken(tokens, genericName.Identifier, SemanticTokenType.Method, code);
        }
    }

    private void ProcessInvocation(InvocationExpressionSyntax invocation, SemanticModel semanticModel, string code, List<SemanticToken> tokens)
    {
        // Get the method name from the invocation
        SyntaxToken? methodIdentifier = invocation.Expression switch
        {
            IdentifierNameSyntax identName => identName.Identifier,
            MemberAccessExpressionSyntax memberAccess when memberAccess.Name is IdentifierNameSyntax name => name.Identifier,
            MemberAccessExpressionSyntax memberAccess when memberAccess.Name is GenericNameSyntax genericName => genericName.Identifier,
            GenericNameSyntax generic => generic.Identifier,
            _ => null
        };

        if (methodIdentifier.HasValue)
        {
            var symbolInfo = semanticModel.GetSymbolInfo(invocation);
            if (symbolInfo.Symbol is IMethodSymbol)
            {
                AddToken(tokens, methodIdentifier.Value, SemanticTokenType.Method, code);
            }
        }
    }

    private void ProcessMemberAccess(MemberAccessExpressionSyntax memberAccess, SemanticModel semanticModel, string code, List<SemanticToken> tokens)
    {
        // Get the member name
        var name = memberAccess.Name;
        SyntaxToken identifier = name switch
        {
            IdentifierNameSyntax identName => identName.Identifier,
            GenericNameSyntax genericName => genericName.Identifier,
            _ => default
        };

        if (identifier == default)
            return;

        var symbolInfo = semanticModel.GetSymbolInfo(memberAccess);
        var symbol = symbolInfo.Symbol;

        if (symbol is IPropertySymbol)
        {
            AddToken(tokens, identifier, SemanticTokenType.Property, code);
        }
        else if (symbol is IFieldSymbol)
        {
            AddToken(tokens, identifier, SemanticTokenType.Field, code);
        }
        else if (symbol is IMethodSymbol)
        {
            AddToken(tokens, identifier, SemanticTokenType.Method, code);
        }
        else if (symbol is INamedTypeSymbol namedType)
        {
            var tokenType = GetTypeTokenType(namedType);
            AddToken(tokens, identifier, tokenType, code);
        }
    }

    private SemanticTokenType GetTypeTokenType(INamedTypeSymbol namedType)
    {
        return namedType.TypeKind switch
        {
            TypeKind.Interface => SemanticTokenType.Interface,
            TypeKind.Struct => SemanticTokenType.Struct,
            TypeKind.Enum => SemanticTokenType.Enum,
            TypeKind.Delegate => SemanticTokenType.Delegate,
            _ => SemanticTokenType.Class
        };
    }

    private void AddToken(List<SemanticToken> tokens, SyntaxToken identifier, SemanticTokenType type, string code)
    {
        if (identifier == default)
            return;

        var span = identifier.Span;
        var lineSpan = identifier.SyntaxTree?.GetLineSpan(span);
        
        if (lineSpan == null)
            return;

        tokens.Add(new SemanticToken
        {
            StartLine = lineSpan.Value.StartLinePosition.Line + 1, // Monaco is 1-based
            StartColumn = lineSpan.Value.StartLinePosition.Character + 1,
            EndLine = lineSpan.Value.EndLinePosition.Line + 1,
            EndColumn = lineSpan.Value.EndLinePosition.Character + 1,
            Type = type
        });
    }
}

/// <summary>
/// Represents a semantic token with position and type information.
/// </summary>
public class SemanticToken
{
    public int StartLine { get; set; }
    public int StartColumn { get; set; }
    public int EndLine { get; set; }
    public int EndColumn { get; set; }
    public SemanticTokenType Type { get; set; }
}

/// <summary>
/// Types of semantic tokens for syntax highlighting.
/// </summary>
public enum SemanticTokenType
{
    Class,
    Interface,
    Struct,
    Enum,
    Delegate,
    Method,
    Property,
    Field,
    Variable,
    Parameter,
    Namespace,
    Keyword
}
