using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Linq;

namespace Linqraft.Core.RoslynHelpers;

/// <summary>
/// Roslynのセマンティック解析を用いた型判定ヘルパー
/// 文字列比較による型判定を置き換え、より正確で堅牢な型判定を提供します。
/// </summary>
public static class RoslynTypeHelper
{
    /// <summary>
    /// 型がnullable型かどうかを判定
    /// </summary>
    /// <param name="typeSymbol">判定する型シンボル</param>
    /// <returns>nullable型の場合true</returns>
    public static bool IsNullableType(ITypeSymbol typeSymbol)
    {
        if (typeSymbol == null)
            return false;

        // Nullable<T> (値型のnullable)
        if (typeSymbol.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
            return true;

        // C# 8.0+ の nullable reference types
        return typeSymbol.NullableAnnotation == NullableAnnotation.Annotated;
    }

    /// <summary>
    /// 型名文字列からnullable判定を行う（レガシーメソッド）
    /// 可能な限り IsNullableType(ITypeSymbol) を使用してください
    /// </summary>
    /// <param name="typeName">型名文字列</param>
    /// <returns>型名が"?"で終わる場合true</returns>
    public static bool IsNullableTypeByString(string typeName)
    {
        return !string.IsNullOrEmpty(typeName) && typeName.EndsWith("?");
    }

    /// <summary>
    /// 型からnullable修飾子を除去
    /// </summary>
    /// <param name="typeSymbol">型シンボル</param>
    /// <returns>nullableでない型シンボル</returns>
    public static ITypeSymbol? GetNonNullableType(ITypeSymbol? typeSymbol)
    {
        if (typeSymbol == null)
            return null;

        // Nullable reference types の場合
        if (typeSymbol.NullableAnnotation == NullableAnnotation.Annotated)
        {
            return typeSymbol.WithNullableAnnotation(NullableAnnotation.NotAnnotated);
        }

        // Nullable<T> (値型) の場合は基底型を返す
        if (typeSymbol is INamedTypeSymbol namedType
            && namedType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
            && namedType.TypeArguments.Length == 1)
        {
            return namedType.TypeArguments[0];
        }

        return typeSymbol;
    }

    /// <summary>
    /// 型名文字列から"?"を除去（レガシーメソッド）
    /// </summary>
    /// <param name="typeName">型名文字列</param>
    /// <returns>"?"を除去した型名</returns>
    public static string RemoveNullableSuffixFromString(string typeName)
    {
        if (string.IsNullOrEmpty(typeName))
            return typeName;

        return typeName.TrimEnd('?');
    }

    /// <summary>
    /// 型がIQueryable&lt;T&gt;を実装しているかを判定
    /// </summary>
    /// <param name="typeSymbol">判定する型シンボル</param>
    /// <param name="compilation">コンパイレーション</param>
    /// <returns>IQueryable&lt;T&gt;を実装している場合true</returns>
    public static bool ImplementsIQueryable(ITypeSymbol typeSymbol, Compilation compilation)
    {
        if (typeSymbol == null || compilation == null)
            return false;

        var iqueryableSymbol = compilation.GetTypeByMetadataName("System.Linq.IQueryable`1");
        if (iqueryableSymbol == null)
            return false;

        if (typeSymbol is INamedTypeSymbol namedType)
        {
            // 直接IQueryable<T>かチェック
            if (SymbolEqualityComparer.Default.Equals(namedType.ConstructedFrom, iqueryableSymbol))
                return true;

            // インターフェースを走査
            return namedType.AllInterfaces.Any(i =>
                SymbolEqualityComparer.Default.Equals(i.ConstructedFrom, iqueryableSymbol));
        }

        return false;
    }

    /// <summary>
    /// 型がIEnumerable&lt;T&gt;を実装しているかを判定
    /// </summary>
    /// <param name="typeSymbol">判定する型シンボル</param>
    /// <param name="compilation">コンパイレーション</param>
    /// <returns>IEnumerable&lt;T&gt;を実装している場合true</returns>
    public static bool ImplementsIEnumerable(ITypeSymbol typeSymbol, Compilation compilation)
    {
        if (typeSymbol == null || compilation == null)
            return false;

        var ienumerableSymbol = compilation.GetTypeByMetadataName("System.Collections.Generic.IEnumerable`1");
        if (ienumerableSymbol == null)
            return false;

        if (typeSymbol is INamedTypeSymbol namedType)
        {
            if (SymbolEqualityComparer.Default.Equals(namedType.ConstructedFrom, ienumerableSymbol))
                return true;

            return namedType.AllInterfaces.Any(i =>
                SymbolEqualityComparer.Default.Equals(i.ConstructedFrom, ienumerableSymbol));
        }

        return false;
    }

    /// <summary>
    /// ジェネリック型の型引数を安全に取得
    /// </summary>
    /// <param name="typeSymbol">型シンボル</param>
    /// <param name="index">型引数のインデックス（デフォルト: 0）</param>
    /// <returns>型引数、存在しない場合はnull</returns>
    public static ITypeSymbol? GetGenericTypeArgument(ITypeSymbol typeSymbol, int index = 0)
    {
        if (typeSymbol is INamedTypeSymbol namedType
            && namedType.IsGenericType
            && namedType.TypeArguments.Length > index)
        {
            return namedType.TypeArguments[index];
        }

        return null;
    }

    /// <summary>
    /// 匿名型かどうかを判定
    /// </summary>
    /// <param name="typeSymbol">型シンボル</param>
    /// <returns>匿名型の場合true</returns>
    public static bool IsAnonymousType(ITypeSymbol? typeSymbol)
    {
        return typeSymbol?.IsAnonymousType ?? false;
    }

    /// <summary>
    /// グローバルネームスペースかどうかを判定
    /// </summary>
    /// <param name="namespaceSymbol">ネームスペースシンボル</param>
    /// <returns>グローバルネームスペースの場合true</returns>
    public static bool IsGlobalNamespace(INamespaceSymbol? namespaceSymbol)
    {
        return namespaceSymbol?.IsGlobalNamespace ?? false;
    }

    /// <summary>
    /// 式がSelectメソッド呼び出しを含むかを判定
    /// </summary>
    /// <param name="expression">判定する式</param>
    /// <returns>Selectメソッド呼び出しを含む場合true</returns>
    public static bool ContainsSelectInvocation(ExpressionSyntax expression)
    {
        if (expression == null)
            return false;

        return expression.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Any(inv => inv.Expression is MemberAccessExpressionSyntax ma
                && ma.Name.Identifier.Text == "Select");
    }

    /// <summary>
    /// 式がSelectManyメソッド呼び出しを含むかを判定
    /// </summary>
    /// <param name="expression">判定する式</param>
    /// <returns>SelectManyメソッド呼び出しを含む場合true</returns>
    public static bool ContainsSelectManyInvocation(ExpressionSyntax expression)
    {
        if (expression == null)
            return false;

        return expression.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Any(inv => inv.Expression is MemberAccessExpressionSyntax ma
                && ma.Name.Identifier.Text == "SelectMany");
    }

    /// <summary>
    /// 型がジェネリック型かどうかを判定（文字列ベース - レガシー用）
    /// 可能な限り INamedTypeSymbol.IsGenericType を使用してください
    /// </summary>
    /// <param name="typeName">型名文字列</param>
    /// <returns>型名に"&lt;"が含まれる場合true</returns>
    public static bool IsGenericTypeByString(string typeName)
    {
        return !string.IsNullOrEmpty(typeName) && typeName.Contains("<");
    }

    /// <summary>
    /// 型名が匿名型を示すかを判定（文字列ベース - レガシー用）
    /// 可能な限り IsAnonymousType(ITypeSymbol) を使用してください
    /// </summary>
    /// <param name="typeName">型名文字列</param>
    /// <returns>型名が匿名型を示す場合true</returns>
    public static bool IsAnonymousTypeByString(string typeName)
    {
        if (string.IsNullOrEmpty(typeName))
            return false;

        // "global::<anonymous" で始まるか、または "<>" を含む
        return typeName.StartsWith("global::<anonymous") ||
               (typeName.Contains("<") && typeName.Contains(">") && typeName.Contains("AnonymousType"));
    }
}
