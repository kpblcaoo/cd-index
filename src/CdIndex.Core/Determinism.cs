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
        if (string.IsNullOrEmpty(path)) return string.Empty;
        var p = path.Replace("\\", "/");
        var root = repoRoot.Replace("\\", "/");
        if (p.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            p = p.Substring(root.Length);
        p = p.Replace("//", "/").TrimStart('/');
        return p;
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

    public static IReadOnlyList<T> ThenDeterministic<T, TKey1, TKey2, TKey3>(IEnumerable<T> source,
        Func<T, TKey1> k1, Func<T, TKey2> k2, Func<T, TKey3> k3,
        IComparer<object>? cmp = null)
    {
        cmp ??= StringComparer.Ordinal as IComparer<object> ?? Comparer<object>.Default;
        return source.OrderBy(x => k1(x), Comparer<TKey1>.Default)
            .ThenBy(x => k2(x), Comparer<TKey2>.Default)
            .ThenBy(x => k3(x), Comparer<TKey3>.Default)
            .ToList();
    }
}
