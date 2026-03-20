using System.Text;
using System.Threading;

namespace Linqraft.Core.Utilities;

internal static class HashingHelper
{
    private static readonly uint[] Crc32Table = CreateCrc32Table();

    public static string ComputeHash(
        string value,
        int length = 8,
        CancellationToken cancellationToken = default
    )
    {
        if (length <= 0)
        {
            return string.Empty;
        }

        var bytes = Encoding.UTF8.GetBytes(value);
        var builder = new StringBuilder(((length + 7) / 8) * 8);
        var seed = 0u;

        while (builder.Length < length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var crc = ComputeCrc32(bytes, seed, cancellationToken);
            builder.Append(crc.ToString("X8"));
            seed = unchecked((crc * 16777619u) + 2166136261u + (uint)builder.Length);
        }

        return builder.ToString(0, length);
    }

    private static uint ComputeCrc32(
        byte[] bytes,
        uint seed,
        CancellationToken cancellationToken = default
    )
    {
        var crc = ~seed;
        foreach (var current in bytes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            crc = (crc >> 8) ^ Crc32Table[(crc ^ current) & 0xFF];
        }

        return ~crc;
    }

    private static uint[] CreateCrc32Table()
    {
        var table = new uint[256];
        for (uint index = 0; index < table.Length; index++)
        {
            var current = index;
            for (var bit = 0; bit < 8; bit++)
            {
                current = (current & 1) == 0 ? current >> 1 : (current >> 1) ^ 0xEDB88320u;
            }

            table[index] = current;
        }

        return table;
    }
}
