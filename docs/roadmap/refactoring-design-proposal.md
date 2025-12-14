# Linqraft リファクタリング設計案（修正版）

## 概要

Issue #246、#252の内容とコードベース調査に基づいた、包括的なリファクタリング設計案です。主な目的は以下の通りです：

1. **コードの構造化と重複排除**（Issue #246）
2. **外部ライブラリからの利用を容易にする**（Issue #252）
3. **構文解析パイプラインの設計**（全体フロー設計の見直し）
4. **コードフォーマットの一元化**（最終フェーズでの一括処理）
5. **将来的な拡張に対応できる基盤整備**（Issue #228など）

---

## 現状の課題分析

### 1. Linqraft.Core の問題点

#### 重複コード（Critical）
- **NullCheckHelper vs NullConditionalHelper**
  - `IsNullLiteral()`, `IsNullOrNullCast()`, `RemoveNullableCast()` が完全に重複
  - `NullCheckHelper` の3メソッドは**使用箇所ゼロ**（デッドコード）
  - `NullConditionalHelper` のみが実際に使用されている
  - 場所:
    - `src/Linqraft.Core/AnalyzerHelpers/NullCheckHelper.cs:16-58`
    - `src/Linqraft.Core/SyntaxHelpers/NullConditionalHelper.cs:96-138`

- **IsPartOfMemberAccess の重複**
  - `SyntaxHelper.IsPartOfMemberAccess()` - public static
  - `CaptureHelper.IsPartOfMemberAccess()` - private static
  - 同一実装が2箇所に存在
  - 場所:
    - `src/Linqraft.Core/AnalyzerHelpers/SyntaxHelper.cs:35-63`
    - `src/Linqraft.Core/AnalyzerHelpers/CaptureHelper.cs:383-411`

#### 不明瞭な構造
- **AnalyzerHelpers vs SyntaxHelpers の境界が曖昧**
  - `AnalyzerHelpers/SyntaxHelper.cs` - 基本的な構文操作（2メソッドのみ）
  - `SyntaxHelpers/` フォルダ - 包括的な構文ヘルパー（7ファイル）
  - 命名規則の混乱

- **CaptureHelper が肥大化**
  - 467行、複数の責務を持つ
  - 抽出ロジック、シンボル解析、構文生成が混在
  - 場所: `src/Linqraft.Core/AnalyzerHelpers/CaptureHelper.cs`

### 2. SourceGenerator の問題点

#### 密結合な設計
- **SelectExprInfo が巨大**（2,176行）
  - DTO生成、クエリ変換、式生成がすべて含まれる
  - 単一責任の原則に違反
  - 場所: `src/Linqraft.Core/SelectExprInfo.cs`

- **アドホックな構文解析と変換**
  - 各所で独自に実装されている（例: SelectExpr箇所の特定 + 内部の匿名型生成Func）
  - パーツを組み合わせて汎用的に対応できる仕組みがない
  - クエリ変換が SelectExprInfo にハードコーディング
  - 拡張ポイントが存在しない

- **フォーマット処理が散在**
  - コード生成の各所でインデントや改行を個別に処理
  - 統一的なフォーマット戦略がない
  - `CodeFormatter` が存在するが基本的なユーティリティのみ
  - 場所: `src/Linqraft.Core/Formatting/CodeFormatter.cs`

### 3. 外部ライブラリ利用の制約（重要）

- **Source Generatorの特性**
  - Source Generatorは各プロジェクトのコンパイル時に独立して動作
  - 他のライブラリから直接Linqraftの生成ワークフローに乗っかることは**不可能**
  - 外部ライブラリが利用できるのは、構文解析ユーティリティなどの**補助機能のみ**

---

## リファクタリング設計案

### 全体アーキテクチャの見直し

#### 新しい処理フロー

```
[1. 発見フェーズ (Discovery)]
   ↓
[2. 構文解析フェーズ (Parsing)]
   ↓
[3. 意味解析フェーズ (Semantic Analysis)]
   ↓
[4. 変換フェーズ (Transformation)]
   ↓
[5. 生成フェーズ (Generation)] ← 構文木ベース (SyntaxNode)
   ↓
[6. セマンティック付加 (Semantic Enrichment)] ← 新規追加
   ↓
[7. フォーマットフェーズ (Formatting)] ← NormalizeWhitespace()
   ↓
[出力 (string)]
```

各フェーズは独立したコンポーネントで構成され、**パーツとして組み合わせ可能**にします。

#### アクセス修飾子設計の原則

**基本方針: Internal by Default**

外部利用を想定しないロジックは `internal` とし、必要最小限のAPIのみを `public` として公開します。

**アクセス修飾子のガイドライン:**

| 対象 | アクセス修飾子 | 理由 |
|------|---------------|------|
| パイプライン内部ロジック | `internal` | Source Generator 内でのみ使用 |
| ヘルパークラス（内部用） | `internal` | Source Generator/Analyzer 内でのみ使用 |
| 外部公開 API | `public` | 外部ライブラリから利用されるユーティリティ |
| テスト用アクセス | `InternalsVisibleTo` | Playground や Test プロジェクトからのアクセス |

**具体例:**

```csharp
// ❌ Bad: すべて public
public class SelectExprMatcher : IPatternMatcher
{
    public IEnumerable<SyntaxNode> FindMatches(SyntaxNode root) { }
}

// ✅ Good: 内部実装は internal
internal class SelectExprMatcher : IPatternMatcher
{
    public IEnumerable<SyntaxNode> FindMatches(SyntaxNode root) { }
}

// ✅ Good: 外部公開APIのみ public
public static class LinqraftSyntaxUtilities
{
    // 外部から利用されるユーティリティ
    public static DtoStructure InferDtoStructure(
        ExpressionSyntax expression,
        SemanticModel semanticModel
    ) { }

    // 内部実装の詳細は internal
    internal static PropertyInfo[] ExtractProperties(ExpressionSyntax expr) { }
}
```

**AssemblyInfo.cs の設定:**

```csharp
[assembly: InternalsVisibleTo("Linqraft.SourceGenerator")]
[assembly: InternalsVisibleTo("Linqraft.Analyzer")]
[assembly: InternalsVisibleTo("Linqraft.Tests")]
[assembly: InternalsVisibleTo("Linqraft.Analyzer.Tests")]
[assembly: InternalsVisibleTo("Linqraft.Playground")]
```

**適用ルール:**

1. **新規作成時のデフォルト**: すべてのクラス・メソッドは `internal` で作成
2. **public への昇格条件**:
   - 外部ライブラリから利用される場合のみ
   - Issue #252 対応の `LinqraftSyntaxUtilities` など
3. **既存コードの移行時**:
   - 現在 `public` でも外部利用がなければ `internal` に変更
   - Source Generator/Analyzer 内でのみ使用されるものは `internal` 化

---

### Phase 1: パイプラインアーキテクチャの構築（全体フロー再設計）

**方針**: まず新しい構造（パイプライン）を構築し、その過程で重複コードを自然に排除します。

#### 1.1 新しいディレクトリ構造の作成

**新しい構造:**

