namespace CdIndex.Core;

public static class LocCounter
{
    public static int Count(string content)
    {
        var normalized = content.Replace("\r\n", "\n");
        var lines = normalized.Split('\n');
        int count = lines.Length;
        if (count > 0 && string.IsNullOrWhiteSpace(lines[^1]))
            count--;
        return count;
    }
}
