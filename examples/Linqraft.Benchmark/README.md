# Linqraft Performance Benchmark

このプロジェクトは、Linqraftを使用した場合と従来のEF Core `.Select`を使用した場合のパフォーマンス比較を行うベンチマークです。

## ベンチマーク内容

以下の2つの方法を比較します:

1. **TraditionalSelect (ベースライン)**: 従来のEF Core `.Select`を使用し、手動で定義したDTOクラスにマッピング
   - 手動でDTOクラスを定義する必要がある
   - null条件演算子が使えないため、三項演算子で冗長なnullチェックを記述

2. **LinqraftSelectExpr**: Linqraftの`.SelectExpr`を使用し、自動生成されたDTOクラスにマッピング
   - DTOクラスは自動生成される
   - null条件演算子(`?.`)が使用可能で、簡潔なコード記述が可能

## 実行方法

### 前提条件
- .NET 9.0 SDK以降

### ベンチマーク実行

```bash
cd examples/Linqraft.Benchmark
dotnet run -c Release
```

## ベンチマーク結果の見方

BenchmarkDotNetは以下の情報を提供します:

- **Mean**: 平均実行時間
- **Error**: 測定誤差
- **StdDev**: 標準偏差
- **Ratio**: ベースラインとの比率
- **Gen0, Gen1, Gen2**: ガベージコレクションの発生回数
- **Allocated**: 割り当てられたメモリ量

## テストデータ

ベンチマークでは100件のサンプルデータを使用します。各レコードは:
- 親クラス (SampleClass)
- 子クラスのリスト (2件のSampleChildClass)
- nullableな子クラス (SampleChildClass2) - 50%の確率で存在
- 必須の子クラス (SampleChildClass3)
- さらにネストされた子クラス (SampleChildChildClass, SampleChildChildClass2)

を含みます。
