# Linqraft Performance Benchmark

このプロジェクトは、Linqraftを使用した場合と従来のEF Core `.Select`を使用した場合のパフォーマンス比較を行うベンチマークです。

## ベンチマーク結果

**📊 [詳細なベンチマーク結果を見る](./BENCHMARK_RESULTS.md)**

### 結果サマリー

| Method                        | Mean     | Ratio | Allocated |
|------------------------------ |---------:|------:|----------:|
| Traditional Manual DTO        | 1.635 ms |  0.89 | 244.79 KB |
| **Linqraft Auto-Generated DTO** | **1.651 ms** |  **0.90** | **245.23 KB** |
| Linqraft Anonymous            | 1.778 ms |  0.97 | 244.41 KB |
| Traditional Anonymous         | 1.834 ms |  1.00 | 245.99 KB |

**主な発見:**
- ✅ Linqraftの自動生成DTOは従来の手動DTOと**ほぼ同じパフォーマンス** (差は0.98%)
- ✅ メモリ割り当ても**ほぼ同じ** (~245 KB)
- ✅ パフォーマンスのペナルティなしで、より読みやすいコードを実現

## ベンチマーク内容

以下の4つのパターンを比較します:

1. **Traditional Anonymous** (従来 - 匿名型): 冗長なnullチェックが必要
2. **Traditional Manual DTO** (従来 - 手動DTO): 手動でDTOを定義 + 冗長なnullチェック
3. **Linqraft Anonymous** (Linqraft - 匿名型): null条件演算子が使用可能
4. **Linqraft Auto-Generated DTO** (Linqraft - 自動生成DTO): DTOクラスが自動生成 + null条件演算子

### コード比較

**従来の方法 (冗長なnullチェック)**:
```csharp
ChildId = c.Child != null ? c.Child.Id : null,
Child3ChildId = s.Child3 != null && s.Child3.Child != null ? s.Child3.Child.Id : null,
```

**Linqraft使用 (簡潔なnull条件演算子)**:
```csharp
ChildId = c.Child?.Id,
Child3ChildId = s.Child3?.Child?.Id,
```

## 実行方法

### 前提条件
- .NET 9.0 SDK以降

### テスト実行（動作確認）

まず、4つのパターンが正しく動作することを確認:
```bash
cd examples/Linqraft.Benchmark
dotnet run --test
```

### ベンチマーク実行

実際のパフォーマンス測定:
```bash
cd examples/Linqraft.Benchmark
dotnet run -c Release
```

結果は `BenchmarkDotNet.Artifacts/results/` に保存されます。

## テストデータ

ベンチマークでは100件のサンプルデータを使用します。各レコードは:
- 親クラス (SampleClass)
- 子クラスのリスト (2件のSampleChildClass)
- nullableな子クラス (SampleChildClass2) - 50%の確率で存在
- 必須の子クラス (SampleChildClass3)
- さらにネストされた子クラス (SampleChildChildClass, SampleChildChildClass2)

を含みます。


