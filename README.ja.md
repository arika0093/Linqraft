# <img width="24" src="./assets/linqraft.png" /> Linqraft

[![NuGet Version](https://img.shields.io/nuget/v/Linqraft?style=flat-square&logo=NuGet&color=0080CC)](https://www.nuget.org/packages/Linqraft/) ![GitHub Actions Workflow Status](https://img.shields.io/github/actions/workflow/status/arika0093/Linqraft/test.yaml?branch=main&label=Test&style=flat-square) [![DeepWiki](https://img.shields.io/badge/DeepWiki-arika0093%2FLinqraft-blue.svg?logo=data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAACwAAAAyCAYAAAAnWDnqAAAAAXNSR0IArs4c6QAAA05JREFUaEPtmUtyEzEQhtWTQyQLHNak2AB7ZnyXZMEjXMGeK/AIi+QuHrMnbChYY7MIh8g01fJoopFb0uhhEqqcbWTp06/uv1saEDv4O3n3dV60RfP947Mm9/SQc0ICFQgzfc4CYZoTPAswgSJCCUJUnAAoRHOAUOcATwbmVLWdGoH//PB8mnKqScAhsD0kYP3j/Yt5LPQe2KvcXmGvRHcDnpxfL2zOYJ1mFwrryWTz0advv1Ut4CJgf5uhDuDj5eUcAUoahrdY/56ebRWeraTjMt/00Sh3UDtjgHtQNHwcRGOC98BJEAEymycmYcWwOprTgcB6VZ5JK5TAJ+fXGLBm3FDAmn6oPPjR4rKCAoJCal2eAiQp2x0vxTPB3ALO2CRkwmDy5WohzBDwSEFKRwPbknEggCPB/imwrycgxX2NzoMCHhPkDwqYMr9tRcP5qNrMZHkVnOjRMWwLCcr8ohBVb1OMjxLwGCvjTikrsBOiA6fNyCrm8V1rP93iVPpwaE+gO0SsWmPiXB+jikdf6SizrT5qKasx5j8ABbHpFTx+vFXp9EnYQmLx02h1QTTrl6eDqxLnGjporxl3NL3agEvXdT0WmEost648sQOYAeJS9Q7bfUVoMGnjo4AZdUMQku50McDcMWcBPvr0SzbTAFDfvJqwLzgxwATnCgnp4wDl6Aa+Ax283gghmj+vj7feE2KBBRMW3FzOpLOADl0Isb5587h/U4gGvkt5v60Z1VLG8BhYjbzRwyQZemwAd6cCR5/XFWLYZRIMpX39AR0tjaGGiGzLVyhse5C9RKC6ai42ppWPKiBagOvaYk8lO7DajerabOZP46Lby5wKjw1HCRx7p9sVMOWGzb/vA1hwiWc6jm3MvQDTogQkiqIhJV0nBQBTU+3okKCFDy9WwferkHjtxib7t3xIUQtHxnIwtx4mpg26/HfwVNVDb4oI9RHmx5WGelRVlrtiw43zboCLaxv46AZeB3IlTkwouebTr1y2NjSpHz68WNFjHvupy3q8TFn3Hos2IAk4Ju5dCo8B3wP7VPr/FGaKiG+T+v+TQqIrOqMTL1VdWV1DdmcbO8KXBz6esmYWYKPwDL5b5FA1a0hwapHiom0r/cKaoqr+27/XcrS5UwSMbQAAAABJRU5ErkJggg==)](https://deepwiki.com/arika0093/Linqraft)

EntityFrameworkCore(EFCore)におけるSelectクエリの記述を簡潔にし、DTOクラスの自動生成・nullable式のサポートを提供します。

[English](./README.md) | [Japanese](./README.ja.md)

## 課題
EFCoreにおいて、関連テーブルが大量にあるテーブルのデータを取得する例を考えます。

`Include`や`ThenInclude`を使用する方法は、すぐにコードが複雑になり可読性が低下します。  
また、Includeを忘れると実行時に`NullReferenceException`が発生する上、それを検知することは難しい難点があります。  
さらに、全てのデータを取得する関係上、パフォーマンス上でも問題があります。

```csharp
// ⚠️ unreadable, inefficient, and error-prone
var orders = await dbContext.Orders
    .Include(o => o.Customer)
        .ThenInclude(c => c.Address)
            .ThenInclude(a => a.Country)
    .Include(o => o.Customer)
        .ThenInclude(c => c.Address)
            .ThenInclude(a => a.City)
    .Include(o => o.OrderItems)
        .ThenInclude(oi => oi.Product)
    .ToListAsync();
```

より理想的な方法はDTO(Data Transfer Object)を使用し、必要なデータのみを選択的に取得することです。

```csharp
// ✅️ readable and efficient
var orders = await dbContext.Orders
    .Select(o => new OrderDto
    {
        Id = o.Id,
        CustomerName = o.Customer.Name,
        CustomerCountry = o.Customer.Address.Country.Name,
        CustomerCity = o.Customer.Address.City.Name,
        Items = o.OrderItems.Select(oi => new OrderItemDto
        {
            ProductName = oi.Product.Name,
            Quantity = oi.Quantity
        }).ToList()
    })
    .ToListAsync();
```

上記の方法は、必要なデータのみを取得できるためパフォーマンス上大きな利点があります。
しかし、以下のような欠点があります。

* 匿名型を利用することは可能ですが、他の関数に渡す場合や返却値として使用する場合、手動でDTOクラスを定義する必要があります。
* nullableなプロパティを持つ子オブジェクトが存在する場合、三項演算子を駆使した冗長なコードを書く必要があります。

Expression内ではnullable演算子が利用できない性質上、`o.Customer?.Name`のような記述ができず、以下のようなコードになりがちです。

```csharp
// 🤔 too ugly code with lots of null checks
var orders = await dbContext.Orders
    .Select(o => new OrderDto
    {
        Id = o.Id,
        CustomerName = o.Customer != null ? o.Customer.Name : null,
        CustomerCountry = o.Customer != null && o.Customer.Address != null && o.Customer.Address.Country != null
            ? o.Customer.Address.Country.Name
            : null,
        CustomerCity = o.Customer != null && o.Customer.Address != null && o.Customer.Address.City != null
            ? o.Customer.Address.City.Name
            : null,
        Items = o.OrderItems != null
            ? o.OrderItems.Select(oi => new OrderItemDto
            {
                ProductName = oi.Product != null ? oi.Product.Name : null,
                Quantity = oi.Quantity
            }).ToList()
            : new List<OrderItemDto>()
    })
    .ToListAsync();

// 🤔 you must define DTO classes manually
public class OrderDto
{
    public int Id { get; set; }
    public string? CustomerName { get; set; }
    public string? CustomerCountry { get; set; }
    public string? CustomerCity { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();
}
public class OrderItemDto
{
    public string? ProductName { get; set; }
    public int Quantity { get; set; }
}
```

## 特徴
Linqraftは、上記の問題を解決するために設計されたSource Generatorです。
上記の例では、以下のように記述することができます。

```csharp
using Linqraft;

// ✨️ auto-generated DTOs, with null-propagation support
var orders = await dbContext.Orders
    // Order: input entity type
    // OrderDto: output DTO type (auto-generated)
    .SelectExpr<Order, OrderDto>(o => new
    {
        Id = o.Id,
        CustomerName = o.Customer?.Name,
        CustomerCountry = o.Customer?.Address?.Country?.Name,
        CustomerCity = o.Customer?.Address?.City?.Name,
        Items = o.OrderItems?.Select(oi => new
        {
            ProductName = oi.Product?.Name,
            Quantity = oi.Quantity
        }) ?? [],
    })
    .ToListAsync();
```

`SelectExpr`のジェネリクス引数に`OrderDto`を指定することで、関連するDTOクラスを自動生成します。
匿名型セレクターから自動的にコードを生成するため、`OrderDto`や`OrderItemDto`を手動で定義する必要はありません。
例えば、上記の例では以下のようなメソッドおよびクラスが生成されます。

<details>
<summary>生成されたコード例</summary>

```csharp
// <auto-generated />
#nullable enable
#pragma warning disable IDE0060
#pragma warning disable CS8601
#pragma warning disable CS8603
#pragma warning disable CS8604

using System;
using System.Linq;
using System.Collections.Generic;
using Tutorial;

namespace Linqraft
{
    file static partial class GeneratedExpression
    {
        [global::System.Runtime.CompilerServices.InterceptsLocationAttribute(1, "3qqsORkQIsffTvt853DkMxcEAABUdXRvcmlhbENhc2VUZXN0LmNz")]
        public static IQueryable<TResult> SelectExpr_E6FDF286_87D91E16<TIn, TResult>(
            this IQueryable<TIn> query,
            Func<TIn, object> selector) where TResult : global::Tutorial.OrderDto
        {
            var matchedQuery = query as object as IQueryable<global::Tutorial.Order>;
            var converted = matchedQuery.Select(s => new global::Tutorial.OrderDto
            {
                Id = s.Id,
                CustomerName = s.Customer != null ? (string?)s.Customer.Name : null,
                CustomerCountry = s.Customer != null && s.Customer.Address != null && s.Customer.Address.Country != null ? (string?)s.Customer.Address.Country.Name : null,
                CustomerCity = s.Customer != null && s.Customer.Address != null && s.Customer.Address.City != null ? (string?)s.Customer.Address.City.Name : null,
                Items = s.OrderItems != null ? s.OrderItems.Select(oi => new OrderItemDto_DE33EA40 {
                    ProductName = oi.Product != null ? (string?)oi.Product.Name : null,
                    Quantity = oi.Quantity,
                }) : System.Linq.Enumerable.Empty<OrderItemDto_DE33EA40>()
            });
            return converted as object as IQueryable<TResult>;
        }

    }
}

namespace Tutorial
{
    public partial class OrderItemDto_DE33EA40
    {
        public required string? ProductName { get; set; }
        public required int Quantity { get; set; }
    }

    public partial class OrderDto
    {
        public required int Id { get; set; }
        public required string? CustomerName { get; set; }
        public required string? CustomerCountry { get; set; }
        public required string? CustomerCity { get; set; }
        public required global::System.Collections.Generic.IEnumerable<Tutorial.OrderItemDto_DE33EA40>? Items { get; set; }
    }
}
```

</details>

## 使用方法
### 前提
このライブラリは内部的に [C# interceptors](https://learn.microsoft.com/ja-jp/dotnet/csharp/whats-new/csharp-12#interceptors) を使用しているため、**C# 12以降**を使用する必要があります。  

<details>
<summary>.NET 7以下の場合に必要な設定</summary>

`LangVersion`プロパティを設定し、[Polysharp](https://github.com/Sergio0694/PolySharp/)を使用してC#の最新機能を有効にしてください。

```xml
<Project>
    <PropertyGroup>
        <LangVersion>12.0</LangVersion>
    </PropertyGroup>
    <ItemGroup>
        <PackageReference Include="Polysharp" Version="1.*" />
    </ItemGroup>
</Project>
```

</details>

### インストール
`Linqraft`をNuGetからインストールします。

```bash
dotnet add package Linqraft
```

## 利用例
### Anonymous pattern
`SelectExpr`をジェネリクス無しで使用すると、匿名型が返されます。

```csharp
var orders = await dbContext.Orders
    .SelectExpr(o => new
    {
        Id = o.Id,
        CustomerName = o.Customer?.Name,
        // ...
    })
    .ToListAsync();
```

### Explicit DTO pattern
結果をDTOクラスに変更したい場合は、以下のようにジェネリクスを指定するだけです。

```csharp
var orders = await dbContext.Orders
    // Order: input entity type
    // OrderDto: output DTO type (auto-generated)
    .SelectExpr<Order, OrderDto>(o => new
    {
        Id = o.Id,
        CustomerName = o.Customer?.Name,
        // ...
    })
    .ToListAsync();
```

DTOクラスは`partial`で出力されるため、必要に応じて拡張することもできます。
```csharp
// extend generated DTO class if needed
public partial class OrderDto
{
    public string GetDisplayName() => $"{Id}: {CustomerName}";
}
```

`IEnumerable`型についても同様に記述することで、DTO自動生成機能のみを利用することも可能です。

```csharp
var orders = MySampleList
    .SelectExpr<Order, OrderDto>(o => new
    {
        Id = o.Id,
        CustomerName = o.Customer?.Name,
        // ...
    })
    .ToList();
```


> [!TIP]
> 自動生成された型情報を利用したい場合、OrderDtoクラスにカーソルを合わせてF12キーを押すと、生成されたコードにジャンプします。
> あとはコピーするなどして自由に利用できます。

### Pre-existing DTO pattern
自動生成機能を使用せず、既存のDTOクラスを利用することも可能です。この場合、ジェネリクス引数を指定せずに使用する必要があります。

```csharp
var orders = await dbContext.Orders
    .SelectExpr(o => new OrderDto
    {
        Id = o.Id,
        CustomerName = o.Customer?.Name,
        // ...
    })
    .ToListAsync();

// your existing DTO class
public class OrderDto { /* ... */ }
```

## パフォーマンス

<details>
<summary>ベンチマーク結果</summary>

```
BenchmarkDotNet v0.15.7, Windows 11 (10.0.26200.7171/25H2/2025Update/HudsonValley2)
Intel Core i7-14700F 2.10GHz, 1 CPU, 28 logical and 20 physical cores
.NET SDK 10.0.100-rc.2.25502.107
  [Host]     : .NET 9.0.10 (9.0.10, 9.0.1025.47515), X64 RyuJIT x86-64-v3
  DefaultJob : .NET 9.0.10 (9.0.10, 9.0.1025.47515), X64 RyuJIT x86-64-v3

| Method                        | Mean       | Error    | StdDev   | Ratio | RatioSD | Rank | Gen0    | Gen1   | Allocated | Alloc Ratio |
|------------------------------ |-----------:|---------:|---------:|------:|--------:|-----:|--------:|-------:|----------:|------------:|
| 'Traditional Manual DTO'      |   962.2 us |  7.11 us |  6.65 us |  0.92 |    0.01 |    1 | 13.6719 | 1.9531 | 245.06 KB |        1.00 |
| 'Linqraft Auto-Generated DTO' |   968.6 us |  7.40 us |  6.92 us |  0.92 |    0.01 |    1 | 13.6719 | 1.9531 | 245.09 KB |        1.00 |
| 'Linqraft Anonymous'          | 1,030.7 us |  4.64 us |  4.34 us |  0.98 |    0.01 |    2 | 13.6719 | 1.9531 | 244.92 KB |        1.00 |
| 'Traditional Anonymous'       | 1,047.7 us | 16.51 us | 15.44 us |  1.00 |    0.02 |    2 | 13.6719 | 1.9531 | 246.14 KB |        1.00 |
```


</details>

手動で定義した場合とLinqraftの性能はほぼ同等です。詳細については、[Linqraft.Benchmark](./examples/Linqraft.Benchmark)をご覧ください。

## トラブルシューティング
### CS8072 Error
変更直後にビルドを行うと エラー `CS8072`(式ツリーのラムダに null 伝搬演算子を含めることはできません) が発生する場合があります。
この場合、プロジェクトをリビルドすると解決します。
もし解決しない場合、生成されたソースコードに誤ってnull伝搬演算子が含まれている可能性があります。その場合はお気軽にIssueを立ててください！

## 注意事項
`.SelectExpr`内の匿名型を`.Select`対応のものに置き換える処理は力技で行っているため、複雑な式や一部のC#構文に対応していない場合があります。
それらのケースに遭遇した際には、Issueを立てていただけると幸いです。

## ライセンス
このプロジェクトはApache License 2.0の下でライセンスされています。
