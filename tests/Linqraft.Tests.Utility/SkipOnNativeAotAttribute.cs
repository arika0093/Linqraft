using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Linqraft.Tests.Utility;

/// <summary>
/// Indicates that a test should be skipped when running on NativeAOT runtimes. <br/>
/// * dynamic code generation is not supported on NativeAOT runtimes <br/>
/// * decimal expression support is not available on NativeAOT runtimes
/// </summary>
public class SkipOnNativeAotAttribute()
    : SkipAttribute("This test is not supported on NativeAOT runtimes.")
{
    public override Task<bool> ShouldSkip(TestRegisteredContext context)
    {
        return Task.FromResult(!RuntimeFeature.IsDynamicCodeSupported);
    }
}
