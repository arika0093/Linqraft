using System;

namespace Linqraft;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class LinqraftExtensionsAttribute : Attribute
{
    public LinqraftExtensionsAttribute(string methodName)
    {
        MethodName = methodName;
    }

    public string MethodName { get; }
}
