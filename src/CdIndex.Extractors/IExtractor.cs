using CdIndex.Roslyn;

namespace CdIndex.Extractors;

public interface IExtractor
{
    void Extract(RoslynContext context);
}

// Generic variant to allow strongly-typed access to produced sections/records.
// Non-breaking: existing code can continue to depend on non-generic IExtractor.
public interface IExtractor<T> : IExtractor
{
    IReadOnlyList<T> Items { get; }
}
