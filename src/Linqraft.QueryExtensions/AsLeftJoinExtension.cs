namespace Linqraft.Utility;

[global::Linqraft.LinqraftExtensions(MethodName)]
public sealed class AsLeftJoinExtension : global::Linqraft.LinqraftExtensionDeclaration
{
    public const string MethodName = "AsLeftJoin";
    public const string NamespaceValue = "Linqraft.Utility";
    public const string ExtensionClassNameValue = "LinqraftJoinHintExtensions";
    public const string BehaviorKeyValue = "AsLeftJoin";
    public const string MethodDeclarationsValue = """
        summary: Marks a navigation access so that Linqraft rewrites the following member access using left-join semantics.
        signature: public static T? AsLeftJoin<T>(this T? value) where T : class

        summary: Marks a queryable navigation access so that Linqraft rewrites the following query using left-join semantics.
        signature: public static global::System.Linq.IQueryable<T> AsLeftJoin<T>(this global::System.Linq.IQueryable<T> query) where T : class
        """;

    public override string Namespace => NamespaceValue;

    public override string ExtensionClassName => ExtensionClassNameValue;

    public override string BehaviorKey => BehaviorKeyValue;

    public override string MethodDeclarations => MethodDeclarationsValue;
}
