using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Linqraft.Benchmark;

/// <summary>
/// Main sample entity for benchmark.
/// </summary>
public class SampleClass
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }
    public string Foo { get; set; } = string.Empty;
    public string Bar { get; set; } = string.Empty;

    /// <summary>
    /// Collection of child entities.
    /// </summary>
    public List<SampleChildClass> Childs { get; set; } = [];

    /// <summary>
    /// Optional second child entity.
    /// </summary>
    public SampleChildClass2? Child2 { get; set; } = null;

    /// <summary>
    /// Third child entity (required).
    /// </summary>
    public SampleChildClass3 Child3 { get; set; } = null!;
}

/// <summary>
/// Child entity for SampleClass.
/// </summary>
public class SampleChildClass
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }
    public int SampleClassId { get; set; }
    public string Baz { get; set; } = string.Empty;

    [ForeignKey("SampleClassId")]
    public SampleClass SampleClass { get; set; } = null!;

    /// <summary>
    /// Optional grandchild entity.
    /// </summary>
    public SampleChildChildClass? Child { get; set; } = null;
}

/// <summary>
/// Grandchild entity for SampleChildClass.
/// </summary>
public class SampleChildChildClass
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }
    public int SampleChildClassId { get; set; }
    public string Qux { get; set; } = string.Empty;

    [ForeignKey("SampleChildClassId")]
    public SampleChildClass SampleChildClass { get; set; } = null!;
}

/// <summary>
/// Second child entity for SampleClass.
/// </summary>
public class SampleChildClass2
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public int SampleClassId { get; set; }

    public string Quux { get; set; } = string.Empty;

    [ForeignKey("SampleClassId")]
    public SampleClass SampleClass { get; set; } = null!;
}

public class SampleChildClass3
{
    /// <summary>
    /// Third child entity for SampleClass.
    /// </summary>
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }
    public int SampleClassId { get; set; }
    public string Corge { get; set; } = string.Empty;

    [ForeignKey("SampleClassId")]
    public SampleClass SampleClass { get; set; } = null!;

    /// <summary>
    /// Optional grandchild entity for third child.
    /// </summary>
    public SampleChildChildClass2? Child { get; set; } = null;
}

/// <summary>
/// Grandchild entity for SampleChildClass3.
/// </summary>
public class SampleChildChildClass2
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }
    public int SampleChildClass3Id { get; set; }
    public string Grault { get; set; } = string.Empty;

    [ForeignKey("SampleChildClass3Id")]
    public SampleChildClass3 SampleChildClass3 { get; set; } = null!;
}
