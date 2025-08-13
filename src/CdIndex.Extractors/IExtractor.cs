using CdIndex.Roslyn;

namespace CdIndex.Extractors;

public interface IExtractor
{
    void Extract(RoslynContext context);
}
