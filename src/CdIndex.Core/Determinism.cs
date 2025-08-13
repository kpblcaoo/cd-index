using System.Globalization;

namespace CdIndex.Core;

public static class InvariantCultureScope
{
    public static IDisposable Enter()
    {
        var original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        return new DisposableAction(() => CultureInfo.CurrentCulture = original);
    }

    private sealed class DisposableAction : IDisposable
    {
        private readonly Action _onDispose;
        public DisposableAction(Action onDispose) => _onDispose = onDispose;
        public void Dispose() => _onDispose();
    }
}

public static class PathEx
{
    public static string Normalize(string path, string repoRoot)
    {
        var rel = path.Replace(repoRoot, "").TrimStart(System.IO.Path.DirectorySeparatorChar, '/');
        return rel.Replace("\\", "/");
    }
}

public static class Orderer
{
    public static IReadOnlyList<T> Sort<T>(IEnumerable<T> source, IComparer<T> comparer)
    {
        var list = source.ToList();
        list.Sort(comparer);
        return list;
    }
}