```
src/Linqraft.Core/
├── Pipeline/                    # 新規: パイプライン関連
│   ├── Discovery/              # 発見フェーズ
│   │   ├── IPatternMatcher.cs
│   │   ├── SelectExprMatcher.cs
│   │   └── MappingAttributeMatcher.cs
│   ├── Parsing/                # 構文解析フェーズ
│   │   ├── ISyntaxParser.cs
│   │   ├── LambdaParser.cs
│   │   ├── AnonymousTypeParser.cs
│   │   └── ObjectCreationParser.cs
│   ├── Analysis/               # 意味解析フェーズ
│   │   ├── ISemanticAnalyzer.cs
│   │   ├── TypeAnalyzer.cs
│   │   ├── CaptureAnalyzer.cs  # CaptureHelper から分離・移行
│   │   └── NullabilityAnalyzer.cs
│   ├── Transformation/         # 変換フェーズ
│   │   ├── IExpressionTransformer.cs
│   │   ├── NullConditionalTransformer.cs
│   │   ├── SelectTransformer.cs
│   │   └── StaticMemberTransformer.cs
│   ├── Generation/             # 生成フェーズ（構文木ベース）
│   │   ├── ISyntaxTreeGenerator.cs
│   │   ├── DtoClassSyntaxGenerator.cs
│   │   ├── InterceptorGenerator.cs
│   │   └── SemanticEnricher.cs
│   └── Formatting/             # フォーマットフェーズ
│       ├── ICodeFormatter.cs
│       ├── SyntaxTreeFormatter.cs
│       └── FormattingOptions.cs
├── Helpers/
│   ├── Syntax/                 # 構文操作（既存の SyntaxHelpers から移行）
│   │   ├── ExpressionHelper.cs
│   │   ├── LambdaHelper.cs
│   │   ├── LinqMethodHelper.cs
│   │   ├── ArgumentListHelper.cs
│   │   ├── ObjectCreationHelper.cs
│   │   ├── NullConditionalHelper.cs  # ← NullCheckHelper と統合
│   │   └── TriviaHelper.cs
│   ├── Semantic/               # セマンティック解析（既存の RoslynHelpers から移行）
│   │   ├── RoslynTypeHelper.cs
│   │   └── DocumentationCommentHelper.cs
│   └── CodeFix/                # コード修正固有（既存の AnalyzerHelpers から移行）
│       ├── SyntaxUtilities.cs  # SyntaxHelper をリネーム
│       ├── CodeFixUtilities.cs # SyntaxGenerationHelper をリネーム
│       └── UsingDirectiveHelper.cs
├── Analyzers/                  # Analyzer 基盤
│   └── BaseLinqraftAnalyzer.cs
└── [既存のファイル群]
```

#### 1.2 パイプライン基盤の実装

**基盤インターフェースの定義:**

```csharp
namespace Linqraft.Core.Pipeline;

// パイプラインコンテキスト（各フェーズ間でデータを受け渡し）（内部実装）
internal record PipelineContext
{
    public required SyntaxNode TargetNode { get; init; }
    public required SemanticModel SemanticModel { get; init; }
    public Dictionary<string, object> Metadata { get; init; } = new();
}

// パイプラインステージの基底インターフェース（内部実装）
internal interface IPipelineStage<TInput, TOutput>
{
    TOutput Process(TInput input);
}

// パイプライン全体の orchestrator（構文木ベース）（内部実装）
internal class SyntaxTreeGenerationPipeline
{
    private readonly IPatternMatcher _matcher;
    private readonly ISyntaxParser _parser;
    private readonly ISemanticAnalyzer _analyzer;
    private readonly IExpressionTransformer _transformer;
    private readonly ISyntaxTreeGenerator _generator;  // 変更: ICodeGenerator → ISyntaxTreeGenerator
    private readonly SemanticEnricher _enricher;       // 新規追加
    private readonly SyntaxTreeFormatter _formatter;   // 変更: ICodeFormatter → SyntaxTreeFormatter

    public SyntaxTreeGenerationPipeline(
        IPatternMatcher matcher,
        ISyntaxParser parser,
        ISemanticAnalyzer analyzer,
        IExpressionTransformer transformer,
        ISyntaxTreeGenerator generator,
        SemanticEnricher enricher,
        SyntaxTreeFormatter formatter)
    {
        _matcher = matcher;
        _parser = parser;
        _analyzer = analyzer;
        _transformer = transformer;
        _generator = generator;
        _enricher = enricher;
        _formatter = formatter;
    }

    public GenerationResult Execute(SyntaxNode root, SemanticModel semanticModel, Compilation compilation)
    {
        // 1. Discovery: 対象パターンの特定
        var matches = _matcher.FindMatches(root);

        var syntaxNodes = new List<SyntaxNode>();  // 変更: string → SyntaxNode
        foreach (var match in matches)
        {
            var context = new PipelineContext
            {
                TargetNode = match,
                SemanticModel = semanticModel
            };

            // 2. Parsing: 構文解析
            var parsed = _parser.Parse(context);

            // 3. Semantic Analysis: 意味解析
            var analyzed = _analyzer.Analyze(parsed);

            // 4. Transformation: 式変換
            var transformed = _transformer.Transform(analyzed);

            // 5. Syntax Tree Generation: 構文木生成（文字列ではなく）
            var generationContext = new GenerationContext
            {
                SemanticModel = semanticModel,
                Structure = ExtractStructure(transformed),
                Configuration = GetConfiguration()
            };
            var syntaxNode = _generator.Generate(generationContext);

            // 6. Semantic Enrichment: セマンティック情報の付加
            var enriched = _enricher.Enrich(syntaxNode);

            syntaxNodes.Add(enriched);
        }

        // 7. 複数の構文木を結合
        var compilationUnit = CombineSyntaxNodes(syntaxNodes);

        // 8. Formatting: 構文木を直接フォーマット（NormalizeWhitespace）
        var formatted = _formatter.FormatSyntaxNode(compilationUnit);

        return new GenerationResult { Code = formatted };
    }

    private SyntaxNode CombineSyntaxNodes(List<SyntaxNode> nodes)
    {
        // 複数のクラス宣言を名前空間内に配置
        var namespaceDeclaration = SyntaxFactory.NamespaceDeclaration(
            SyntaxFactory.ParseName("GeneratedNamespace")
        );

        foreach (var node in nodes)
        {
            if (node is MemberDeclarationSyntax member)
            {
                namespaceDeclaration = namespaceDeclaration.AddMembers(member);
            }
        }

        // CompilationUnit として返す
        return SyntaxFactory.CompilationUnit()
            .AddMembers(namespaceDeclaration);
    }
}
```

#### 1.3 既存コードの移行と重複排除

**移行の順序:**

1. **Helpers の移行**
   - `SyntaxHelpers/` → `Helpers/Syntax/`
   - `RoslynHelpers/` → `Helpers/Semantic/`
   - `AnalyzerHelpers/` の一部 → `Helpers/CodeFix/`
   - この過程で **NullCheckHelper を削除**し、NullConditionalHelper に統合

2. **CaptureHelper の分割**
   - 抽出ロジック → `Pipeline/Analysis/CaptureAnalyzer.cs`
   - 判定ロジック → `Pipeline/Analysis/CaptureDetector.cs`
   - コード生成 → `Pipeline/Generation/CaptureCodeGenerator.cs`
   - この過程で **IsPartOfMemberAccess の重複を解消**

3. **既存の生成ロジックをパイプラインに移行**
   - `SelectExprInfo` の変換ロジック → `Pipeline/Transformation/`
   - DTO生成ロジック → `Pipeline/Generation/`

**重複排除のタイミング:**
- ✅ 新しい構造に移行する際に自然に重複を解消
- ✅ 同じ責務のコードは同じフェーズにまとめる
- ✅ 使用されていないコード（NullCheckHelper）は削除

#### 1.4 各フェーズの詳細設計

##### 1.4.1 Discovery Phase（発見フェーズ）

```csharp
namespace Linqraft.Core.Pipeline.Discovery;

// パターンマッチャーの基底インターフェース（内部実装）
internal interface IPatternMatcher
{
    IEnumerable<SyntaxNode> FindMatches(SyntaxNode root);
    bool IsMatch(SyntaxNode node);
}

// 具体例: SelectExpr 呼び出しの検出（内部実装）
internal class SelectExprMatcher : IPatternMatcher
{
    public IEnumerable<SyntaxNode> FindMatches(SyntaxNode root)
    {
        return root.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Where(IsMatch);
    }

    public bool IsMatch(SyntaxNode node)
    {
        if (node is not InvocationExpressionSyntax invocation)
            return false;

        // SelectExpr メソッド呼び出しかどうかを判定
        return invocation.Expression.ToString().EndsWith("SelectExpr");
    }
}

// 組み合わせ可能なマッチャー（内部実装）
internal class CompositePatternMatcher : IPatternMatcher
{
    private readonly List<IPatternMatcher> _matchers;

    public CompositePatternMatcher(params IPatternMatcher[] matchers)
    {
        _matchers = matchers.ToList();
    }

    public IEnumerable<SyntaxNode> FindMatches(SyntaxNode root)
    {
        return _matchers.SelectMany(m => m.FindMatches(root)).Distinct();
    }

    public bool IsMatch(SyntaxNode node)
    {
        return _matchers.Any(m => m.IsMatch(node));
    }
}
```

