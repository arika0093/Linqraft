using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Linqraft.Playground.Services;

/// <summary>
/// Service for extracting semantic highlighting tokens from C# code using Roslyn.
/// Uses SharedCompilationService for consistent compilation across services.
/// </summary>
public class SemanticHighlightingService
{
    private readonly SharedCompilationService _sharedCompilation;

    public SemanticHighlightingService(SharedCompilationService sharedCompilation)
    {
        _sharedCompilation = sharedCompilation;
    }

    /// <summary>
    /// Analyzes C# code using the shared compilation and returns semantic tokens.
    /// This provides accurate highlighting for all code including generated code.
    /// </summary>
    public List<SemanticToken> GetSemanticTokens(string code, string? filePath = null)
    {
        var tokens = new List<SemanticToken>();

        try
        {
            // Find the matching syntax tree in the shared compilation
            SyntaxTree? syntaxTree = null;
            SemanticModel? semanticModel = null;

            if (_sharedCompilation.Compilation != null)
            {
                // Try to find matching tree by content or path
                foreach (var tree in _sharedCompilation.GetAllSyntaxTrees())
                {
                    var treeText = tree.GetText().ToString();
                    if (filePath != null && tree.FilePath == filePath)
                    {
                        syntaxTree = tree;
                        semanticModel = _sharedCompilation.GetSemanticModel(tree);
                        break;
                    }
                }
            }

            // If no matching tree found, parse independently (fallback)
            if (syntaxTree == null)
            {
                syntaxTree = CSharpSyntaxTree.ParseText(code);
                // For fallback, we can't get semantic model without compilation
                // Process only syntactic tokens
            }

            var root = syntaxTree.GetRoot();

            // Process all nodes
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

    /// <summary>
    /// Analyzes generated code using the shared compilation.
    /// Call this after code generation to get accurate highlighting for generated code.
    /// </summary>
    public List<SemanticToken> GetSemanticTokensForGenerated(
        string generatedCode,
        bool isExpression
    )
    {
        var tokens = new List<SemanticToken>();

        try
        {
            if (_sharedCompilation.Compilation == null || string.IsNullOrWhiteSpace(generatedCode))
            {
                return tokens;
            }

            // Find the generated code tree in the compilation
            var fileName = isExpression ? "__GeneratedExpression.cs" : "__GeneratedDto.cs";
            SyntaxTree? syntaxTree = null;

            foreach (var tree in _sharedCompilation.GetAllSyntaxTrees())
            {
                if (tree.FilePath == fileName)
                {
                    syntaxTree = tree;
                    break;
                }
            }

            if (syntaxTree != null)
            {
                var semanticModel = _sharedCompilation.GetSemanticModel(syntaxTree);
                var root = syntaxTree.GetRoot();

                foreach (var node in root.DescendantNodesAndSelf())
                {
                    ProcessNode(node, semanticModel, generatedCode, tokens);
                }
            }
            else
            {
                // Fallback: parse and analyze independently
                return GetSemanticTokensFallback(generatedCode);
            }
        }
        catch
        {
            // Ignore parsing errors
        }

        return tokens;
    }

    /// <summary>
    /// Fallback method for when shared compilation is not available.
    /// </summary>
    private List<SemanticToken> GetSemanticTokensFallback(string code)
    {
        var tokens = new List<SemanticToken>();

        try
        {
            var syntaxTree = CSharpSyntaxTree.ParseText(code);
            var root = syntaxTree.GetRoot();

            // Process only syntactic tokens (no semantic analysis)
            foreach (var node in root.DescendantNodesAndSelf())
            {
                ProcessNodeSyntaxOnly(node, code, tokens);
            }
        }
        catch
        {
            // Ignore parsing errors
        }

        return tokens;
    }

    private void ProcessNodeSyntaxOnly(SyntaxNode node, string code, List<SemanticToken> tokens)
    {
        switch (node)
        {
            case ClassDeclarationSyntax classDecl:
                AddToken(tokens, classDecl.Identifier, SemanticTokenType.Class, code);
                break;
            case RecordDeclarationSyntax recordDecl:
                AddToken(tokens, recordDecl.Identifier, SemanticTokenType.Class, code);
                break;
            case InterfaceDeclarationSyntax interfaceDecl:
                AddToken(tokens, interfaceDecl.Identifier, SemanticTokenType.Interface, code);
                break;
            case StructDeclarationSyntax structDecl:
                AddToken(tokens, structDecl.Identifier, SemanticTokenType.Struct, code);
                break;
            case MethodDeclarationSyntax methodDecl:
                AddToken(tokens, methodDecl.Identifier, SemanticTokenType.Method, code);
                break;
            case PropertyDeclarationSyntax propDecl:
                AddToken(tokens, propDecl.Identifier, SemanticTokenType.Property, code);
                ProcessPropertyKeywords(propDecl, code, tokens);
                break;
        }
    }

    private void ProcessNode(
        SyntaxNode node,
        SemanticModel? semanticModel,
        string code,
        List<SemanticToken> tokens
    )
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
            case TypeArgumentListSyntax typeArgList when semanticModel != null:
                ProcessTypeArgumentList(typeArgList, semanticModel, code, tokens);
                break;

            // Generic names (must come before TypeSyntax/IdentifierNameSyntax)
            case GenericNameSyntax genericName when semanticModel != null:
                ProcessGenericName(genericName, semanticModel, code, tokens);
                break;

            // Identifier names that refer to types or methods
            case IdentifierNameSyntax identifierName when semanticModel != null:
                ProcessIdentifierName(identifierName, semanticModel, code, tokens);
                break;

            // Invocation expressions (method calls)
            case InvocationExpressionSyntax invocation when semanticModel != null:
                ProcessInvocation(invocation, semanticModel, code, tokens);
                break;

            // Member access expressions
            case MemberAccessExpressionSyntax memberAccess when semanticModel != null:
                ProcessMemberAccess(memberAccess, semanticModel, code, tokens);
                break;
        }
    }

