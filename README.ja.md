# EFCore.ExprGenerator

EntityFrameworkCore(EFCore)におけるSelectクエリの記述を簡潔にし、nullable式を利用できるようにします。

[English](./README.md) | [Japanese](./README.ja.md)

## 課題
EFCoreにおいて、関連テーブルが大量にあるテーブルのデータを取得する例を考えます。

`Include`や`ThenInclude`を使用する方法は、すぐにコードが複雑になり可読性が低下します。
また、Includeを忘れると実行時に`NullReferenceException`が発生する上、それを検知することは難しい難点があります。
さらに、全てのデータを取得する関係上、パフォーマンス上でも問題があります。

```csharp
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
```

## 特徴
EFCore.ExprGeneratorは、上記の問題を解決するために設計されたSource Generatorです。
上記の例では、以下のように記述することができます。

```csharp
var orders = await dbContext.Orders
    .SelectExpr(o => new
    {
        Id = o.Id,
        CustomerName = o.Customer?.Name,
        CustomerCountry = o.Customer?.Address?.Country?.Name,
        CustomerCity = o.Customer?.Address?.City?.Name,
        Items = o.OrderItems?.Select(oi => new
        {
            ProductName = oi.Product?.Name,
            Quantity = oi.Quantity
        })
    })
    .ToListAsync();
```

`OrderDto`や`OrderItemDto`などを定義していないことに注目してください。
Source Generatorにより、上記のコードから以下のようなメソッドが自動生成されます。

<details>
<summary>生成されたコード例</summary>

```csharp
namespace EFCore.ExprGenerator.Sample;
internal static class GeneratedExpression
{
    /// <summary>
    /// generated method
    /// </summary>
    public static IQueryable<OrderDto_D03CE9AC> SelectExpr<TResult>(
        this IQueryable<Order> query,
        Func<Order, TResult> selector)
    {
        return query.Select(s => new OrderDto_D03CE9AC
        {
            Id = s.Id,
            CustomerName = s.Customer != null ? s.Customer.Name : default,
            CustomerCountry = s.Customer != null && s.Customer.Address != null && s.Customer.Address.Country != null
                ? s.Customer.Address.Country.Name
                : default,
            CustomerCity = s.Customer != null && s.Customer.Address != null && s.Customer.Address.City != null
                ? s.Customer.Address.City.Name
                : default,
            Items = s.OrderItems != null
                ? s.OrderItems.Select(oi => new OrderItemDto_34ADD7E8
                {
                    ProductName = oi.Product != null ? oi.Product.Name : default,
                    Quantity = oi.Quantity
                })
                : default,
        });
    }
}

public class OrderItemDto_34ADD7E8
{
    public required string? ProductName { get; set; }
    public required int Quantity { get; set; }
}

public class OrderDto_D03CE9AC
{
    public required int Id { get; set; }
    public required string? CustomerName { get; set; }
    public required string? CustomerCountry { get; set; }
    public required string? CustomerCity { get; set; }
    public required IEnumerable<OrderItemDto_34ADD7E8> Items { get; set; }
}
```

</details>

## 使用方法
### インストール
`EFCore.ExprGenerator`をNuGetからインストールします。

```
-dotnet add package EFCore.ExprGenerator --prerelease
```

### 利用例
以下のように`SelectExpr`メソッドを使用して、匿名型でクエリを記述します。

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

## ライセンス
このプロジェクトはApache License 2.0の下でライセンスされています。
