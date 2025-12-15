# パイプライン統合状況レポート

## 概要
このドキュメントは、refactoring-design-proposal.md に基づいたパイプラインアーキテクチャの統合状況を記録します。

## 完了した実装 ✅

### 1. パイプライン基盤 (Phase 1)
- **ディレクトリ構造**: `src/Linqraft.Core/Pipeline/` 配下に完全な構造を構築
  - `Discovery/` - パターンマッチング (SelectExprMatcher, MappingAttributeMatcher, CompositePatternMatcher)
  - `Parsing/` - 構文解析 (LinqExpressionParser, LambdaAnonymousTypeParser, ObjectCreationParser, LambdaParsingHelper)
  - `Analysis/` - 意味解析 (TypeAnalyzer, CaptureAnalyzer, DtoAnalyzer)
  - `Transformation/` - 式変換 (TransformationPipeline, NullConditionalTransformer, FullyQualifyingTransformer)
  - `Generation/` - コード生成 (DtoGenerator, DtoCodeBuilder, SelectExprCodeGenerator, SelectExprPipelineProcessor, InterceptorGenerator, MappingMethodGenerator, PropertyAssignmentGenerator, NullCheckGenerator, CollectionHelper, SourceCodeGenerator, StaticFieldGenerator, SemanticEnricher, CodeGenerationOrchestrator)
  - `Formatting/` - フォーマット (SyntaxTreeFormatter, FormattingOptions, ICodeFormatter)

### 2. 重複コード削除とヘルパー統合
- ✅ NullCheckHelper 削除 (デッドコードを削除)
- ✅ IsPartOfMemberAccess の重複解消
- ✅ ヘルパークラスを `internal` 化:
  - SyntaxHelpers/ (ArgumentListHelper, ExpressionHelper, LinqMethodHelper, LambdaHelper, ObjectCreationHelper, TriviaHelper, NullConditionalHelper)
  - RoslynHelpers/ (RoslynTypeHelper, DocumentationCommentHelper)
  - AnalyzerHelpers/ (CaptureHelper, SyntaxGenerationHelper, SyntaxHelper, UsingDirectiveHelper)
- ✅ `DocumentationInfo` を `public` として分離
- ✅ `InternalsVisibleTo` 属性を `Linqraft.Core.csproj` に追加

### 3. SelectExprInfo でのパイプライン統合 (部分的)
`SelectExprInfo` は既に以下のメソッドでパイプラインを使用:

```csharp
// SelectExprInfo.cs での統合例
internal CodeGenerationPipeline GetPipeline()
{
    _pipeline ??= new CodeGenerationPipeline(SemanticModel, Configuration);
    return (CodeGenerationPipeline)_pipeline;
}

// パイプラインを使用しているメソッド:
- FullyQualifyExpression() // FullyQualifyingTransformer
- ConvertToExplicitNullCheck() // NullCheckGenerator
- GetDefaultValueForType() // NullCheckGenerator
- ExtractSelectInfo() // LinqExpressionParser
- ExtractSelectExprInfo() // LinqExpressionParser
```

**統合済みメソッド数**: SelectExprInfo 内の約 30% のヘルパーメソッドがパイプラインを使用

### 4. 外部ライブラリ向けユーティリティ (Phase 2)
- ✅ `LinqraftSyntaxUtilities` を `public` として実装
  - `ParseAnonymousType()` - Lambda式から匿名型構造を解析
  - `InferPropertiesFromExpression()` - 式からプロパティを推論
  - `HasNullConditionalAccess()` - Null条件演算子の検出
  - `GetLambdaParameterName()`, `GetLambdaBody()` - Lambda式ヘルパー

### 5. SelectExprInfo のリファクタリング (Phase 3)
- ✅ SelectExprInfo のサイズ削減: **2190行 → 1458行 (33.4%削減)**
- ✅ 以下のメソッドをパイプラインに委譲:
  - `FullyQualifyAllReferences` → `pipeline.FullyQualifyExpression`
  - `FullyQualifyAllStaticReferences` → `pipeline.FullyQualifyExpression`
  - `ExtractSelectInfoFromSyntax` → `LinqExpressionParser`
  - `ExtractSelectExprInfoFromSyntax` → `LinqExpressionParser`
  - `ConvertNullableAccessToExplicitCheckWithRoslyn` → `NullCheckGenerator`
  - `GetEmptyCollectionExpression` → `CollectionHelper`
  - `GetDefaultValueForType` → `NullCheckGenerator`

## 未完了の実装 🔄

### メインの生成ロジックの統合

**課題**: `SelectExprInfo.GenerateSelectExprMethod()` と `SelectExprGroups` でのパイプライン使用

#### 現在の状況

1. **SelectExprInfo サブクラスの実装** (`SelectExprInfoAnonymous`, `SelectExprInfoNamed`, `SelectExprInfoExplicitDto`)
   - 各サブクラスで `GenerateSelectExprMethod()` を実装
   - 従来の文字列ビルダーベースのコード生成を使用
   - 複雑なロジック (キャプチャ変数、プリビルト式、ネストされた構造など) を含む

2. **SelectExprGroups での生成**
   - `GenerateCodeWithoutDtos()` メソッドで実際の Source Generator 処理を実行
   - まだ従来の `GenerateSourceCodeSnippets` を使用:
     ```csharp
     // 現在の実装
     var exprMethods = info.GenerateSelectExprCodes(expr.Location);  // 従来の方法
     var dtoCode = GenerateSourceCodeSnippets.BuildDtoCodeSnippetsGroupedByNamespace(..);
     ```

