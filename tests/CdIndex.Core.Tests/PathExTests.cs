using CdIndex.Core;
using Xunit;

namespace CdIndex.Core.Tests;

public class PathExTests
{
    [Theory]
    [InlineData("/repo/src/File.cs", "/repo", "src/File.cs")] // basic root strip
    [InlineData("/repo/src//File.cs", "/repo", "src/File.cs")] // double slash collapse
    [InlineData("/repo/src/dir/File.cs", "/repo/", "src/dir/File.cs")] // trailing slash in root
    [InlineData("/repo/src/dir/File.cs", "/repo", "src/dir/File.cs")] // exact
    [InlineData("/repo/src/dir/File.cs", "/REPO", "src/dir/File.cs")] // case-insensitive prefix
    [InlineData("C:/repo/src/File.cs", "C:/repo", "src/File.cs")] // Windows style forward
    [InlineData("C:\\repo\\src\\File.cs", "C:/repo", "src/File.cs")] // Windows backslashes
    [InlineData("/other/File.cs", "/repo", "other/File.cs")] // different root -> left trimmed leading slash
    [InlineData("", "/repo", "")] // empty path
    public void Normalize_VariousCases_ReturnsExpected(string full, string root, string expected)
    {
        var actual = PathEx.Normalize(full, root);
        Assert.Equal(expected, actual);
    }
}
