using Xunit;
using CdIndex.Core;
using System.Globalization;
using System.Collections.Generic;

public class DeterminismTests
{
    [Fact]
    public void InvariantCultureScope_SetsCulture()
    {
        var before = CultureInfo.CurrentCulture;
        using (InvariantCultureScope.Enter())
        {
            Assert.Equal(CultureInfo.InvariantCulture, CultureInfo.CurrentCulture);
        }
        Assert.Equal(before, CultureInfo.CurrentCulture);
    }

    [Theory]
    [InlineData("C:/repo/src/File.cs", "C:/repo", "src/File.cs")]
    [InlineData("C:\\repo\\src\\File.cs", "C:\\repo", "src/File.cs")]
    public void PathEx_Normalize_Works(string path, string repoRoot, string expected)
    {
        Assert.Equal(expected, PathEx.Normalize(path, repoRoot));
    }

    [Fact]
    public void Orderer_Sort_IsStable()
    {
        var input = new List<int> { 3, 1, 2 };
        var sorted = Orderer.Sort(input, Comparer<int>.Default);
        Assert.Equal(new List<int> { 1, 2, 3 }, sorted);
    }
}