##### 1.4.2 Parsing Phase（構文解析フェーズ）

```csharp
namespace Linqraft.Core.Pipeline.Parsing;

// 構文パーサーの基底インターフェース（内部実装）
internal interface ISyntaxParser
{
    ParsedSyntax Parse(PipelineContext context);
}

internal record ParsedSyntax
{
    public required SyntaxNode OriginalNode { get; init; }
    public string? LambdaParameterName { get; init; }
    public ExpressionSyntax? LambdaBody { get; init; }
    public ObjectCreationExpressionSyntax? ObjectCreation { get; init; }
    public Dictionary<string, object> ParsedData { get; init; } = new();
}

// 具体例: Lambda + 匿名型のパーサー（内部実装）
internal class LambdaAnonymousTypeParser : ISyntaxParser
{
    public ParsedSyntax Parse(PipelineContext context)
    {
        // LambdaHelper を活用
        var lambda = LambdaHelper.FindLambdaInArguments(context.TargetNode);
        var paramName = LambdaHelper.GetLambdaParameterName(lambda);
        var body = LambdaHelper.GetLambdaBody(lambda);

        // ExpressionHelper を活用
        var anonymousType = ExpressionHelper.FindAnonymousObjectCreation(body);

        return new ParsedSyntax
        {
            OriginalNode = context.TargetNode,
            LambdaParameterName = paramName,
            LambdaBody = body,
            ObjectCreation = anonymousType
        };
    }
}

// パーサーの組み合わせ（パイプライン）（内部実装）
internal class CompositeSyntaxParser : ISyntaxParser
{
    private readonly List<ISyntaxParser> _parsers;

    public CompositeSyntaxParser(params ISyntaxParser[] parsers)
    {
        _parsers = parsers.ToList();
    }

    public ParsedSyntax Parse(PipelineContext context)
    {
        var result = new ParsedSyntax { OriginalNode = context.TargetNode };

        foreach (var parser in _parsers)
        {
            var parsed = parser.Parse(context);
            result = MergeResults(result, parsed);
        }

        return result;
    }

    private ParsedSyntax MergeResults(ParsedSyntax a, ParsedSyntax b)
    {
        // 複数のパーサー結果をマージ
        return a with
        {
            LambdaParameterName = b.LambdaParameterName ?? a.LambdaParameterName,
            LambdaBody = b.LambdaBody ?? a.LambdaBody,
            ObjectCreation = b.ObjectCreation ?? a.ObjectCreation,
            ParsedData = a.ParsedData.Concat(b.ParsedData)
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
        };
    }
}
```

##### 1.4.3 Transformation Phase（変換フェーズ）

```csharp
namespace Linqraft.Core.Pipeline.Transformation;

// 式変換の共通インターフェース（内部実装）
internal interface IExpressionTransformer
{
    bool CanTransform(TransformContext context);
    ExpressionSyntax Transform(TransformContext context);
    int Priority { get; } // 優先順位
}

internal record TransformContext
{
    public required ExpressionSyntax Expression { get; init; }
    public required SemanticModel SemanticModel { get; init; }
    public required ITypeSymbol ExpectedType { get; init; }
    public Dictionary<string, object> Metadata { get; init; } = new();
}

// 変換パイプライン（Chain of Responsibility パターン）（内部実装）
internal class TransformationPipeline
{
    private readonly List<IExpressionTransformer> _transformers;

    public TransformationPipeline(IEnumerable<IExpressionTransformer> transformers)
    {
        _transformers = transformers.OrderByDescending(t => t.Priority).ToList();
    }

    public ExpressionSyntax Transform(TransformContext context)
    {
        var current = context.Expression;

        foreach (var transformer in _transformers)
        {
            if (transformer.CanTransform(context with { Expression = current }))
            {
                current = transformer.Transform(context with { Expression = current });
            }
        }

        return current;
    }

    // 再帰的な変換（ネストした式に対応）
    public ExpressionSyntax TransformRecursive(TransformContext context)
    {
        var transformed = Transform(context);

        // 子ノードにも再帰的に適用
        if (transformed is InvocationExpressionSyntax invocation)
        {
            // 引数内の式も変換
            // ...
        }

        return transformed;
    }
}

// 具体例: Null条件演算子の変換（内部実装）
internal class NullConditionalTransformer : IExpressionTransformer
{
    public int Priority => 100;

    public bool CanTransform(TransformContext context)
        => NullConditionalHelper.HasNullConditionalAccess(context.Expression);

    public ExpressionSyntax Transform(TransformContext context)
    {
        // NullConditionalHelper を活用した変換
        return ConvertToTernary(context.Expression, context.SemanticModel);
    }

    private ExpressionSyntax ConvertToTernary(ExpressionSyntax expr, SemanticModel model)
    {
        // x?.Property -> x != null ? x.Property : default
        // の変換ロジック
        // ...
    }
}
```

##### 1.4.4 Generation Phase（生成フェーズ - 構文木ベース）

**重要な設計変更：文字列ベース → 構文木ベース**

**問題点：文字列ベース生成の限界**

```csharp
// ❌ 現在の実装（文字列ベース）
var code = $"public class {className} {{ public {typeName} {propertyName} {{ get; set; }} }}";

// 問題:
// 1. typeName が "List<int>" の場合、完全修飾名に変換できない
// 2. セマンティック情報が失われる
// 3. フォーマット時にパース→文字列化するとSemanticModelが失われる
// 4. 型の曖昧性解決ができない
```

**解決策：Roslyn SyntaxFactory を使った構文木生成**

