using System.Collections.Generic;

namespace Linqraft.Tests.OfTypeTestNamespace;

public class OfTypeParent
{
    public int Id { get; set; }
    public List<OfTypeChildBase> Items { get; set; } = [];
}

public abstract class OfTypeChildBase
{
    public string Name { get; set; } = "";
}

public class OfTypeChildA : OfTypeChildBase
{
    public int AValue { get; set; }
}

public class OfTypeChildB : OfTypeChildBase
{
    public int BValue { get; set; }
}
