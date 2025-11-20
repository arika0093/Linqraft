using System;
using System.Security.Cryptography;
using System.Text;

namespace Linqraft.Core;

/// <summary>
/// Utility class for generating consistent hash values across the codebase
/// </summary>
public static class HashUtility
{
    /// <summary>
    /// Generates a SHA256-based hash string with hexadecimal characters
    /// </summary>
    /// <param name="input">The input string to hash</param>
    /// <param name="length">The desired length of the hash (default: 8)</param>
    /// <returns>A hash string of the specified length using hexadecimal characters (0-9, A-F)</returns>
    public static string GenerateSha256Hash(string input, int length = 8)
    {
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
        return BitConverter.ToString(hash).Replace("-", "")[..Math.Min(length, 64)];
    }

    /// <summary>
    /// Generates a hash string using the FNV-1a algorithm with alphanumeric characters
    /// </summary>
    /// <param name="input">The input string to hash</param>
    /// <param name="length">The desired length of the hash (default: 8)</param>
    /// <returns>A hash string of the specified length using uppercase letters and digits (A-Z, 0-9)</returns>
    public static string GenerateAlphanumericHash(string input, int length = 8)
    {
        // Use FNV-1a algorithm for deterministic hashing
        uint hash = 2166136261;
        foreach (char c in input)
        {
            hash ^= c;
            hash *= 16777619;
        }

        var hashString = new StringBuilder(length);
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

        for (int i = 0; i < length; i++)
        {
            hashString.Append(chars[(int)(hash % chars.Length)]);
            hash /= (uint)chars.Length;
        }

        return hashString.ToString();
    }

    /// <summary>
    /// Generates a random alphanumeric identifier (for error handling scenarios)
    /// </summary>
    /// <param name="length">The desired length of the identifier (default: 8)</param>
    /// <returns>A random alphanumeric string of the specified length</returns>
    public static string GenerateRandomIdentifier(int length = 8)
    {
        return Guid.NewGuid().ToString("N")[..Math.Min(length, 32)];
    }
}