#### 必要な作業

##### オプション1: 既存メソッドの内部実装を変更 (推奨)
`SelectExprInfo` サブクラスの `GenerateSelectExprMethod()` 内でパイプラインを使用:

```csharp
// SelectExprInfoAnonymous.cs の変更例
protected override string GenerateSelectExprMethod(
    string dtoName,
    DtoStructure structure,
    InterceptableLocation location)
{
    // パイプラインを使用した新しい実装
    var pipeline = GetPipeline();
    var processor = new SelectExprPipelineProcessor(SemanticModel, Configuration);

    var result = processor.ProcessAnonymous(
        Invocation,
        AnonymousObject,
        SourceType,
        LambdaParameterName,
        CallerNamespace,
        CaptureArgumentExpression,
        CaptureArgumentType
    );

    var codeGenerator = new SelectExprCodeGenerator(pipeline);
    return codeGenerator.GenerateInterceptorMethod(
        result,
        location,
        IsEnumerableInvocation(),
        structure.SourceTypeFullName
    );
}
```

**利点**:
- 既存のAPI を維持
- テストへの影響を最小化
- 段階的な移行が可能

**課題**:
- 各サブクラスで個別に実装が必要
- 既存のロジック (キャプチャ変数、プリビルト式など) の移行が複雑

##### オプション2: SelectExprGroups に新しいパイプラインパスを追加
`SelectExprGroups` に新しいコード生成パスを追加し、段階的に移行:

```csharp
// SelectExprGroups.cs での新しいパス
public void GenerateCodeWithPipeline(SourceProductionContext context)
{
    var orchestrator = new CodeGenerationOrchestrator(Configuration);

    foreach (var expr in Exprs)
    {
        var info = expr.Info;
        var processor = new SelectExprPipelineProcessor(info.SemanticModel, Configuration);

        // パイプラインを使って処理
        SelectExprProcessingResult result = info switch
        {
            SelectExprInfoAnonymous anonymous => processor.ProcessAnonymous(...),
            SelectExprInfoNamed named => processor.ProcessNamed(...),
            SelectExprInfoExplicitDto explicit => processor.ProcessExplicitDto(...),
            _ => throw new NotSupportedException()
        };

        // コード生成
        var codeGenerator = new SelectExprCodeGenerator(processor.Pipeline);
        var code = codeGenerator.GenerateInterceptorMethod(...);

        // ソース追加
        context.AddSource(..., code);
    }
}
```

**利点**:
- 既存のコードを完全に保持
- 新旧の実装を並行して使用可能
- 段階的な検証が容易

**課題**:
- 一時的にコードの重複が発生
- 最終的には既存のパスを削除する必要がある

## 統合完了までのロードマップ

### フェーズ1: 基盤整備 ✅ (完了)
- [x] パイプラインアーキテクチャの構築
- [x] 重複コード削除
- [x] ヘルパークラスの internal 化
- [x] InternalsVisibleTo 属性の追加
- [x] 部分的な SelectExprInfo 統合

### フェーズ2: メインロジック統合 (未完了)
**推定作業時間**: 2-3週間

#### タスク2.1: SelectExprInfo サブクラスの移行
- [ ] `SelectExprInfoAnonymous.GenerateSelectExprMethod()` をパイプラインベースに変更
- [ ] `SelectExprInfoNamed.GenerateSelectExprMethod()` をパイプラインベースに変更
- [ ] `SelectExprInfoExplicitDto.GenerateSelectExprMethod()` をパイプラインベースに変更
- [ ] キャプチャ変数処理の統合
- [ ] プリビルト式機能の統合

#### タスク2.2: SelectExprGroups の統合
- [ ] `GenerateCodeWithPipeline()` メソッドの実装
- [ ] DTO生成のパイプライン統合
- [ ] インターセプター生成のパイプライン統合
- [ ] マッピングメソッド生成のパイプライン統合

#### タスク2.3: テストと検証
- [ ] 既存の334個のテストがパスすることを確認
- [ ] 新しいパイプラインベースのテストを追加
- [ ] パフォーマンステスト
- [ ] 生成されたコードの品質確認

### フェーズ3: クリーンアップ (未完了)
**推定作業時間**: 1週間

- [ ] 従来の `GenerateSourceCodeSnippets` の段階的削除
- [ ] 不要なコードの削除
- [ ] ドキュメント更新
- [ ] Breaking Changes の文書化

## 現在のビルド状況

- ✅ Linqraft.Core のビルド成功
- ✅ 134個のコアテストが成功
- ⚠️ 一部のテストで PolySharp 関連のビルドエラー (既存の問題、今回の変更とは無関係)

## 推奨される次のステップ

1. **Issue の作成**: フェーズ2とフェーズ3のタスクを GitHub Issue として登録
2. **プロトタイプの実装**: `SelectExprInfoAnonymous` の1つのメソッドでパイプライン統合のプロトタイプを実装
3. **段階的な移行**: テストを維持しながら、1つずつサブクラスを移行
4. **レビューとフィードバック**: 各フェーズ完了後にレビューを実施

## 参照

- [refactoring-design-proposal.md](./refactoring-design-proposal.md) - 元の設計書
- [GitHub PR #254](https://github.com/arika0093/Linqraft/pull/254) - 現在のリファクタリングPR
- Issue #246: Refactor the codebase (Linqraft.Core)
- Issue #252: feat: Provides features to make it easier to use Linqraft functionality from other libraries

---

**最終更新**: 2025-12-15
**ステータス**: フェーズ1完了、フェーズ2未着手
