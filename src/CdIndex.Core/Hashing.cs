using System.Security.Cryptography;
using System.Text;

namespace CdIndex.Core;

public static class Hashing
{
    public static string Sha256Hex(string filePath)
    {
        var content = System.IO.File.ReadAllText(filePath, Encoding.UTF8).Replace("\r\n", "\n");
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(content));
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }
}
