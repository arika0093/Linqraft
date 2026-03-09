* インデントが壊れてる。.generatedファイルを全て見て、インデントが壊れてる箇所を全て修正してください
```csharp
// SelectExpr_5149BB84729D0008
[global::System.Runtime.CompilerServices.InterceptsLocationAttribute(1, "LRNIE4/zY5C1YPJdENHuTMAkAABFeHBsaWNpdER0b0NvbXByZWhlbnNpdmVUZXN0LmNz")]
public static global::System.Linq.IQueryable<TResult> SelectExpr_5149BB84729D0008<TIn, TResult>(this global::System.Linq.IQueryable<TIn> query, global::System.Func<TIn, object> selector) where TIn : class
{
    // 不要な空行

    var converted = ((global::System.Linq.IQueryable<global::Linqraft.Tests.ExplicitDtoComprehensiveTest.EntityWithNullableChild>)(object)query).Select(e => new global::Linqraft.Tests.TwoLevelNullConditionalDto() {
    // 略
    });
}

// SelectExpr_9180A98084AC6E14
        public static global::System.Linq.IQueryable<TResult> SelectExpr_9180A98084AC6E14<TIn, TResult>(this global::System.Linq.IQueryable<TIn> query, global::System.Func<TIn, TResult> selector, [global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicProperties)] object capture) where TIn : class
        {

            var captureType = capture.GetType();
            var __linqraft_capture_0_valProperty = captureType.GetProperty("val", global::System.Reflection.BindingFlags.Instance | global::System.Reflection.BindingFlags.Public);
            if (__linqraft_capture_0_valProperty is null)
            {
                throw new global::System.InvalidOperationException("Captured value 'val' was not found.");
            }
        var __linqraft_capture_0_valValue = __linqraft_capture_0_valProperty.GetValue(capture);
        var __linqraft_capture_0_val = __linqraft_capture_0_valValue is null ? default! : (global::System.Int32)__linqraft_capture_0_valValue;
        var __linqraft_capture_1_multiplierProperty = captureType.GetProperty("multiplier", global::System.Reflection.BindingFlags.Instance | global::System.Reflection.BindingFlags.Public);
        if (__linqraft_capture_1_multiplierProperty is null)
        {
            throw new global::System.InvalidOperationException("Captured value 'multiplier' was not found.");
        }
    var __linqraft_capture_1_multiplierValue = __linqraft_capture_1_multiplierProperty.GetValue(capture);
    var __linqraft_capture_1_multiplier = __linqraft_capture_1_multiplierValue is null ? default! : (global::System.Int32)__linqraft_capture_1_multiplierValue;

    var converted = ((global::System.Linq.IQueryable<global::Linqraft.Tests.TestItem>)(object)query).Select(x => new global::Linqraft.Tests.PredefinedDto2() {
        Id = x.Id,
        NewValue = x.Value + __linqraft_capture_0_val,
        DoubledValue = x.Value * __linqraft_capture_1_multiplier,
    });
return (global::System.Linq.IQueryable<TResult>)(object)converted;
}
}
}
```

* analyzer側のテストケース+codefixテストをより充実させてください
* EFCoreのテストをより充実させる。またファイルを適宜分割すること。
* examples配下が実行できることを確認する。