```csharp
namespace Linqraft.Core.Pipeline.Generation;

/// <summary>
/// 構文木ベースのコード生成インターフェース（内部実装）
/// </summary>
internal interface ISyntaxTreeGenerator
{
    SyntaxNode Generate(GenerationContext context);  // string ではなく SyntaxNode を返す
}

internal record GenerationContext
{
    public required SemanticModel SemanticModel { get; init; }
    public required DtoStructure Structure { get; init; }
    public required LinqraftConfiguration Configuration { get; init; }
    public Dictionary<string, object> Metadata { get; init; } = new();
}

/// <summary>
/// DTO クラスの構文木を生成（内部実装）
/// </summary>
internal class DtoClassSyntaxGenerator : ISyntaxTreeGenerator
{
    public SyntaxNode Generate(GenerationContext context)
    {
        var structure = context.Structure;
        var config = context.Configuration;

        // 1. クラス宣言を構文木として構築
        var classDeclaration = SyntaxFactory.ClassDeclaration(structure.DtoClassName)
            .AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword))
            .AddModifiers(SyntaxFactory.Token(SyntaxKind.PartialKeyword));

        // 2. プロパティを追加
        foreach (var prop in structure.Properties)
        {
            var propertyDeclaration = CreatePropertyDeclaration(
                prop,
                config,
                context.SemanticModel
            );
            classDeclaration = classDeclaration.AddMembers(propertyDeclaration);
        }

        return classDeclaration;
    }

    private PropertyDeclarationSyntax CreatePropertyDeclaration(
        DtoProperty property,
        LinqraftConfiguration config,
        SemanticModel semanticModel)
    {
        // ITypeSymbol からセマンティック情報を使って型を解決
        var typeSymbol = property.Type;
        var typeSyntax = CreateTypeSyntax(typeSymbol, semanticModel);

        var propertyDeclaration = SyntaxFactory.PropertyDeclaration(
            typeSyntax,
            SyntaxFactory.Identifier(property.Name)
        )
        .AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword))
        .AddAccessorListAccessors(
            CreateAccessors(config.GetEffectivePropertyAccessor())
        );

        // required キーワードの追加
        if (config.HasRequired && !property.IsNullable)
        {
            propertyDeclaration = propertyDeclaration.AddModifiers(
                SyntaxFactory.Token(SyntaxKind.RequiredKeyword)
            );
        }

        return propertyDeclaration;
    }

    /// <summary>
    /// ITypeSymbol から TypeSyntax を生成（完全修飾名を使用）
    /// </summary>
    private TypeSyntax CreateTypeSyntax(ITypeSymbol typeSymbol, SemanticModel semanticModel)
    {
        // 完全修飾名を取得
        var fullName = typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        // ジェネリック型の場合
        if (typeSymbol is INamedTypeSymbol namedType && namedType.IsGenericType)
        {
            return CreateGenericTypeSyntax(namedType, semanticModel);
        }

        // Nullable<T> の場合
        if (typeSymbol.NullableAnnotation == NullableAnnotation.Annotated)
        {
            var underlyingType = typeSymbol.WithNullableAnnotation(NullableAnnotation.NotAnnotated);
            var underlyingTypeSyntax = CreateTypeSyntax(underlyingType, semanticModel);

            return SyntaxFactory.NullableType(underlyingTypeSyntax);
        }

        // シンプルな型の場合
        return SyntaxFactory.ParseTypeName(fullName);
    }

    private TypeSyntax CreateGenericTypeSyntax(INamedTypeSymbol namedType, SemanticModel semanticModel)
    {
        // List<T>, Dictionary<TKey, TValue> などのジェネリック型を構築
        var genericName = namedType.ConstructedFrom.ToDisplayString(
            SymbolDisplayFormat.FullyQualifiedFormat
        );

        var typeArguments = namedType.TypeArguments
            .Select(arg => CreateTypeSyntax(arg, semanticModel))
            .ToArray();

        // 型名からジェネリックパラメータ部分を除去
        var baseTypeName = genericName.Split('<')[0];

        return SyntaxFactory.GenericName(
            SyntaxFactory.Identifier(baseTypeName.Split('.').Last())
        )
        .WithTypeArgumentList(
            SyntaxFactory.TypeArgumentList(
                SyntaxFactory.SeparatedList(typeArguments)
            )
        );
    }

    private AccessorDeclarationSyntax[] CreateAccessors(PropertyAccessor accessor)
    {
        return accessor switch
        {
            PropertyAccessor.GetAndSet => new[]
            {
                SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                    .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)),
                SyntaxFactory.AccessorDeclaration(SyntaxKind.SetAccessorDeclaration)
                    .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken))
            },
            PropertyAccessor.GetAndInit => new[]
            {
                SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                    .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)),
                SyntaxFactory.AccessorDeclaration(SyntaxKind.InitAccessorDeclaration)
                    .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken))
            },
            PropertyAccessor.GetAndInternalSet => new[]
            {
                SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                    .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)),
                SyntaxFactory.AccessorDeclaration(SyntaxKind.SetAccessorDeclaration)
                    .AddModifiers(SyntaxFactory.Token(SyntaxKind.InternalKeyword))
                    .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken))
            },
            _ => throw new NotSupportedException()
        };
    }
}
```

**セマンティック情報の付加（Semantic Enrichment）**

```csharp
namespace Linqraft.Core.Pipeline.Generation;

/// <summary>
/// 生成された構文木にセマンティック情報を付加（内部実装）
/// </summary>
internal class SemanticEnricher
{
    private readonly SemanticModel _semanticModel;
    private readonly Compilation _compilation;

    public SemanticEnricher(SemanticModel semanticModel, Compilation compilation)
    {
        _semanticModel = semanticModel;
        _compilation = compilation;
    }

    /// <summary>
    /// 構文木を走査し、型参照を完全修飾名に変換
    /// </summary>
    public SyntaxNode Enrich(SyntaxNode node)
    {
        var rewriter = new FullyQualifyingRewriter(_semanticModel, _compilation);
        return rewriter.Visit(node);
    }

    private class FullyQualifyingRewriter : CSharpSyntaxRewriter
    {
        private readonly SemanticModel _semanticModel;
        private readonly Compilation _compilation;

        public FullyQualifyingRewriter(SemanticModel semanticModel, Compilation compilation)
        {
            _semanticModel = semanticModel;
            _compilation = compilation;
        }

        public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
        {
            // 型参照の場合、完全修飾名に変換
            var symbolInfo = _semanticModel.GetSymbolInfo(node);
            if (symbolInfo.Symbol is ITypeSymbol typeSymbol)
            {
                var fullyQualifiedName = typeSymbol.ToDisplayString(
                    SymbolDisplayFormat.FullyQualifiedFormat
                );

                // 必要に応じて完全修飾名に変換
                if (ShouldFullyQualify(typeSymbol))
                {
                    return SyntaxFactory.ParseTypeName(fullyQualifiedName);
                }
            }

            return base.VisitIdentifierName(node);
        }

        private bool ShouldFullyQualify(ITypeSymbol typeSymbol)
        {
            // ビルトイン型（int, string など）は除外
            if (typeSymbol.SpecialType != SpecialType.None)
                return false;

            // System 名前空間の型は除外（オプション）
            if (typeSymbol.ContainingNamespace?.ToDisplayString().StartsWith("System") == true)
                return false;

            return true;
        }
    }
}
```

**生成例の比較**

```csharp
// ❌ Before: 文字列ベース
var code = $"public class {className} {{ public List<int> Ids {{ get; set; }} }}";
// 問題: "List" が曖昧（System.Collections.Generic.List? カスタムList?）

// ✅ After: 構文木ベース
var classDecl = SyntaxFactory.ClassDeclaration("UserDto")
    .AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword))
    .AddMembers(
        SyntaxFactory.PropertyDeclaration(
            CreateTypeSyntax(listOfIntTypeSymbol, semanticModel),  // ITypeSymbol から生成
            "Ids"
        )
        .AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword))
        // ...
    );

// 結果: public partial class UserDto
// {
//     public global::System.Collections.Generic.List<int> Ids { get; set; }
// }
// → 完全修飾名で曖昧性なし
```

**利点**
1. ✅ **セマンティック情報の保持**: 型の完全修飾名を正確に生成
2. ✅ **フォーマットの自動化**: NormalizeWhitespace() でRoslynが自動整形
3. ✅ **保守性の向上**: 文字列連結のバグを防止
4. ✅ **拡張性**: CSharpSyntaxRewriter でカスタム変換が容易

##### 1.4.5 Formatting Phase（フォーマットフェーズ - 構文木ベース対応）

