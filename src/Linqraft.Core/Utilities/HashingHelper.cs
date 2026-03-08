using System.Security.Cryptography;
using System.Text;

namespace Linqraft.Core.Utilities;

internal static class HashingHelper
{
    public static string ComputeHash(string value, int length = 8)
    {
        using var sha256 = SHA256.Create();
        var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(value));
        var builder = new StringBuilder(bytes.Length * 2);

        foreach (var current in bytes)
        {
            builder.Append(current.ToString("X2"));
        }

        return builder.ToString(0, length);
    }
}