    private void ProcessPropertyKeywords(
        PropertyDeclarationSyntax propDecl,
        string code,
        List<SemanticToken> tokens
    )
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

    private void ProcessAccessorKeywords(
        AccessorDeclarationSyntax accessorDecl,
        string code,
        List<SemanticToken> tokens
    )
    {
        // Highlight 'init' keyword (SyntaxKind.InitKeyword in accessor)
        var keyword = accessorDecl.Keyword;
        if (keyword.IsKind(SyntaxKind.InitKeyword))
        {
            AddToken(tokens, keyword, SemanticTokenType.Keyword, code);
        }
    }

    private void ProcessIdentifierName(
        IdentifierNameSyntax identifierName,
        SemanticModel semanticModel,
        string code,
        List<SemanticToken> tokens
    )
    {
        // Skip if part of a declaration (already handled)
        if (
            identifierName.Parent
            is MethodDeclarationSyntax
                or ClassDeclarationSyntax
                or PropertyDeclarationSyntax
                or VariableDeclaratorSyntax
                or ParameterSyntax
        )
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

    private void ProcessTypeArgumentList(
        TypeArgumentListSyntax typeArgList,
        SemanticModel semanticModel,
        string code,
        List<SemanticToken> tokens
    )
    {
        // Process each type argument in the generic type (e.g., Order in List<Order>)
        foreach (var typeArg in typeArgList.Arguments)
        {
            ProcessTypeArgument(typeArg, semanticModel, code, tokens);
        }
    }

    private void ProcessTypeArgument(
        TypeSyntax typeSyntax,
        SemanticModel semanticModel,
        string code,
        List<SemanticToken> tokens
    )
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

    private void ProcessGenericName(
        GenericNameSyntax genericName,
        SemanticModel semanticModel,
        string code,
        List<SemanticToken> tokens
    )
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

    private void ProcessInvocation(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        string code,
        List<SemanticToken> tokens
    )
    {
        // Get the method name from the invocation
        SyntaxToken? methodIdentifier = invocation.Expression switch
        {
            IdentifierNameSyntax identName => identName.Identifier,
            MemberAccessExpressionSyntax memberAccess
                when memberAccess.Name is IdentifierNameSyntax name => name.Identifier,
            MemberAccessExpressionSyntax memberAccess
                when memberAccess.Name is GenericNameSyntax genericName => genericName.Identifier,
            GenericNameSyntax generic => generic.Identifier,
            _ => null,
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

    private void ProcessMemberAccess(
        MemberAccessExpressionSyntax memberAccess,
        SemanticModel semanticModel,
        string code,
        List<SemanticToken> tokens
    )
    {
        // Get the member name
        var name = memberAccess.Name;
        SyntaxToken identifier = name switch
        {
            IdentifierNameSyntax identName => identName.Identifier,
            GenericNameSyntax genericName => genericName.Identifier,
            _ => default,
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
            _ => SemanticTokenType.Class,
        };
    }

    private void AddToken(
        List<SemanticToken> tokens,
        SyntaxToken identifier,
        SemanticTokenType type,
        string code
    )
    {
        if (identifier == default)
            return;

        var span = identifier.Span;
        var lineSpan = identifier.SyntaxTree?.GetLineSpan(span);

        if (lineSpan == null)
            return;

        tokens.Add(
            new SemanticToken
            {
                StartLine = lineSpan.Value.StartLinePosition.Line + 1, // Monaco is 1-based
                StartColumn = lineSpan.Value.StartLinePosition.Character + 1,
                EndLine = lineSpan.Value.EndLinePosition.Line + 1,
                EndColumn = lineSpan.Value.EndLinePosition.Character + 1,
                Type = type,
            }
        );
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
    Keyword,
}