```csharp
namespace Linqraft.Core.Pipeline.Formatting;

// フォーマッターの基底インターフェース（構文木ベース対応）（内部実装）
internal interface ICodeFormatter
{
    string Format(string code, FormattingOptions options = default);  // 後方互換性のため
    string FormatSyntaxNode(SyntaxNode node, FormattingOptions options = default);  // 新規
}

internal record FormattingOptions
{
    public int IndentSize { get; init; } = 4;
    public string NewLine { get; init; } = "\n";
    public bool NormalizeWhitespace { get; init; } = true;
    public bool RemoveTrailingWhitespace { get; init; } = true;
}

// 包括的なフォーマッター（最終フェーズで一括適用）（内部実装）
internal class ComprehensiveCodeFormatter : ICodeFormatter
{
    public string Format(string code, FormattingOptions options = default)
    {
        options ??= new FormattingOptions();

        // 1. 改行の正規化
        code = NormalizeNewLines(code, options.NewLine);

        // 2. インデントの調整
        code = NormalizeIndentation(code, options.IndentSize);

        // 3. 空白の正規化
        if (options.NormalizeWhitespace)
        {
            code = NormalizeWhitespace(code);
        }

        // 4. 末尾空白の削除
        if (options.RemoveTrailingWhitespace)
        {
            code = RemoveTrailingWhitespace(code);
        }

        // 5. 連続した空行の削除
        code = RemoveExcessiveBlankLines(code);

        return code;
    }

    private string NormalizeNewLines(string code, string newLine)
    {
        return code.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", newLine);
    }

    private string NormalizeIndentation(string code, int indentSize)
    {
        var lines = code.Split('\n');
        var normalized = new List<string>();

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                normalized.Add("");
                continue;
            }

            // タブをスペースに変換
            var withoutTabs = line.Replace("\t", new string(' ', indentSize));

            // 先頭の空白を数える
            var leadingSpaces = withoutTabs.TakeWhile(char.IsWhiteSpace).Count();

            // indentSize の倍数に調整
            var normalizedIndent = (leadingSpaces / indentSize) * indentSize;
            var content = withoutTabs.TrimStart();

            normalized.Add(new string(' ', normalizedIndent) + content);
        }

        return string.Join("\n", normalized);
    }

    private string NormalizeWhitespace(string code)
    {
        // 演算子周りの空白を正規化
        // 例: "a+b" -> "a + b"
        // ...
        return code;
    }

    private string RemoveTrailingWhitespace(string code)
    {
        var lines = code.Split('\n');
        return string.Join("\n", lines.Select(line => line.TrimEnd()));
    }

    private string RemoveExcessiveBlankLines(string code)
    {
        // 3行以上の連続した空行を2行に削減
        return System.Text.RegularExpressions.Regex.Replace(
            code,
            @"\n{3,}",
            "\n\n"
        );
    }
}

// 構文木ベースのフォーマッター（推奨）（内部実装）
internal class SyntaxTreeFormatter : ICodeFormatter
{
    public string Format(string code, FormattingOptions options = default)
    {
        // 後方互換性のため文字列も受け付ける
        var tree = CSharpSyntaxTree.ParseText(code);
        var root = tree.GetRoot();
        return FormatSyntaxNode(root, options);
    }

    /// <summary>
    /// SyntaxNode を直接受け取ってフォーマット（推奨）
    /// </summary>
    public string FormatSyntaxNode(SyntaxNode node, FormattingOptions options = default)
    {
        options ??= new FormattingOptions();

        // 1. Roslyn の NormalizeWhitespace を適用
        var normalized = node.NormalizeWhitespace(
            indentation: new string(' ', options.IndentSize),
            eol: options.NewLine,
            elasticTrivia: false
        );

        // 2. カスタムフォーマット処理（必要に応じて）
        normalized = ApplyCustomFormatting(normalized, options);

        // 3. 文字列化
        return normalized.ToFullString();
    }

    private SyntaxNode ApplyCustomFormatting(SyntaxNode node, FormattingOptions options)
    {
        // カスタムフォーマットルールを適用
        // 例: 特定の属性の配置、XMLコメントの整形など
        var rewriter = new CustomFormattingRewriter(options);
        return rewriter.Visit(node);
    }

    private class CustomFormattingRewriter : CSharpSyntaxRewriter
    {
        private readonly FormattingOptions _options;

        public CustomFormattingRewriter(FormattingOptions options)
        {
            _options = options;
        }

        public override SyntaxNode? VisitPropertyDeclaration(PropertyDeclarationSyntax node)
        {
            // プロパティ宣言の前に空行を追加するなどのカスタムルール
            var result = base.VisitPropertyDeclaration(node);

            if (result is PropertyDeclarationSyntax propertyDeclaration)
            {
                // 前に空行を追加
                var leadingTrivia = propertyDeclaration.GetLeadingTrivia();
                if (!leadingTrivia.Any(t => t.IsKind(SyntaxKind.EndOfLineTrivia)))
                {
                    propertyDeclaration = propertyDeclaration.WithLeadingTrivia(
                        leadingTrivia.Insert(0, SyntaxFactory.CarriageReturnLineFeed)
                    );
                }

                return propertyDeclaration;
            }

            return result;
        }
    }
}

// フォーマッターの合成（内部実装）
internal class CompositeCodeFormatter : ICodeFormatter
{
    private readonly List<ICodeFormatter> _formatters;

    public CompositeCodeFormatter(params ICodeFormatter[] formatters)
    {
        _formatters = formatters.ToList();
    }

    public string Format(string code, FormattingOptions options = default)
    {
        var current = code;
        foreach (var formatter in _formatters)
        {
            current = formatter.Format(current, options);
        }
        return current;
    }

    public string FormatSyntaxNode(SyntaxNode node, FormattingOptions options = default)
    {
        // 構文木も対応
        var current = node;
        foreach (var formatter in _formatters)
        {
            var formatted = formatter.FormatSyntaxNode(current, options);
            current = CSharpSyntaxTree.ParseText(formatted).GetRoot();
        }
        return current.ToFullString();
    }
}
```

---

### Phase 2: 外部ライブラリ向けユーティリティ（Issue #252対応・修正版）

#### 2.1 Source Generatorの制約を考慮した設計

**重要な前提:**
- Source Generatorは各プロジェクトのコンパイル時に独立して動作
- 外部ライブラリから直接Linqraftの生成ワークフローに乗ることは**不可能**
- 提供できるのは**構文解析・変換のユーティリティ**のみ

**修正後の方針:**
```csharp
namespace Linqraft.Core.Utilities;

// 外部ライブラリ向けに公開するユーティリティパッケージ
// ※ Source Generation ではなく、構文解析・変換のヘルパー機能を提供

/// <summary>
/// 外部ライブラリが Linqraft の構文解析機能を利用するためのユーティリティ
/// </summary>
public static class LinqraftSyntaxUtilities
{
    /// <summary>
    /// Lambda式から匿名型の構造を解析
    /// </summary>
    public static AnonymousTypeStructure ParseAnonymousType(
        LambdaExpressionSyntax lambda,
        SemanticModel semanticModel)
    {
        var parser = new LambdaAnonymousTypeParser();
        var context = new PipelineContext
        {
            TargetNode = lambda,
            SemanticModel = semanticModel
        };
        var parsed = parser.Parse(context);

        // 解析結果を返す（生成はしない）
        return new AnonymousTypeStructure
        {
            Properties = ExtractProperties(parsed.ObjectCreation, semanticModel),
            ParameterName = parsed.LambdaParameterName
        };
    }

    /// <summary>
    /// 式を変換するためのパイプラインを構築
    /// </summary>
    public static TransformationPipeline CreateTransformationPipeline(
        params IExpressionTransformer[] customTransformers)
    {
        var defaultTransformers = new IExpressionTransformer[]
        {
            new NullConditionalTransformer(),
            new SelectTransformer(),
            // ... デフォルトの変換器
        };

        return new TransformationPipeline(
            defaultTransformers.Concat(customTransformers)
        );
    }

    /// <summary>
    /// Func&lt;T, anonymous&gt; から DTO 構造を推論
    /// </summary>
    public static DtoStructure InferDtoStructure(
        ExpressionSyntax expression,
        SemanticModel semanticModel)
    {
        var analyzer = new DtoStructureAnalyzer();
        return analyzer.AnalyzeProjection(expression, semanticModel);
    }
}

// 外部ライブラリでの使用例
public class MyLibrarySourceGenerator : ISourceGenerator
{
    public void Execute(GeneratorExecutionContext context)
    {
        // Linqraft のユーティリティを使って構文解析
        var lambda = FindLambdaExpression(context);
        var structure = LinqraftSyntaxUtilities.ParseAnonymousType(
            lambda,
            context.Compilation.GetSemanticModel(lambda.SyntaxTree)
        );

        // 自分のライブラリ独自の生成ロジックを実行
        var myCode = GenerateMyLibraryCode(structure);

        context.AddSource("MyGenerated.g.cs", myCode);
    }
}
```

#### 2.2 外部ライブラリが利用できる機能

**公開API:**
1. **構文解析ユーティリティ**
   - Lambda式の解析
   - 匿名型の構造抽出
   - LINQ メソッドチェーンの解析

2. **変換パイプライン**
   - カスタム変換器の登録
   - 既存の変換器の再利用

3. **型情報解析**
   - DTO構造の推論
   - Nullable解析
   - コレクション型の判定

**利用不可能（Source Generatorの制約）:**
- ❌ Linqraftの生成ワークフローへの直接参加
- ❌ Linqraftが生成するDTOクラスの直接利用
- ❌ Linqraftのインターセプター機能の利用

