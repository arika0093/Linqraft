* DON'T USE `SelectExprRuntimeHelper`
  * 全てのロジックはインターセプトされた中身にinlineで書かれるべきである(see README.md / Overview section)
  * SelectExprRuntimeHelperに移譲するやり方はIQueryableに対応しておらず、Linqraftの目的に反する
  * SelectExprRuntimeHelperは削除する.

* インターセプトされるロジックの中身を綺麗にフォーマットする
  * DON'T: `query.Select(x => new FooBar { y = x.y.Select(y => new { ... }) })`
  * DO:
```csharp
query.Select(x => new FooBar {
    y = x.y
        .Select(y => new {
            // ...
        }),
    z = x.z,
})
```

* LinqraftSG001/LinqraftSG002のアナライザー自体を削除
* Linqraft.Support.g.cs → Linqraft.Declarations.g.csにリネーム
* LinqraftCaptureHelperを削除(inlineで書く)
* 