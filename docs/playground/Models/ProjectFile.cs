namespace Linqraft.Playground.Models;

/// <summary>
/// Represents a file in the project
/// </summary>
public class ProjectFile
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public string Content { get; set; } = "";
    public bool IsFolder { get; set; }
    public List<ProjectFile> Children { get; set; } = new();
}

/// <summary>
/// Represents a template that can be loaded
/// </summary>
public class Template
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public List<ProjectFile> Files { get; set; } = new();
}

/// <summary>
/// Represents the generated output from Linqraft
/// </summary>
public class GeneratedOutput
{
    public string QueryExpression { get; set; } = "";
    public string DtoClass { get; set; } = "";
    public string ErrorMessage { get; set; } = "";
    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);
}