#### 2.3 アクセス修飾子の設計

**外部公開 API (`public`)**:
```csharp
// ✅ 外部から利用される公開API
public static class LinqraftSyntaxUtilities
{
    public static AnonymousTypeStructure ParseAnonymousType(...) { }
    public static DtoStructure InferDtoStructure(...) { }
    public static TransformationPipeline CreateTransformationPipeline(...) { }
}
```

**内部実装 (`internal`)**:
```csharp
// ✅ 内部実装の詳細（外部に公開しない）
internal class DtoStructureAnalyzer
{
    internal DtoStructure AnalyzeProjection(...) { }
}

internal class LambdaAnonymousTypeParser : ISyntaxParser
{
    public ParsedSyntax Parse(...) { }
}

// ✅ パイプラインの内部コンポーネント
internal interface IPipelineStage { }
internal class TransformationPipeline { }
```

**設計原則:**
- `LinqraftSyntaxUtilities` の public メソッドのみが外部契約
- 内部で使用されるパーサー、アナライザー、パイプラインは `internal`
- Source Generator や Analyzer からは `InternalsVisibleTo` でアクセス
- 外部ライブラリは `LinqraftSyntaxUtilities` の public メソッドのみ利用可能

---

### Phase 3: SelectExprInfo のリファクタリング

#### 3.1 責務の分離（パイプライン適用）

**現在:** `SelectExprInfo.cs` (2,176行) が以下をすべて担当
- DTO構造解析
- プロパティ割り当て生成
- クエリ変換
- 式木構築
- コード生成

**分離後:**

```csharp
// SelectExprInfo は orchestrator として簡素化
public abstract record SelectExprInfo
{
    protected readonly CodeGenerationPipeline Pipeline;

    protected SelectExprInfo(CodeGenerationPipeline pipeline)
    {
        Pipeline = pipeline;
    }

    public abstract IEnumerable<GenerateDtoClassInfo> GenerateDtoClasses();
    public abstract string GenerateInterceptorCode();

    // 具体的な処理はパイプラインに委譲
    protected string GeneratePropertyMapping(ExpressionSyntax expression)
    {
        var context = new TransformContext
        {
            Expression = expression,
            SemanticModel = SemanticModel,
            ExpectedType = GetExpectedType()
        };

        return Pipeline.Transform(context).ToString();
    }
}

// 各サブクラスは具体的なパターン処理のみに集中（内部実装）
internal record SelectExprInfoExplicitDto : SelectExprInfo
{
    public override IEnumerable<GenerateDtoClassInfo> GenerateDtoClasses()
    {
        // DtoStructureAnalyzer を使用
        var analyzer = new DtoStructureAnalyzer();
        var structure = analyzer.AnalyzeProjection(Invocation, SemanticModel);

        // GenerateDtoClassInfo に委譲
        return new[] { new GenerateDtoClassInfo(structure, Configuration) };
    }

    public override string GenerateInterceptorCode()
    {
        // InterceptorGenerator に委譲
        var generator = new InterceptorGenerator();
        return generator.Generate(this);
    }
}
```

#### 3.2 設定の分離

```csharp
// 設定プロバイダーパターン（内部実装）
internal interface ILinqraftConfigurationProvider
{
    LinqraftConfiguration GetConfiguration(ConfigurationContext context);
}

internal record ConfigurationContext
{
    public required string Namespace { get; init; }
    public required ITypeSymbol SourceType { get; init; }
    public Dictionary<string, string> AdditionalOptions { get; init; } = new();
}

// パイプラインに設定を注入（内部実装）
internal class ConfigurableCodeGenerationPipeline : CodeGenerationPipeline
{
    private readonly ILinqraftConfigurationProvider _configProvider;

    public ConfigurableCodeGenerationPipeline(
        ILinqraftConfigurationProvider configProvider,
        /* 他のコンポーネント */)
        : base(/* ... */)
    {
        _configProvider = configProvider;
    }

    public override GenerationResult Execute(SyntaxNode root, SemanticModel semanticModel)
    {
        // 実行時に動的に設定を取得
        var config = _configProvider.GetConfiguration(new ConfigurationContext
        {
            Namespace = GetNamespace(root),
            SourceType = GetSourceType(root, semanticModel)
        });

        // 設定をパイプラインに適用
        ApplyConfiguration(config);

        return base.Execute(root, semanticModel);
    }
}
```

---

### Phase 4: 将来的な拡張の設計（Issue #228: 逆変換機能など）

**注意**: この Phase は将来的な拡張として設計されており、**今回のリファクタリング実装には含まれません**。
ただし、パイプラインアーキテクチャを設計する際に、**将来的にこのような機能を追加できる拡張ポイント**を確保しておきます。

#### 4.1 逆変換可能性の判定

**注**: 以下のコード例では、将来的な設計を示すため `public` を使用していますが、実装時にはアクセス修飾子設計の原則に従い `internal` として実装されます。

```csharp
namespace Linqraft.Core.Pipeline.ReverseMapping;

// 逆変換解析もパイプラインの一部として実装（実装時は internal）
public interface IReverseConversionAnalyzer : ISemanticAnalyzer
{
    ReverseConversionCapability AnalyzeConversionCapability(DtoStructure structure);
}

public record ReverseConversionCapability
{
    public bool IsFullyReversible { get; init; }
    public IReadOnlyList<PropertyReverseCapability> Properties { get; init; }
    public IReadOnlyList<string> Warnings { get; init; }
}

public record PropertyReverseCapability
{
    public required string PropertyName { get; init; }
    public required bool IsReversible { get; init; }
    public string? Reason { get; init; }
    public ReverseStrategy Strategy { get; init; }
}

public enum ReverseStrategy
{
    DirectAssignment,     // x.Foo = dto.Foo
    NestedMapping,        // x.Foo = FromDto(dto.Foo)
    CollectionMapping,    // x.Items = dto.Items.Select(FromDto).ToList()
    Ignore,               // 集約操作など、逆変換不可能
    Custom                // カスタム変換ロジック
}

// ホワイトリストベースの判定実装
public class WhitelistReverseConversionAnalyzer : IReverseConversionAnalyzer
{
    public ReverseConversionCapability AnalyzeConversionCapability(DtoStructure structure)
    {
        var properties = new List<PropertyReverseCapability>();

        foreach (var prop in structure.Properties)
        {
            var capability = AnalyzeProperty(prop);
            properties.Add(capability);
        }

        return new ReverseConversionCapability
        {
            IsFullyReversible = properties.All(p => p.IsReversible),
            Properties = properties,
            Warnings = GenerateWarnings(properties)
        };
    }

    private PropertyReverseCapability AnalyzeProperty(DtoProperty property)
    {
        var expr = property.Expression;

        // 1. Simple property access: x.Foo.Bar -> 逆変換可能
        if (IsSimplePropertyAccess(expr))
        {
            return new PropertyReverseCapability
            {
                PropertyName = property.Name,
                IsReversible = true,
                Strategy = ReverseStrategy.DirectAssignment
            };
        }

        // 2. Select/SelectMany: x.Items.Select(...) -> 逆変換可能
        if (IsSelectPattern(expr))
        {
            return new PropertyReverseCapability
            {
                PropertyName = property.Name,
                IsReversible = true,
                Strategy = ReverseStrategy.CollectionMapping
            };
        }

        // 3. Aggregation: x.Items.Count() -> 逆変換不可能
        if (IsAggregation(expr))
        {
            return new PropertyReverseCapability
            {
                PropertyName = property.Name,
                IsReversible = false,
                Reason = "Aggregation operations cannot be reversed",
                Strategy = ReverseStrategy.Ignore
            };
        }

        // 4. Where/First/Last: 無視（逆変換時は単に無視）
        if (IsFilteringOperation(expr))
        {
            return new PropertyReverseCapability
            {
                PropertyName = property.Name,
                IsReversible = true,
                Strategy = ReverseStrategy.DirectAssignment,
                Reason = "Filtering will be ignored during reverse conversion"
            };
        }

        // デフォルト: 逆変換不可能
        return new PropertyReverseCapability
        {
            PropertyName = property.Name,
            IsReversible = false,
            Reason = "Unsupported expression pattern for reverse conversion"
        };
    }

    private bool IsSimplePropertyAccess(ExpressionSyntax expr)
    {
        // x.Foo, x.Foo.Bar, x.Foo?.Bar などのシンプルなプロパティアクセス
        return expr is MemberAccessExpressionSyntax
            || expr is ConditionalAccessExpressionSyntax;
    }

    private bool IsSelectPattern(ExpressionSyntax expr)
    {
        // LinqMethodHelper を活用
        return LinqMethodHelper.IsSelectInvocation(expr)
            || LinqMethodHelper.IsSelectManyInvocation(expr);
    }

    private bool IsAggregation(ExpressionSyntax expr)
    {
        // Count, Sum, Average, Min, Max など
        if (expr is not InvocationExpressionSyntax invocation)
            return false;

        var methodName = invocation.Expression.ToString();
        return methodName.EndsWith("Count")
            || methodName.EndsWith("Sum")
            || methodName.EndsWith("Average")
            || methodName.EndsWith("Min")
            || methodName.EndsWith("Max");
    }

    private bool IsFilteringOperation(ExpressionSyntax expr)
    {
        if (expr is not InvocationExpressionSyntax invocation)
            return false;

        var methodName = invocation.Expression.ToString();
        return methodName.EndsWith("Where")
            || methodName.EndsWith("First")
            || methodName.EndsWith("FirstOrDefault")
            || methodName.EndsWith("Last")
            || methodName.EndsWith("LastOrDefault");
    }
}
```

