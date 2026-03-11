using System.Linq;
using System.Runtime.CompilerServices;

namespace Linqraft.Tests;

public sealed class HashingHelperTests
{
    [Test]
    public void ComputeHash_returns_deterministic_uppercase_hex_with_requested_length()
    {
        SkipIfNativeAot();
        var shortHash = InvokeComputeHash("Linqraft.Hash.Sample", 8);
        var longHash = InvokeComputeHash("Linqraft.Hash.Sample", 16);

        shortHash.Length.ShouldBe(8);
        longHash.Length.ShouldBe(16);
        shortHash.ShouldBe(longHash[..8]);
        shortHash.All(IsUpperHexCharacter).ShouldBeTrue();
        longHash.All(IsUpperHexCharacter).ShouldBeTrue();
        InvokeComputeHash("Linqraft.Hash.Sample", 16).ShouldBe(longHash);
    }

    [Test]
    public void ComputeHash_changes_when_input_changes()
    {
        SkipIfNativeAot();
        var left = InvokeComputeHash("Linqraft.Hash.Left", 16);
        var right = InvokeComputeHash("Linqraft.Hash.Right", 16);

        left.ShouldNotBe(right);
    }

    private static string InvokeComputeHash(string value, int length)
    {
        var coreAssembly = System.Reflection.Assembly.Load("Linqraft.Core");
        var helperType = coreAssembly.GetType("Linqraft.Core.Utilities.HashingHelper");
        helperType.ShouldNotBeNull();

        var computeHash = helperType.GetMethod(
            "ComputeHash",
            System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Static
        );
        computeHash.ShouldNotBeNull();

        return computeHash.Invoke(null, [value, length]).ShouldBeOfType<string>();
    }

    private static void SkipIfNativeAot()
    {
        if (!RuntimeFeature.IsDynamicCodeSupported)
        {
            global::TUnit.Core.Skip.Test(
                "Reflection-only hash verification is skipped under NativeAOT."
            );
        }
    }

    private static bool IsUpperHexCharacter(char character)
    {
        return char.IsDigit(character) || (character >= 'A' && character <= 'F');
    }
}
