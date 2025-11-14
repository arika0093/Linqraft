namespace Linqraft.Benchmark;

// Manual DTOs for traditional Select approach (Pattern 1)
public class ManualSampleClassDto
{
    public int Id { get; set; }
    public string Foo { get; set; } = string.Empty;
    public string Bar { get; set; } = string.Empty;
    public IEnumerable<ManualSampleChildDto> Childs { get; set; } = [];
    public int? Child2Id { get; set; }
    public string? Child2Quux { get; set; }
    public int Child3Id { get; set; }
    public string Child3Corge { get; set; } = string.Empty;
    public int? Child3ChildId { get; set; }
    public string? Child3ChildGrault { get; set; }
}

// Manual Data Transfer Objects (DTOs) for the traditional Select approach (Pattern 1)

public class ManualSampleChildDto
{
    public int Id { get; set; }
    public string Baz { get; set; } = string.Empty;
    public int? ChildId { get; set; }
    public string? ChildQux { get; set; }
}
// Child Data Transfer Object (DTO)