#### 4.2 逆変換コード生成

```csharp
namespace Linqraft.Core.Pipeline.ReverseMapping;

// 逆変換コード生成もパイプラインの一部
public class ReverseConverterGenerator : ICodeGenerator
{
    private readonly ICodeFormatter _formatter;

    public ReverseConverterGenerator(ICodeFormatter formatter)
    {
        _formatter = formatter;
    }

    public string Generate(ReverseConverterGenerationContext context)
    {
        var code = GenerateRawCode(context);

        // 最後にフォーマット（フォーマットフェーズで一括処理）
        return _formatter.Format(code);
    }

    private string GenerateRawCode(ReverseConverterGenerationContext context)
    {
        var sb = new StringBuilder();
        var entityType = context.EntityType.ToDisplayString();
        var dtoType = context.DtoStructure.DtoClassName;

        // クラス宣言
        sb.AppendLine($"public partial class {context.ConverterClassName}");
        sb.AppendLine("{");

        // FromDto メソッド
        GenerateFromDtoMethod(sb, context, entityType, dtoType);

        // コレクション用メソッド
        if (context.Options.GenerateCollectionOverloads)
        {
            GenerateCollectionFromDtoMethod(sb, context, entityType, dtoType);
        }

        sb.AppendLine("}");

        return sb.ToString();
    }

    private void GenerateFromDtoMethod(
        StringBuilder sb,
        ReverseConverterGenerationContext context,
        string entityType,
        string dtoType)
    {
        var methodName = context.Options.MethodNamePrefix;
        var isStatic = context.Options.GenerateAsStatic ? "static " : "";

        sb.AppendLine($"public {isStatic}{entityType} {methodName}({dtoType} dto)");
        sb.AppendLine("{");
        sb.AppendLine($"return new {entityType}");
        sb.AppendLine("{");

        foreach (var prop in context.Capability.Properties.Where(p => p.IsReversible))
        {
            var assignment = GeneratePropertyReverseAssignment(prop);
            sb.AppendLine($"{assignment},");
        }

        sb.AppendLine("};");
        sb.AppendLine("}");
    }

    private string GeneratePropertyReverseAssignment(PropertyReverseCapability capability)
    {
        return capability.Strategy switch
        {
            ReverseStrategy.DirectAssignment => $"{capability.PropertyName} = dto.{capability.PropertyName}",
            ReverseStrategy.NestedMapping => $"{capability.PropertyName} = FromDto(dto.{capability.PropertyName})",
            ReverseStrategy.CollectionMapping => $"{capability.PropertyName} = dto.{capability.PropertyName}.Select(FromDto).ToList()",
            _ => throw new NotSupportedException()
        };
    }
}

public record ReverseConverterGenerationContext
{
    public required DtoStructure DtoStructure { get; init; }
    public required ITypeSymbol EntityType { get; init; }
    public required ReverseConversionCapability Capability { get; init; }
    public required string ConverterClassName { get; init; }
    public ReverseConverterOptions Options { get; init; } = new();
}

public record ReverseConverterOptions
{
    public bool GenerateAsStatic { get; init; } = true;
    public bool GenerateCollectionOverloads { get; init; } = true;
    public string MethodNamePrefix { get; init; } = "FromDto";
}
```

#### 4.3 属性ベースの生成トリガー

```csharp
// ユーザーコード（将来的な実装例）
[LinqraftReverseConversion(IsStatic = true, MethodName = "ToEntity")]
public partial class UserDtoReverseConverter;

// Source Generator での処理（将来的な実装例）
public class ReverseConversionSourceGenerator : ISourceGenerator
{
    public void Execute(GeneratorExecutionContext context)
    {
        // 属性が付いたクラスを検出
        var candidates = FindReverseConversionCandidates(context);

        foreach (var candidate in candidates)
        {
            // パイプラインを使って生成（既存のパイプラインアーキテクチャを再利用）
            var pipeline = CreateReverseConversionPipeline();
            var result = pipeline.Execute(candidate.Node, candidate.SemanticModel);

            context.AddSource($"{candidate.ClassName}.g.cs", result.Code);
        }
    }

    private SyntaxTreeGenerationPipeline CreateReverseConversionPipeline()
    {
        // 既存のパイプラインコンポーネントを組み合わせて逆変換パイプラインを構築
        return new SyntaxTreeGenerationPipeline(
            matcher: new ReverseConversionAttributeMatcher(),
            parser: new ReverseConversionParser(),
            analyzer: new WhitelistReverseConversionAnalyzer(),
            transformer: new IdentityTransformer(), // 逆変換では変換不要
            generator: new ReverseConverterGenerator(),
            enricher: new SemanticEnricher(semanticModel, compilation),
            formatter: new SyntaxTreeFormatter()
        );
    }
}
```

**設計上の考慮点（今回のリファクタリングで整備）:**
- ✅ パイプラインアーキテクチャにより、逆変換生成器を容易に追加可能
- ✅ `ISyntaxTreeGenerator` を実装することで、逆変換コード生成を統一的に実装
- ✅ `SemanticEnricher` により、逆変換でも型情報を正確に扱える
- ✅ 既存の `DtoStructure` 解析ロジックを再利用可能

---

### Phase 5: ドキュメントとテスト（最終工程）

#### 5.1 リファクタリングガイドの作成

`docs/developments/refactoring-guide.md`:

```markdown
# Linqraft リファクタリングガイド

## アーキテクチャ概要

### パイプライン処理フロー

Linqraft のコード生成は、以下の6つのフェーズからなるパイプラインで処理されます：

1. **Discovery**: 対象パターンの特定（SelectExpr呼び出し、属性など）
2. **Parsing**: 構文解析（Lambda, 匿名型など）
3. **Semantic Analysis**: 意味解析（型情報、Nullable、Captureなど）
4. **Transformation**: 式変換（null条件演算子、Select変換など）
5. **Generation**: コード生成（DTO、インターセプターなど）
6. **Formatting**: コードフォーマット（インデント、空白、改行の統一）

各フェーズは独立しており、パーツとして組み合わせ可能です。

### パッケージ構成

- `Linqraft`: ランタイムライブラリ
- `Linqraft.Core`: 共通インフラ（Analyzer + SourceGenerator + Pipeline）
- `Linqraft.Core.Utilities`: 外部ライブラリ向けユーティリティ
- `Linqraft.Analyzer`: Roslyn Analyzer
- `Linqraft.SourceGenerator`: Source Generator

### ヘルパークラスの構成

[Phase 1.2 の構造図を記載]

## 拡張ポイント

### カスタム変換器の追加

```csharp
public class MyCustomTransformer : IExpressionTransformer
{
    public int Priority => 50;

