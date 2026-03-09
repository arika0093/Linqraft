* DON'T USE `SelectExprRuntimeHelper`
  * 全てのロジックはインターセプトされた中身にinlineで書かれるべきである(see README.md / Overview section)
  * SelectExprRuntimeHelperに移譲するやり方はIQueryableに対応しておらず、Linqraftの目的に反する
  * SelectExprRuntimeHelperは削除する.

* インターセプトされるロジックの中身を綺麗にフォーマットする. 1行で書くのではなく、適切にインデントして書くべき.
  * DON'T: `query.Select(x => new FooBar { y = x.y.Select(y => new { ... }) })`
  * DO:
```csharp
query.Select(x => new FooBar {
    y = x.y
        // LINQクエリは1つインデントして書く
        .Select(y => new {
            // 中身もさらにインデントして、きれいに出力する
        }),
    z = x.z,
})
```

* LinqraftSG001/LinqraftSG002のアナライザー自体を削除
* Linqraft.Support.g.cs → Linqraft.Declarations.g.csにリネーム
* LinqraftCaptureHelperを削除, inlineで書く


* テストをXUnitからTUnitに変更
  * NativeAOTでビルド→テストを追加(GH Actions)
  * SourceGeneratorのキャッシュが効いていること+単純なSmokeテストするLinqraft.Tests.SGプロジェクトを追加
  
* EFCore(SQLite)を使用したテスト Linqraft.Tests.EFCoreプロジェクトを追加
  * 単純なケースからはじめ、大規模なテストケース(各種LINQクエリ、大規模なデータセット、TPH等に対するOfTypeのテストなど)を追加していく
* Linqraft.Tests内のPreviousテストを整理して、Linqraft.Testルートに移動
  * 重複したテスト項目を整理
