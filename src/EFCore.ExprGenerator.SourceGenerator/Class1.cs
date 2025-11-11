using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EFCore.ExprGenerator;

/// <summary>
/// Generator for SelectExpr method
/// </summary>
[Generator]
public class SelectExprGenerator : IIncrementalGenerator
{
    /// <summary>
    /// Initialize the generator
    /// </summary>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // デバッグ: Source Generatorが実行されていることを確認
        context.RegisterPostInitializationOutput(ctx =>
        {
            ctx.AddSource("DebugInfo.g.cs", @"// Source Generator is running");
        });

        // SelectExpr メソッド呼び出しを検出するプロバイダー
        var invocations = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => IsSelectExprInvocation(node),
                transform: static (ctx, _) => GetSelectExprInfo(ctx))
            .Where(static info => info is not null);

        // コード生成
        context.RegisterSourceOutput(invocations, (spc, info) =>
        {
            if (info is null) return;
            GenerateCode(spc, info);
        });
    }

    private static bool IsSelectExprInvocation(SyntaxNode node)
    {
        // InvocationExpressionで、メソッド名が "SelectExpr" のものを検出
        if (node is not InvocationExpressionSyntax invocation)
            return false;

        var expression = invocation.Expression;

        // MemberAccessExpression (例: query.SelectExpr)
        if (expression is MemberAccessExpressionSyntax memberAccess)
        {
            return memberAccess.Name.Identifier.Text == "SelectExpr";
        }

        return false;
    }

    private static SelectExprInfo? GetSelectExprInfo(GeneratorSyntaxContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        var semanticModel = context.SemanticModel;

        // 引数からラムダ式を取得
        if (invocation.ArgumentList.Arguments.Count == 0)
            return null;

        var lambdaArg = invocation.ArgumentList.Arguments[0].Expression;
        if (lambdaArg is not LambdaExpressionSyntax lambda)
            return null;

        // ラムダの本体が匿名型の初期化子かチェック
        var body = lambda.Body;
        AnonymousObjectCreationExpressionSyntax? anonymousObj = body switch
        {
            AnonymousObjectCreationExpressionSyntax anon => anon,
            _ => null
        };

        if (anonymousObj is null)
            return null;

        // MemberAccessExpressionから対象の型を取得
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return null;

        var symbolInfo = semanticModel.GetSymbolInfo(memberAccess.Expression);
        if (symbolInfo.Symbol is not IPropertySymbol and not ILocalSymbol and not IParameterSymbol)
            return null;

        // 型情報を取得
        var typeInfo = semanticModel.GetTypeInfo(memberAccess.Expression);
        if (typeInfo.Type is not INamedTypeSymbol namedType)
            return null;

        // IQueryable<T> の T を取得
        var sourceType = namedType.TypeArguments.FirstOrDefault();
        if (sourceType is null)
            return null;

        return new SelectExprInfo(
            SourceType: sourceType,
            AnonymousObject: anonymousObj,
            SemanticModel: semanticModel,
            Invocation: invocation
        );
    }

    private static void GenerateCode(SourceProductionContext context, SelectExprInfo info)
    {
        try
        {
            // 匿名型の構造を解析
            var dtoStructure = AnalyzeAnonymousType(info.AnonymousObject, info.SemanticModel, info.SourceType);

            // プロパティが空の場合はスキップ
            if (dtoStructure.Properties.Count == 0)
                return;

            // ユニークIDを生成（プロパティ構造のハッシュから）
            var uniqueId = GenerateUniqueId(dtoStructure);

            // ネームスペースを取得
            var namespaceSymbol = info.SourceType.ContainingNamespace;
            var namespaceName = namespaceSymbol?.ToDisplayString() ?? "Generated";

            // DTOクラスを生成（ネストしたDTOも含む）
            var dtoClasses = new List<string>();
            var mainDtoName = GenerateDtoClasses(dtoStructure, uniqueId, dtoClasses);

            // SelectExprメソッドを生成
            var selectExprMethod = GenerateSelectExprMethod(
                info.SourceType.Name,
                mainDtoName,
                dtoStructure,
                info.AnonymousObject
            );

            // 最終的なソースコードを組み立て
            var sourceCode = BuildSourceCode(namespaceName, mainDtoName, dtoClasses, selectExprMethod);

            // デバッグ: 生成されたコードをファイルとして出力
            context.AddSource("Debug_GeneratedCode.g.cs", $"/*\n{sourceCode}\n*/");

            // Source Generatorに登録
            context.AddSource($"GeneratedExpression_{uniqueId}.g.cs", sourceCode);
        }
        catch (Exception ex)
        {
            // デバッグ用にエラー情報を出力
            var errorMessage = $@"// Source Generator Error: {ex.Message}
// Stack Trace: {ex.StackTrace}
";
            context.AddSource("GeneratorError.g.cs", errorMessage);
        }
    }

    private static string GenerateDtoClasses(
        DtoStructure structure,
        string uniqueId,
        List<string> dtoClasses)
    {
        var dtoName = $"{structure.SourceTypeName}Dto_{uniqueId}";

        var sb = new StringBuilder();
        sb.AppendLine($"    public class {dtoName}");
        sb.AppendLine("    {");

        foreach (var prop in structure.Properties)
        {
            var propertyType = prop.TypeName;
            // もしpropertyTypeがGenerics型であれば、その親側のみを使う
            if (propertyType.Contains("<"))
            {
                propertyType = propertyType[..propertyType.IndexOf("<")];
            }

            // ネストした構造の場合、再帰的にDTOを生成（先に追加）
            if (prop.NestedStructure is not null)
            {
                var nestedId = GenerateUniqueId(prop.NestedStructure);
                var nestedDtoName = GenerateDtoClasses(prop.NestedStructure, nestedId, dtoClasses);
                propertyType = $"{propertyType}<{nestedDtoName}>";
            }
            sb.AppendLine($"        public required {propertyType} {prop.Name} {{ get; set; }}");
        }

        sb.AppendLine("    }");

        // 現在のDTOを追加（ネストしたDTOは再帰呼び出しで既に追加されている）
        dtoClasses.Add(sb.ToString());
        return dtoName;
    }

    private static string GenerateSelectExprMethod(
        string sourceTypeName,
        string dtoName,
        DtoStructure structure,
        AnonymousObjectCreationExpressionSyntax anonymousObj)
    {
        var sb = new StringBuilder();
        sb.AppendLine("        /// <summary>");
        sb.AppendLine("        /// generated method");
        sb.AppendLine("        /// </summary>");
        sb.AppendLine($"        public static IQueryable<{dtoName}> SelectExpr<TResult>(");
        sb.AppendLine($"            this IQueryable<{sourceTypeName}> query,");
        sb.AppendLine($"            Func<{sourceTypeName}, TResult> selector)");
        sb.AppendLine("        {");
        sb.AppendLine($"            return query.Select(s => new {dtoName}");
        sb.AppendLine("            {");

        // 各プロパティの割り当てを生成
        var propertyAssignments = new List<string>();
        foreach (var prop in structure.Properties)
        {
            var assignment = GeneratePropertyAssignment(prop, "s");
            propertyAssignments.Add($"                {prop.Name} = {assignment}");
        }

        sb.AppendLine(string.Join(",\n", propertyAssignments));
        sb.AppendLine("            });");
        sb.AppendLine("        }");

        return sb.ToString();
    }

    private static string GeneratePropertyAssignment(DtoProperty property, string parameterName)
    {
        var expression = property.OriginalExpression;

        // ネストしたSelect（コレクション）の場合
        if (property.NestedStructure is not null)
        {
            return ConvertNestedSelect(expression, property.NestedStructure);
        }

        // Nullable演算子が使われている場合、明示的なnullチェックに変換
        if (property.IsNullable && expression.Contains("?."))
        {
            return ConvertNullableAccessToExplicitCheck(expression);
        }

        // 通常のプロパティアクセス
        return expression;
    }

    private static string ConvertNestedSelect(string expression, DtoStructure nestedStructure)
    {
        // 例: s.Childs.Select(c => new { ... })
        // パラメータ名を抽出（例: "c"）
        var selectIndex = expression.IndexOf(".Select(");
        if (selectIndex == -1)
            return expression;

        var lambdaStart = expression.IndexOf("(", selectIndex + 8);
        if (lambdaStart == -1)
            return expression;

        var lambdaArrow = expression.IndexOf("=>", lambdaStart);
        if (lambdaArrow == -1 || lambdaArrow <= lambdaStart + 1)
            return expression;

        var paramName = expression.Substring(lambdaStart + 1, lambdaArrow - lambdaStart - 1).Trim();
        if (string.IsNullOrEmpty(paramName))
            paramName = "x"; // デフォルトパラメータ名

        var baseExpression = expression[..selectIndex];
        var uniqueId = GenerateUniqueId(nestedStructure);
        var nestedDtoName = $"{nestedStructure.SourceTypeName}Dto_{uniqueId}";

        var propertyAssignments = new List<string>();
        foreach (var prop in nestedStructure.Properties)
        {
            var assignment = GeneratePropertyAssignment(prop, paramName);
            // ネストしたSelectの場合、複数行にならないように inline に変換
            assignment = assignment.Replace("\n", " ").Replace("\r", "");
            propertyAssignments.Add($"{prop.Name} = {assignment}");
        }

        var propertiesCode = string.Join(", ", propertyAssignments);
        return $"{baseExpression}.Select({paramName} => new {nestedDtoName} {{ {propertiesCode} }}).ToList()";
    }

    private static string ConvertNullableAccessToExplicitCheck(string expression)
    {
        // 例: c.Child?.Id → c.Child != null ? c.Child.Id : null
        // 例: s.Child3?.Child?.Id → s.Child3 != null && s.Child3.Child != null ? s.Child3.Child.Id : null

        if (!expression.Contains("?."))
            return expression;

        // ?. を . に置換して実際のアクセスパスを作成
        var accessPath = expression.Replace("?.", ".");

        // ?. が出現する箇所を見つけてnullチェックを構築
        var checks = new List<string>();
        var parts = expression.Split(["?."], StringSplitOptions.None);

        if (parts.Length < 2)
            return expression;

        // 最初のパート以外はnullチェックが必要
        var currentPath = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            checks.Add($"{currentPath} != null");

            // 次のパートの最初のトークン（プロパティ名）を取得
            var nextPart = parts[i];
            var dotIndex = nextPart.IndexOf('.');
            var propertyName = dotIndex > 0 ? nextPart[..dotIndex] : nextPart;

            currentPath = $"{currentPath}.{propertyName}";
        }

        if (checks.Count == 0)
            return expression;

        // nullチェックを構築
        var nullCheckPart = string.Join(" && ", checks);

        return $"{nullCheckPart} ? {accessPath} : null";
    }

    private static string BuildSourceCode(
        string namespaceName,
        string mainDtoName,
        List<string> dtoClasses,
        string selectExprMethod)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("#pragma warning disable IDE0060");
        sb.AppendLine();
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Linq;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine();
        sb.AppendLine($"namespace {namespaceName}");
        sb.AppendLine("{");
        sb.AppendLine("    internal static class GeneratedExpression");
        sb.AppendLine("    {");
        sb.AppendLine(selectExprMethod);
        sb.AppendLine("    }");
        sb.AppendLine();

        foreach (var dtoClass in dtoClasses)
        {
            sb.AppendLine(dtoClass);
        }

        sb.AppendLine("}");

        return sb.ToString();
    }

    private static DtoStructure AnalyzeAnonymousType(
        AnonymousObjectCreationExpressionSyntax anonymousObj,
        SemanticModel semanticModel,
        ITypeSymbol sourceType)
    {
        var properties = new List<DtoProperty>();

        foreach (var initializer in anonymousObj.Initializers)
        {
            string propertyName;
            var expression = initializer.Expression;

            // 明示的なプロパティ名がある場合 (例: Id = s.Id)
            if (initializer.NameEquals is not null)
            {
                propertyName = initializer.NameEquals.Name.Identifier.Text;
            }
            // 暗黙的なプロパティ名の場合 (例: s.Id)
            else
            {
                // 式から推測されるプロパティ名を取得
                var name = GetImplicitPropertyName(expression);
                if (name is null)
                {
                    continue;
                }
                propertyName = name;
            }

            var property = AnalyzeExpression(propertyName, expression, semanticModel);
            if (property is not null)
                properties.Add(property);
        }

        return new DtoStructure(
            SourceTypeName: sourceType.Name,
            SourceTypeFullName: sourceType.ToDisplayString(),
            Properties: properties
        );
    }

    private static string? GetImplicitPropertyName(ExpressionSyntax expression)
    {
        // メンバーアクセス (例: s.Id) からプロパティ名を取得
        if (expression is MemberAccessExpressionSyntax memberAccess)
        {
            return memberAccess.Name.Identifier.Text;
        }

        // 識別子 (例: id) からプロパティ名を取得
        if (expression is IdentifierNameSyntax identifier)
        {
            return identifier.Identifier.Text;
        }

        // その他の複雑な式の場合は処理しない
        return null;
    }

    private static DtoProperty? AnalyzeExpression(
        string propertyName,
        ExpressionSyntax expression,
        SemanticModel semanticModel)
    {
        var typeInfo = semanticModel.GetTypeInfo(expression);
        if (typeInfo.Type is null)
            return null;

        var propertyType = typeInfo.Type;
        var isNullable = propertyType.NullableAnnotation == NullableAnnotation.Annotated;

        // Nullable演算子 ?. が使われているかチェック
        var hasNullableAccess = HasNullableAccess(expression);

        // ネストした Select (例: s.Childs.Select(...)) を検出
        DtoStructure? nestedStructure = null;
        if (expression is InvocationExpressionSyntax nestedInvocation)
        {
            // Select メソッド呼び出しかチェック
            if (nestedInvocation.Expression is MemberAccessExpressionSyntax nestedMemberAccess &&
                nestedMemberAccess.Name.Identifier.Text == "Select")
            {
                // ラムダ式の中の匿名型を解析
                if (nestedInvocation.ArgumentList.Arguments.Count > 0)
                {
                    var lambdaArg = nestedInvocation.ArgumentList.Arguments[0].Expression;
                    if (lambdaArg is LambdaExpressionSyntax nestedLambda &&
                        nestedLambda.Body is AnonymousObjectCreationExpressionSyntax nestedAnonymous)
                    {
                        // コレクション要素の型を取得
                        var collectionType = semanticModel.GetTypeInfo(nestedMemberAccess.Expression).Type;
                        if (collectionType is INamedTypeSymbol namedCollectionType &&
                            namedCollectionType.TypeArguments.Length > 0)
                        {
                            var elementType = namedCollectionType.TypeArguments[0];
                            nestedStructure = AnalyzeAnonymousType(nestedAnonymous, semanticModel, elementType);
                        }
                    }
                }
            }
        }

        return new DtoProperty(
            Name: propertyName,
            TypeName: propertyType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
            IsNullable: isNullable || hasNullableAccess,
            OriginalExpression: expression.ToString(),
            NestedStructure: nestedStructure
        );
    }

    private static bool HasNullableAccess(ExpressionSyntax expression)
    {
        // ?. 演算子が使われているかチェック
        return expression.DescendantNodes()
            .OfType<ConditionalAccessExpressionSyntax>()
            .Any();
    }

    private static string GenerateUniqueId(DtoStructure structure)
    {
        // プロパティ構造からハッシュを生成
        var signature = string.Join("|", structure.Properties.Select(p =>
            $"{p.Name}:{p.TypeName}:{p.IsNullable}"));

        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var hash = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(signature));
        return BitConverter.ToString(hash).Replace("-", "")[..8]; // 最初の8文字を使用
    }

    private record SelectExprInfo(
        ITypeSymbol SourceType,
        AnonymousObjectCreationExpressionSyntax AnonymousObject,
        SemanticModel SemanticModel,
        InvocationExpressionSyntax Invocation
    );

    private record DtoStructure(
        string SourceTypeName,
        string SourceTypeFullName,
        List<DtoProperty> Properties
    );

    private record DtoProperty(
        string Name,
        string TypeName,
        bool IsNullable,
        string OriginalExpression,
        DtoStructure? NestedStructure
    );
}