    public bool CanTransform(TransformContext context)
    {
        // 変換対象かどうかを判定
        return context.Expression is /* 特定のパターン */;
    }

    public ExpressionSyntax Transform(TransformContext context)
    {
        // 変換ロジック
        return /* 変換後の式 */;
    }
}

// パイプラインに登録
var pipeline = new TransformationPipeline(new IExpressionTransformer[]
{
    new NullConditionalTransformer(),
    new MyCustomTransformer(), // カスタム変換器を追加
    // ...
});
```

### 外部ライブラリからの利用

**重要:** Source Generatorの制約により、外部ライブラリから直接Linqraftの生成ワークフローに参加することはできません。
利用できるのは構文解析・変換のユーティリティのみです。

```csharp
// 外部ライブラリのSource Generatorで利用
public class MyLibraryGenerator : ISourceGenerator
{
    public void Execute(GeneratorExecutionContext context)
    {
        // Linqraft のユーティリティを使って構文解析
        var structure = LinqraftSyntaxUtilities.InferDtoStructure(
            expression,
            semanticModel
        );

        // 自分のライブラリ独自の生成ロジック
        var code = GenerateMyCode(structure);
        context.AddSource("Generated.g.cs", code);
    }
}
```

## 実装計画

**注意**: Issue #228（逆変換機能）は将来的な拡張として設計に含めていますが、**今回のリファクタリング実装には含まれません**。
パイプラインアーキテクチャを整備することで、将来的に容易に追加できるようにします。

### マイルストーン

#### M1: パイプラインアーキテクチャ構築（4-5週間）← Phase 1
- [ ] **1.1: 新しいディレクトリ構造の作成**
  - [ ] `Pipeline/` フォルダ構造を作成
  - [ ] 各フェーズのインターフェース定義
- [ ] **1.2: パイプライン基盤実装**
  - [ ] `IPipelineStage`, `PipelineContext` 実装
  - [ ] `SyntaxTreeGenerationPipeline` 実装
- [ ] **1.3: 既存コードの移行と重複排除**（同時進行）
  - [ ] Helpers の移行（SyntaxHelpers → Helpers/Syntax）
  - [ ] **NullCheckHelper 削除、NullConditionalHelper に統合**
  - [ ] CaptureHelper を3つのクラスに分割・移行
  - [ ] **IsPartOfMemberAccess 重複解消**
  - [ ] **アクセス修飾子の見直し**
    - [ ] パイプライン内部ロジックを `internal` 化
    - [ ] ヘルパークラスを `internal` 化（外部公開APIを除く）
    - [ ] `InternalsVisibleTo` 属性を AssemblyInfo.cs に追加
- [ ] **1.4: 各フェーズの実装**
  - [ ] Discovery/Parsing/Analysis フェーズ実装
  - [ ] Transformation パイプライン実装
  - [ ] **Generation フェーズ実装（構文木ベース）** ← 重要
    - [ ] `ISyntaxTreeGenerator` インターフェース
    - [ ] `DtoClassSyntaxGenerator` 実装（SyntaxFactory）
    - [ ] `SemanticEnricher` 実装（完全修飾名変換）
  - [ ] Formatting フェーズ実装（構文木対応）
    - [ ] `SyntaxTreeFormatter` 実装（NormalizeWhitespace）
- [ ] パイプライン統合テスト
- [ ] 既存テストの更新
- [ ] CI/CD の確認

#### M2: 外部ライブラリ向けユーティリティ（2週間）← Phase 2
- [ ] `LinqraftSyntaxUtilities` 実装（`public` として公開）
- [ ] 内部実装ヘルパーは `internal` として分離
- [ ] ドキュメント作成（Source Generatorの制約を明記）
- [ ] サンプルプロジェクト作成

#### M3: SelectExprInfo リファクタリング（2-3週間）← Phase 3
- [ ] SelectExprInfo の簡素化（パイプライン適用）
- [ ] 設定プロバイダー実装
- [ ] 既存機能の動作確認
- [ ] パフォーマンステスト

#### M4: 仕上げ（1-2週間）← Phase 5
- [ ] ドキュメント整備
- [ ] 統合テスト
- [ ] パフォーマンス最適化
- [ ] Breaking Changes の文書化
- [ ] リリースノート作成

---

## 期待される効果

### Issue #246（コードの構造化）
- ✅ 重複コード削除により保守性向上
- ✅ パイプライン設計により責務が明確化
- ✅ ヘルパークラスの発見可能性向上
- ✅ 新規開発者のオンボーディング時間短縮

### Issue #252（外部ライブラリ対応）
- ✅ `LinqraftSyntaxUtilities` により構文解析機能を提供
- ✅ カスタム変換器により拡張可能
- ✅ Source Generatorの制約を明確化し、誤解を防止

### 将来的な拡張への対応（Issue #228など）
- ✅ パイプラインアーキテクチャにより拡張ポイントが明確
- ✅ 逆変換機能などを将来的に追加可能な設計
- ✅ 構文木ベース生成により、双方向マッピングの基盤を整備

### 全体的な改善
- ✅ SelectExprInfo の複雑度低減（2,176行 → パイプライン適用で大幅削減）
- ✅ パーツの組み合わせで汎用的に対応可能
- ✅ フォーマット処理の一元化（最終フェーズで一括処理）
- ✅ **構文木ベース生成によるセマンティック情報の保持**
- ✅ 型の完全修飾名を正確に生成（曖昧性の排除）
- ✅ 拡張ポイントが明確
- ✅ テスタビリティ向上

---

## 次のステップ

1. **このリファクタリング設計案のレビュー**
   - 各Issueの要件を満たしているか確認
   - パイプライン設計の妥当性検証
   - フォーマットフェーズの実現可能性確認

2. **プロトタイプ作成**
   - Phase 2.2（構文解析パイプライン）の小規模実装
   - Phase 2.4（フォーマットフェーズ）の実装と性能検証
   - 外部ライブラリからの利用シナリオ検証

3. **実装開始**
   - Phase 1から順次実装
   - 各フェーズ完了後にレビュー

---

## 参照

- Issue #246: Refactor the codebase (Linqraft.Core)
- Issue #252: feat: Provides features to make it easier to use Linqraft functionality from other libraries
- Issue #228: proposal: Add "reverse conversion" methods from DTOs to the original Entities in the auto-generated API（将来的な拡張として設計）

## フィードバック反映内容

### 1. 全体フロー設計の見直し
- ✅ パイプラインアーキテクチャを導入（Discovery → Parsing → Analysis → Transformation → Generation → Formatting）
- ✅ パーツを組み合わせて汎用的に対応できる設計
- ✅ アドホックな実装を排除し、統一的なフローで処理

### 2. Source Generatorの制約を考慮
- ✅ 外部ライブラリが直接生成ワークフローに乗れないことを明記
- ✅ `LinqraftSyntaxUtilities` として構文解析機能のみを提供
- ✅ ドキュメントで制約を明確化

### 3. フォーマットフェーズの追加
- ✅ 最終フェーズで一括フォーマットを行う設計
- ✅ 生成時はフォーマットを気にせず、ロジックに集中
- ✅ `SyntaxTreeFormatter` による構文木ベースのフォーマット処理
- ✅ Roslyn の `NormalizeWhitespace()` を活用

### 4. 構文木ベースのコード生成（新規追加）
- ✅ 文字列ベース → Roslyn SyntaxFactory による構文木生成
- ✅ `ISyntaxTreeGenerator` インターフェース
- ✅ `SemanticEnricher` によるセマンティック情報の付加
- ✅ 型の完全修飾名を自動生成（`List<int>` → `global::System.Collections.Generic.List<int>`）
- ✅ ジェネリック型、Nullable型の正確な処理
