// Internal helper for command text normalization and literal checks.
// Extracted from CommandsExtractor to reduce its size and enable focused testing.
namespace CdIndex.Extractors;

internal static class CommandText
{
    public static string? Normalize(string? raw, bool trim, bool ensureSlash)
    {
        if (raw == null) return null;
        var s = raw;
        if (trim) s = s.Trim();
        if (s.Length == 0) return null;
        if (ensureSlash && !s.StartsWith('/')) s = "/" + s;
        if (s.Length < 2) return null; // must have content beyond leading slash or >=2 chars total
                                       // Fast whitespace rejection (space, tab, newline, carriage return)
        if (s.IndexOfAny(new[] { ' ', '\t', '\r', '\n' }) >= 0) return null;
        return s;
    }

    public static bool IsCommandLiteral(string s) => s.Length > 1 && s[0] == '/';
}
