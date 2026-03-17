# Linqraft カスタム投影挙動の拡張方式案

## 背景
- `AsLeftJoin()` のような「実行時には不要だが、source generator の出力挙動だけを変えたい」ダミー API を今後も増やしたい。
- 個別実装を generator 本体へ都度ベタ書きすると、構文解析・型推論・式出力の分岐が増え、保守コストが上がる。

## 目標
- ユーザーが独自のダミー拡張メソッドを導入し、そのメソッドに応じて Linqraft の自動生成結果を変更できるようにする。
- 既存の `SelectExpr` / `LinqraftMappingGenerate` / `Generate` の仕組みを大きく壊さず段階導入できるようにする。

## 推奨方針

### 1. 「ダミー拡張メソッド + 変換ルール」を1セットで登録する
- ユーザーは通常の拡張メソッドを用意する。
- そのメソッドに対して、generator 側では「見つけたらどのように式を書き換えるか」を別途登録する。
- 例:
  - `AsLeftJoin()` → 後続のメンバーアクセスを null ガード付き条件式へ変換
  - `AsInnerJoin()` → 明示的に通常展開
  - `AsServerOnly()` → 特定メソッドだけクライアント評価禁止

### 2. 登録単位は string ベースにする
- `IMethodSymbol` ベースにするとユーザー拡張側が Roslyn 参照を持つ必要が出るため、公開 API は string ベースで完結させる。
- 1 拡張につき 1 クラスを作り、クラス側に `[LinqraftExtensions(メソッド名)]` を付けて、内部の abstract class override で generator 用の設定値を書く。
- generator 側ではその宣言を事前収集し、登録されたメソッド名だけを特別扱いする。

### 3. generator には「式変換ルール」キーを設ける
- 内部実装として、たとえば以下の責務を持つルールを用意する。
  - `BehaviorKey` の文字列で標準変換を選ぶ
  - `MethodDeclarations` の文字列定義からダミー拡張メソッドを自動生成する
- `AsLeftJoin` はこの最初の標準実装として `BehaviorKey = "AsLeftJoin"` で扱う。
- 将来的に `ProjectionTemplateBuilder` と `ProjectionExpressionEmitter` の if 文を増やす代わりに、登録済み拡張情報から適用対象を判定する。

### 4. ユーザー向けの登録方法は `Linqraft.QueryExtensions` パッケージで始める
- `Linqraft.QueryExtensions` に `LinqraftExtensionsAttribute` / `LinqraftExtensionDeclaration` / 標準の `AsLeftJoin` 宣言を置く。
- source generator はコンパイル対象と参照アセンブリから `[LinqraftExtensions]` を拾い、`MethodDeclarations` をもとにダミー拡張メソッドを自動生成する。
- これによりユーザーは runtime 実装を持たずに「generator だけが解釈する拡張メソッド」を定義できる。

## 段階導入案

### Phase 1
- generator 内部に string ベースの拡張レジストリを作る。
- `Linqraft.QueryExtensions` パッケージを作り、標準登録として `AsLeftJoin` を載せる。
- 登録された拡張だけダミーメソッドを生成する。

### Phase 2
- 登録用 attribute / base class を公開する。
- ユーザー定義のダミー拡張メソッドを string ベース設定で解決できるようにする。

### Phase 3
- 型変換ルールと値変換ルールを分離する。
- 例:
  - nullability だけ変える
  - SQL 変換専用ヒントを付ける
  - 特定メソッドを標準 LINQ へ正規化する

## 注意点
- 生成後 C# が常に通常の LINQ / C# 式へ落ちることを維持する。実行時に専用ランタイムは増やさない。
- ルール適用後の型 nullability と値式の整合性を必ず取る。
- `IQueryable` と `IEnumerable` で挙動差が出る場合は、ルール側で receiver kind を見て分岐できるようにする。
- NativeAOT を壊さないよう、反射依存の登録方式は避け、Roslyn から静的に読める宣言を優先する。

## まとめ
- 今回の `AsLeftJoin()` は「ダミー API を見つけたら generator が式を正規化する」方式の最小例として扱う。
- 将来拡張を考えると、個別ハードコードを増やすよりも「`[LinqraftExtensions]` で登録された string ベース定義を読む」方向に寄せるのが保守しやすい。
