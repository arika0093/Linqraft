# Linqraft コードベース リファクタリング計画

**作成日**: 2025-11-21
**目的**: 共通処理をLinqraft.Coreに集約し、コードベースを整理する

---

## 📊 調査結果サマリー

### 発見された主要問題点

1. **空白化・フォーマットの不統一** - 10ファイルに影響
   - 改行文字: `\n` ハードコード、`AppendLine()` (環境依存)、`NormalizeWhitespace()` が混在
   - インデント数: 4, 8スペースがハードコードされている
   - インデント生成方法: 複数の方法が混在

2. **文字列比較による型判定** - 9箇所の問題コード
   - `propertyType.EndsWith("?")` でNullable判定
   - `typeDisplayString.Contains("IQueryable")` で型判定
   - Roslynのセマンティック解析を使うべき箇所

3. **構文解析処理の点在** - 55箇所で重複パターン
   - ラムダ式解析: 5箇所で重複
   - プロパティ名取得: 4箇所で重複
   - LINQメソッド検出: 3箇所で重複
   - IQueryable判定: 2箇所で完全重複
   - ソース型取得: 4箇所で重複

4. **Analyzer/CodeFixの重複** - 15パターン、1,500～2,000行削減可能
   - 合計5,513行のコード
   - CaptureHelper関連で100行の完全重複（最大）
   - IQueryable判定で40行の完全重複
   - Using追加処理で4箇所重複

---

## 📈 期待効果

### コード削減
| フェーズ | 削減行数 | 影響ファイル数 |
|---------|---------|--------------|
| フェーズ1 | 約300行 | 10ファイル |
| フェーズ2 | 約600行 | 15ファイル |
| フェーズ3 | 約900行 | 15ファイル |
| フェーズ4 | 約200行 | 10ファイル |
| **合計** | **約2,000行** | **50ファイル** |

