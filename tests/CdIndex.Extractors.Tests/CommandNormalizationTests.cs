using CdIndex.Extractors;
using Xunit;

namespace CdIndex.Extractors.Tests;

public class CommandNormalizationTests
{
    [Theory]
    [InlineData("/start", true, true, "/start")]
    [InlineData("  /start  ", true, true, "/start")]
    [InlineData("start", true, true, "/start")] // ensureSlash adds
    [InlineData("start", true, false, "start")] // bare accepted when not enforcing slash
    [InlineData("/s", true, true, "/s")] // minimum length passes
    [InlineData("/", true, true, null)] // just slash rejected
    [InlineData(" / spaced ", true, true, null)] // internal whitespace rejected
    public void Normalize_Cases(string input, bool trim, bool ensureSlash, string? expected)
    {
        var actual = CommandText.Normalize(input, trim, ensureSlash);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("/a", true)]
    [InlineData("/ab", true)]
    [InlineData("a", false)]
    [InlineData("", false)]
    public void IsCommandLiteral_Works(string input, bool expected)
    {
        Assert.Equal(expected, CommandText.IsCommandLiteral(input));
    }
}
