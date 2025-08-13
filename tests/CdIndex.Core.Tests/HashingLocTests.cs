using Xunit;
using CdIndex.Core;
using System.IO;

public class HashingLocTests
{
    [Fact]
    public void Hashing_CRLF_and_LF_give_same_sha256()
    {
        var lf = "line1\nline2\n";
        var crlf = "line1\r\nline2\r\n";
        var lfPath = Path.GetTempFileName();
        var crlfPath = Path.GetTempFileName();
        File.WriteAllText(lfPath, lf);
        File.WriteAllText(crlfPath, crlf);
        var hashLf = Hashing.Sha256Hex(lfPath);
        var hashCrlf = Hashing.Sha256Hex(crlfPath);
        Assert.Equal(hashLf, hashCrlf);
        File.Delete(lfPath);
        File.Delete(crlfPath);
    }

    [Theory]
    [InlineData("a\nb\nc", 3)]
    [InlineData("a\nb\nc\n", 3)]
    [InlineData("a\r\nb\r\nc\r\n", 3)]
    [InlineData("a\n\n", 2)]
    public void LocCounter_Count_Works(string content, int expected)
    {
        Assert.Equal(expected, LocCounter.Count(content));
    }
}
