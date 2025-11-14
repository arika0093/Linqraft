namespace Linqraft.Tests.SourceNamespace;

public class TestClass
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public class ParentClass
{
    public int Id { get; set; }
    public ChildClass Child { get; set; } = new();
}

public class ChildClass
{
    public string Name { get; set; } = "";
}
