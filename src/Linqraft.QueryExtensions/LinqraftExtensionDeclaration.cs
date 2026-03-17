namespace Linqraft;

public abstract class LinqraftExtensionDeclaration
{
    public abstract string Namespace { get; }

    public abstract string ExtensionClassName { get; }

    public abstract string BehaviorKey { get; }

    public abstract string MethodDeclarations { get; }
}
