using Basic.Reference.Assemblies;
using Linqraft.Core;
using Linqraft.Playground.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Linqraft.Playground.Services;

/// <summary>
/// Shared compilation service that provides a consistent CSharpCompilation
/// for both code generation and semantic highlighting.
/// This ensures that both services operate based on the same codebase.
/// </summary>
public class SharedCompilationService
{
    private static readonly Lazy<MetadataReference[]> LazyReferences = new(() =>
        Net90.References.All.ToArray()
    );

    private CSharpCompilation? _compilation;
    private List<SyntaxTree> _syntaxTrees = [];
    private readonly Dictionary<SyntaxTree, SemanticModel> _semanticModelCache = [];

    /// <summary>
    /// Creates or updates the shared compilation with the provided source files.
    /// </summary>
    public CSharpCompilation CreateCompilation(IEnumerable<ProjectFile> files)
    {
        _semanticModelCache.Clear();

        // Parse each file into its own syntax tree
        _syntaxTrees = files
            .Select(f => CSharpSyntaxTree.ParseText(f.Content, path: f.Path))
            .ToList();

        // Add SelectExpr extensions source
        var selectExprTree = CSharpSyntaxTree.ParseText(
            GenerateSourceCodeSnippets.SelectExprExtensions,
            path: "__SelectExprExtensions.cs"
        );
        _syntaxTrees.Add(selectExprTree);

        // Create compilation with all syntax trees and reference assemblies
        var references = LazyReferences.Value;
        _compilation = CSharpCompilation.Create(
            "PlaygroundAnalysis",
            _syntaxTrees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );

        return _compilation;
    }

    /// <summary>
    /// Adds generated code to the compilation for accurate highlighting.
    /// </summary>
    public CSharpCompilation AddGeneratedCode(string expressionCode, string dtoCode)
    {
        if (_compilation == null)
        {
            throw new InvalidOperationException("CreateCompilation must be called first.");
        }

        _semanticModelCache.Clear();

        var additionalTrees = new List<SyntaxTree>();

        if (!string.IsNullOrWhiteSpace(expressionCode) && !expressionCode.StartsWith("//"))
        {
            var expressionTree = CSharpSyntaxTree.ParseText(
                expressionCode,
                path: "__GeneratedExpression.cs"
            );
            additionalTrees.Add(expressionTree);
        }

        if (!string.IsNullOrWhiteSpace(dtoCode) && !dtoCode.StartsWith("//"))
        {
            var dtoTree = CSharpSyntaxTree.ParseText(dtoCode, path: "__GeneratedDto.cs");
            additionalTrees.Add(dtoTree);
        }

        if (additionalTrees.Count > 0)
        {
            _compilation = _compilation.AddSyntaxTrees(additionalTrees);
            _syntaxTrees.AddRange(additionalTrees);
        }

        return _compilation;
    }

    /// <summary>
    /// Gets a cached semantic model for the given syntax tree.
    /// </summary>
    public SemanticModel GetSemanticModel(SyntaxTree tree)
    {
        if (_compilation == null)
        {
            throw new InvalidOperationException("CreateCompilation must be called first.");
        }

        if (!_semanticModelCache.TryGetValue(tree, out var model))
        {
            model = _compilation.GetSemanticModel(tree);
            _semanticModelCache[tree] = model;
        }
        return model;
    }

    /// <summary>
    /// Gets all syntax trees in the compilation (excluding internal files).
    /// </summary>
    public IEnumerable<SyntaxTree> GetUserSyntaxTrees()
    {
        return _syntaxTrees.Where(t => !t.FilePath.StartsWith("__"));
    }

    /// <summary>
    /// Gets all syntax trees in the compilation including generated code.
    /// </summary>
    public IEnumerable<SyntaxTree> GetAllSyntaxTrees()
    {
        return _syntaxTrees;
    }

    /// <summary>
    /// Gets the current compilation.
    /// </summary>
    public CSharpCompilation? Compilation => _compilation;
}
