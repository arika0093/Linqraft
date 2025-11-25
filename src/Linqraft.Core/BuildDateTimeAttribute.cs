using System;

namespace Linqraft.Core;

[AttributeUsage(AttributeTargets.Assembly)]
public class BuildDateTimeAttribute : Attribute
{
    public DateTime BuiltDateTimeUtc { get; }

    public BuildDateTimeAttribute(string dateTickString)
    {
        var ticks = long.Parse(dateTickString);
        BuiltDateTimeUtc = new DateTime(ticks, DateTimeKind.Utc);
    }

    /// <summary>
    /// Get build date time in UTC from assembly attribute.
    /// </summary>
    public static DateTime? GetBuildDateTimeUtc()
    {
        var assembly = System.Reflection.Assembly.GetExecutingAssembly();
        var attr =
            Attribute.GetCustomAttribute(assembly, typeof(BuildDateTimeAttribute))
            as BuildDateTimeAttribute;
        return attr?.BuiltDateTimeUtc;
    }
}