### 新規作成ファイル
- **Linqraft.Core/Formatting/** (1ファイル)
- **Linqraft.Core/RoslynHelpers/** (1ファイル)
- **Linqraft.Core/SyntaxHelpers/** (5ファイル)
- **Linqraft.Core/AnalyzerHelpers/** (6ファイル)

**合計**: 約13ファイル（約1,500行の新規共通コード）

### ネット効果
**約500行の削減** + **大幅な保守性向上**

---

## 🎯 リファクタリング計画

### フェーズ1: 基盤整備（高優先度）

#### ✅ 1-1. フォーマッティング統一
**目的**: 出力コードの一貫性確保

**作業内容**:
- `Linqraft.Core/Formatting/CodeFormatter.cs` を新規作成
- 改行文字、インデント定数を定義
- 全コード生成箇所（10ファイル）を修正

**影響ファイル**:
- SourceGenerator:
  - `SelectExprInfo.cs` (238, 260, 271, 286, 444, 474-486行)
  - `SelectExprInfoAnonymous.cs` (61-141行)
  - `SelectExprInfoExplicitDto.cs` (271-353行)
  - `SelectExprInfoNamed.cs` (69-150行)
  - `GenerateDtoClassInfo.cs` (80-229行)
  - `SelectExprGroups.cs` (157-163行)
- Analyzer:
  - `AnonymousTypeToDtoCodeFixProvider.cs` (171, 520-538行)
  - `SelectExprToTypedCodeFixProvider.cs` (124-137行)
  - `SelectToSelectExprAnonymousCodeFixProvider.cs` (318-326行)
  - `SelectToSelectExprNamedCodeFixProvider.cs` (578-586行)

**期待効果**: 出力コードの品質向上、メンテナンス性向上

---

#### ✅ 1-2. 型判定処理のRoslyn API化
**目的**: 型システムの正確性向上（最優先）

**作業内容**:
- `Linqraft.Core/RoslynHelpers/RoslynTypeHelper.cs` を新規作成
- 文字列比較による型判定（9箇所）を全て置き換え

**問題箇所**:
1. `GenerateDtoClassInfo.cs:152` - `propertyType.EndsWith("?")`
2. `GenerateDtoClassInfo.cs:194` - `!propertyType.EndsWith("?")`
3. `DtoProperty.cs:30` - `typeName == "?"`
4. `SelectExprInfo.cs:170, 174` - `Contains("IQueryable")`, `Contains("IEnumerable")`
5. `SelectExprInfo.cs:265` - `Contains("SelectMany")`
6. `SelectExprInfo.cs:289` - `Contains("Select")`
7. `GenerateDtoClassInfo.cs:158, 169` - `StartsWith("global::<anonymous")`, `Contains("<")`
8. `SelectExprGroups.cs:35` - `targetNamespace.Contains("<")`
9. `LocalVariableCaptureCodeFixProvider.cs:220` - `ToString().Contains("SelectExpr")`

**新規ヘルパーメソッド**:
```csharp
public static class RoslynTypeHelper
{
    public static bool IsNullableType(ITypeSymbol typeSymbol);
    public static ITypeSymbol GetNonNullableType(ITypeSymbol typeSymbol);
    public static bool ImplementsIQueryable(ITypeSymbol typeSymbol, Compilation compilation);
    public static bool ImplementsIEnumerable(ITypeSymbol typeSymbol, Compilation compilation);
    public static ITypeSymbol? GetGenericTypeArgument(ITypeSymbol typeSymbol, int index = 0);
    public static bool IsAnonymousType(ITypeSymbol typeSymbol);
    public static bool IsGlobalNamespace(INamespaceSymbol namespaceSymbol);
    public static bool ContainsSelectInvocation(ExpressionSyntax expression);
    public static bool ContainsSelectManyInvocation(ExpressionSyntax expression);
}
```

**期待効果**: Nullable型・ジェネリック型の判定精度向上、バグ防止

---

### フェーズ2: 構文解析の共通化（中優先度）

#### 2-1. ラムダ式ヘルパー作成
**目的**: ラムダ式解析の統一

**作業内容**:
- `Linqraft.Core/SyntaxHelpers/LambdaHelper.cs` を新規作成
- 5箇所の重複コードを統合

**影響箇所**:
- `SelectExprGenerator.cs:173-186`
- `LocalVariableCaptureAnalyzer.cs:207-218`
- `SelectExprInfo.cs:788-799`
- 各CodeFixProvider

**新規メソッド**:
```csharp
public static class LambdaHelper
{
    public static string GetLambdaParameterName(LambdaExpressionSyntax lambda);
    public static ImmutableHashSet<string> GetLambdaParameterNames(LambdaExpressionSyntax lambda);
    public static ExpressionSyntax GetLambdaBody(LambdaExpressionSyntax lambda);
    public static LambdaExpressionSyntax? FindLambdaInArguments(ArgumentListSyntax argumentList);
    public static AnonymousObjectCreationExpressionSyntax? FindAnonymousTypeInArguments(ArgumentListSyntax argumentList);
}
```

**期待効果**: コード削減 約150行

---

#### 2-2. 式解析ヘルパー作成
**目的**: プロパティ名抽出、オブジェクト検出の統一

**作業内容**:
- `Linqraft.Core/SyntaxHelpers/ExpressionHelper.cs` を新規作成
- プロパティ名取得（4箇所）を統合

**影響箇所**:
- `DtoStructure.cs:188-214`
- `DtoNamingHelper.cs:107-118`
- `SelectExprInfo.cs:923-939`
- `AnonymousTypeToDtoCodeFixProvider.cs:569-580`

**新規メソッド**:
```csharp
public static class ExpressionHelper
{
    public static string GetPropertyName(ExpressionSyntax expression);
    public static AnonymousObjectCreationExpressionSyntax? FindAnonymousObjectCreation(ExpressionSyntax expression);
    public static ObjectCreationExpressionSyntax? FindObjectCreation(ExpressionSyntax expression);
}
```

**期待効果**: コード削減 約100行

---

#### 2-3. LINQメソッド検出ヘルパー作成
**目的**: Select/SelectMany検出の統一

**作業内容**:
- `Linqraft.Core/SyntaxHelpers/LinqMethodHelper.cs` を新規作成
- LINQメソッド検出（3箇所）を統合

**影響箇所**:
- `DtoProperty.cs:441-510`
- `SelectExprInfo.cs:698-858`
- `AnonymousTypeToDtoCodeFixProvider.cs:415-465`

**新規メソッド**:
```csharp
public static class LinqMethodHelper
{
    public static InvocationExpressionSyntax? FindLinqMethodInvocation(ExpressionSyntax expression, params string[] methodNames);
    public static bool IsSelectInvocation(ExpressionSyntax expression);
    public static bool IsSelectManyInvocation(ExpressionSyntax expression);
    public static LinqInvocationInfo? ExtractLinqInvocationInfo(ExpressionSyntax expression, SemanticModel semanticModel);
}
```

**期待効果**: コード削減 約200行

---

#### 2-4. 型情報ヘルパー作成
**目的**: IQueryable判定、型引数抽出の統一

**作業内容**:
- `Linqraft.Core/SyntaxHelpers/TypeHelper.cs` を新規作成
- IQueryable判定（2箇所で完全重複）を統合
- ソース型取得（4箇所）を統合

**影響箇所**:
- `SelectToSelectExprAnonymousAnalyzer.cs:125-167`
- `SelectToSelectExprNamedAnalyzer.cs:125-167`
- `SelectExprToTypedAnalyzer.cs:143-174`
- 各CodeFixProvider (4箇所)

**新規メソッド**:
```csharp
public static class TypeHelper
{
    public static ITypeSymbol? GetSourceTypeFromQueryable(ExpressionSyntax expression, SemanticModel semanticModel, CancellationToken cancellationToken);
    public static bool IsIQueryable(ExpressionSyntax expression, SemanticModel semanticModel, CancellationToken cancellationToken);
    public static string GetNamespaceFromSyntaxNode(SyntaxNode node);
}
```

**期待効果**: コード削減 約150行

---

#### 2-5. Null条件演算子ヘルパー作成
**目的**: null条件演算子処理の統一

**作業内容**:
- `Linqraft.Core/SyntaxHelpers/NullConditionalHelper.cs` を新規作成

**影響箇所**:
- `SelectExprInfo.cs:860-920`
- `DtoProperty.cs:369-436`
- `TernaryNullCheckToConditionalAnalyzer.cs`
- `TernaryNullCheckSimplifier.cs`

**新規メソッド**:
```csharp
public static class NullConditionalHelper
{
    public static bool HasNullConditionalAccess(ExpressionSyntax expression);
    public static ExpressionSyntax ConvertToExplicitNullCheck(ExpressionSyntax expression, ITypeSymbol typeSymbol);
    public static ExpressionSyntax BuildNullConditionalChain(ExpressionSyntax expression, List<ExpressionSyntax> nullChecks);
    public static List<ExpressionSyntax> ExtractNullChecks(ExpressionSyntax condition);
}
```

**期待効果**: コード削減 約100行

---

#### 2-6. Trivia処理の高度化・共通化
**目的**: コードフォーマットの正確性向上

**作業内容**:
- `Linqraft.Core/SyntaxHelpers/TriviaHelper.cs` を新規作成
- Analyzer/Generator両方で出力されるコードのフォーマット（空白保持等）を正確に行う
- Trivia（空白、改行、コメント）処理の共通化

**影響箇所**:
- 全てのCodeFixProvider
- Generator内のコード生成箇所

**新規メソッド**:
```csharp
public static class TriviaHelper
{
    public static SyntaxNode PreserveTrivia(SyntaxNode original, SyntaxNode updated);
    public static SyntaxToken PreserveLeadingTrivia(SyntaxToken original, SyntaxToken updated);
    public static SyntaxToken PreserveTrailingTrivia(SyntaxToken original, SyntaxToken updated);
    public static SyntaxNode NormalizeWhitespace(SyntaxNode node, string indentation);
}
```

**期待効果**: コード品質向上、フォーマットの一貫性確保

---

### フェーズ3: Analyzer/CodeFix の共通化（中優先度）

#### 3-1. 基底Analyzerクラス作成
**目的**: DiagnosticDescriptor定義の統一

**作業内容**:
- `Linqraft.Core/AnalyzerHelpers/BaseLinqraftAnalyzer.cs` を新規作成
- 全Analyzer（7ファイル）を基底クラスに移行

**影響ファイル**:
- `AnonymousTypeToDtoAnalyzer.cs`
- `ApiControllerProducesResponseTypeAnalyzer.cs`
- `SelectExprToTypedAnalyzer.cs`
- `SelectToSelectExprAnonymousAnalyzer.cs`
- `SelectToSelectExprNamedAnalyzer.cs`
- `LocalVariableCaptureAnalyzer.cs`
- `TernaryNullCheckToConditionalAnalyzer.cs`

**期待効果**: コード削減 約100行、一貫性向上

---

#### 3-2. Analyzer共通ヘルパー作成
**目的**: Analyzer特有の処理の共通化

**作業内容**:
以下のヘルパーを新規作成:

1. **SyntaxHelper.cs** - 構文ヘルパー
```csharp
public static class SyntaxHelper
{
    public static Location GetMethodNameLocation(ExpressionSyntax expression);
    public static bool IsPartOfMemberAccess(IdentifierNameSyntax identifier);
}
```

2. **SyntaxGenerationHelper.cs** - 構文生成
```csharp
public static class SyntaxGenerationHelper
{
    public static ExpressionSyntax CreateTypedSelectExpr(ExpressionSyntax expression, string sourceTypeName, string dtoName);
}
```

3. **UsingDirectiveHelper.cs** - using追加（4箇所で重複）
```csharp
public static class UsingDirectiveHelper
{
    public static SyntaxNode AddUsingDirectiveForType(SyntaxNode root, ITypeSymbol typeSymbol);
}
```

4. **NullCheckHelper.cs** - null判定（3箇所で重複）
```csharp
public static class NullCheckHelper
{
    public static bool IsNullLiteral(ExpressionSyntax expr);
    public static bool IsNullOrNullCast(ExpressionSyntax expr);
    public static ExpressionSyntax RemoveNullableCast(ExpressionSyntax expr);
}
```

5. **CaptureHelper.cs** - キャプチャ判定（2箇所で100行重複★最大効果）
```csharp
public static class CaptureHelper
{
    public static HashSet<string> GetCapturedVariables(InvocationExpressionSyntax invocation);
    public static bool NeedsCapture(ISymbol symbol, LambdaExpressionSyntax lambda, ImmutableHashSet<string> lambdaParameters, SemanticModel semanticModel);
}
```

**影響ファイル**:
- 全CodeFixProvider (7ファイル)
- LocalVariableCaptureAnalyzer

**期待効果**: コード削減 約700～1,000行

---

#### 3-3. TernaryNullCheckSimplifier の統合
**目的**: null条件演算子変換の共通化

**作業内容**:
- `TernaryNullCheckSimplifier` を拡張
- 重複する呼び出しコード（2箇所）を削減

**影響箇所**:
- `SelectToSelectExprAnonymousCodeFixProvider.cs:255-292`
- `SelectToSelectExprNamedCodeFixProvider.cs:524-561`

**期待効果**: コード削減 約80行

---

### フェーズ4: その他の最適化（低優先度）

#### 4-1. SelectExprHelperの拡張
**目的**: SelectExpr判定ロジックの統一

**追加メソッド**:
```csharp
public static class SelectExprHelper
{
    // 既存メソッド
    public static bool IsSelectExprMethod(ISymbol symbol);
    public static bool IsSelectExprSyntax(ExpressionSyntax expression);

    // 新規追加
    public static bool IsSelectExprWithTypeArguments(ExpressionSyntax expression);
    public static bool IsSelectExprWithoutTypeArguments(ExpressionSyntax expression);
    public static Location GetSelectExprMethodNameLocation(ExpressionSyntax expression);
}
```

#### 4-2. その他のユーティリティ
- 名前空間取得処理の統合
- コメント除去などのユーティリティ統合

---

## 🚀 実施順序

### ステップ1（最優先）★実施中
**フェーズ1-2: 型判定処理のRoslyn API化**
- 理由: 型システムの正確性に直接影響
- リスク: 中
- 効果: 高

### ステップ2
**フェーズ1-1: フォーマッティング統一**
- 理由: 出力コードの品質向上
- リスク: 低
- 効果: 中

### ステップ3
**フェーズ2: 構文解析の共通化**
- 理由: コード削減効果が大きい
- リスク: 中
- 効果: 高

### ステップ4
**フェーズ3: Analyzer/CodeFix の共通化**
- 理由: 最大のコード削減効果
- リスク: 中～高
- 効果: 非常に高

### ステップ5
**フェーズ4: その他の最適化**
- 理由: 時間があれば実施
- リスク: 低
- 効果: 中

---

## ⚠️ リスクと注意点

### 1. Source Generator のキャッシュ問題
- リファクタリング中は常に `dotnet clean` を実行
- IDE再起動が必要になる可能性

### 2. テストの充実が必須
- 既存テストが全てパスすることを各フェーズで確認
- 特に型判定ロジックの変更は慎重に

### 3. 段階的な実施が重要
- フェーズごとにcommit
- 各フェーズ後に全テストを実行
- 問題があればrollback可能な状態を維持

### 4. 生成コードの後方互換性
- 出力されるコードが変わらないことを確認
- または、変更が改善であることを確認

---

## 📝 進捗状況

- [x] 事前調査完了
- [x] フェーズ1-2: 型判定処理のRoslyn API化
- [x] フェーズ1-1: フォーマッティング統一
- [x] フェーズ2-1: ラムダ式ヘルパー作成
- [x] フェーズ2-2: 式解析ヘルパー作成
- [x] フェーズ2-3: LINQメソッド検出ヘルパー作成
- [x] フェーズ2-4: 型情報ヘルパー作成
- [x] フェーズ2-5: Null条件演算子ヘルパー作成
- [x] フェーズ2-6: Trivia処理の高度化・共通化
- [x] フェーズ3-1: 基底Analyzerクラス作成
- [ ] フェーズ3-2: Analyzer共通ヘルパー作成
- [ ] フェーズ3-3: TernaryNullCheckSimplifier の統合
- [ ] フェーズ4: その他の最適化

---

## 📌 詳細調査結果

### 空白化・フォーマット関連の問題箇所

**SourceGenerator:**
- SelectExprInfo.cs:238 - `GeneratePropertyAssignment` メソッド
- SelectExprInfo.cs:474-486 - `ConvertDirectAnonymousTypeToDto` でインデント+4をハードコード
- SelectExprInfo.cs:508-546 - `ConvertNestedSelectWithRoslyn` で同様のパターン
- SelectExprInfoAnonymous.cs:136 - `string.Join($",\n", propertyAssignments)` で `\n` をハードコード
- SelectExprInfoExplicitDto.cs:348 - 同様のパターン
- SelectExprInfoNamed.cs:145 - 同様のパターン
- GenerateDtoClassInfo.cs:100, 110 - `new string(' ', i * 4)` で4スペース単位
- SelectExprGroups.cs:157-163 - `IndentUtility` メソッドで `\n` をハードコード

**Analyzer:**
- AnonymousTypeToDtoCodeFixProvider.cs:171 - `NormalizeWhitespace(eol: "\n")` を使用（唯一の正しい例）
- SelectExprToTypedCodeFixProvider.cs:124-137 - 改行文字の検出ロジック

### 文字列比較による型判定の問題箇所

**高優先度:**
1. GenerateDtoClassInfo.cs:152 - `var isTypeNullable = propertyType.EndsWith("?");`
2. GenerateDtoClassInfo.cs:194 - `if (prop.IsNullable && !propertyType.EndsWith("?"))`
3. SelectExprInfo.cs:170 - `if (typeDisplayString.Contains("IQueryable"))`
4. SelectExprInfo.cs:174 - `if (typeDisplayString.Contains("IEnumerable"))`
5. GenerateDtoClassInfo.cs:158 - `if (typeWithoutNullable.StartsWith("global::<anonymous"))`
6. GenerateDtoClassInfo.cs:169 - `else if (typeWithoutNullable.Contains("<"))`

**中優先度:**
7. DtoProperty.cs:30 - `if (string.IsNullOrWhiteSpace(typeName) || typeName == "?")`
8. SelectExprInfo.cs:265 - `if (expression.Contains("SelectMany"))`
9. SelectExprInfo.cs:289 - `if (convertedSelect == expression && expression.Contains("Select"))`
10. SelectExprGroups.cs:35 - `string.IsNullOrEmpty(targetNamespace) || targetNamespace.Contains("<")`
11. LocalVariableCaptureCodeFixProvider.cs:220 - `.Where(inv => inv.ToString().Contains("SelectExpr"))`

### 構文解析の重複パターン

**パターン1: ラムダパラメータ名の取得** (5箇所)
- SelectExprGenerator.cs:173-186
- LocalVariableCaptureAnalyzer.cs:207-218
- SelectExprInfo.cs:788-799
- LocalVariableCaptureCodeFixProvider.cs:362-374

**パターン2: プロパティ名の取得** (4箇所)
- DtoStructure.cs:188-214
- DtoNamingHelper.cs:107-118
- SelectExprInfo.cs:923-939
- AnonymousTypeToDtoCodeFixProvider.cs:569-580

**パターン3: IQueryable型の判定** (2箇所で完全重複)
- SelectToSelectExprAnonymousAnalyzer.cs:125-167
- SelectToSelectExprNamedAnalyzer.cs:125-167

**パターン4: ソース型の取得** (4箇所)
- SelectExprToTypedAnalyzer.cs:143-174
- SelectExprToTypedCodeFixProvider.cs:177-203
- SelectToSelectExprAnonymousCodeFixProvider.cs:205-231
- SelectToSelectExprNamedCodeFixProvider.cs:281-307

**パターン5: 匿名型の検索** (4箇所)
- SelectExprToTypedAnalyzer.cs:120-142
- SelectToSelectExprAnonymousAnalyzer.cs:169-191
- SelectExprToTypedCodeFixProvider.cs:205-225
- SelectToSelectExprAnonymousCodeFixProvider.cs:233-253

### Analyzer/CodeFixの重複パターン

**完全重複（最優先）:**
1. **NeedsCapture判定** - 2箇所で100行完全重複
   - LocalVariableCaptureAnalyzer.cs:425-526
   - LocalVariableCaptureCodeFixProvider.cs:640-739

2. **キャプチャ変数の取得** - 2箇所で60行重複
   - LocalVariableCaptureAnalyzer.cs:111-177
   - LocalVariableCaptureCodeFixProvider.cs:273-335

3. **IQueryable判定** - 2箇所で40行完全重複
   - SelectToSelectExprAnonymousAnalyzer.cs:125-167
   - SelectToSelectExprNamedAnalyzer.cs:125-167

4. **using ディレクティブの追加** - 4箇所で重複
   - ApiControllerProducesResponseTypeCodeFixProvider.cs:298-314
   - SelectExprToTypedCodeFixProvider.cs:108-140
   - SelectToSelectExprAnonymousCodeFixProvider.cs:302-334
   - SelectToSelectExprNamedCodeFixProvider.cs:563-595

---

**最終更新**: 2025-11-21
