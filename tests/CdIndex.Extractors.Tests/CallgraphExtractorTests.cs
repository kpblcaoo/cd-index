using CdIndex.Extractors;
using CdIndex.Roslyn;
using Xunit;
using System.Linq;
using CdIndex.Core;

namespace CdIndex.Extractors.Tests;

public class CallgraphExtractorTests
{
    private RoslynContext LoadContext()
    {
        // Load isolated TestCallgraph project under TestAssets for deterministic symbol set
        var baseDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "TestAssets", "TestCallgraph"));
        var proj = Directory.GetFiles(baseDir, "TestCallgraph.csproj").Single();
        return SolutionLoader.LoadProjectAsync(proj, baseDir).GetAwaiter().GetResult();
    }

    [Fact]
    public void BuildsSimpleGraph_WithDepthLimit()
    {
            var ctx = LoadContext();
            var extractor = new CallgraphExtractor(new[] { "TestCallgraph.RootClass.A" }, maxDepth: 2, maxNodes: 100, includeExternal: false, verbose: false, log: null);
            extractor.Extract(ctx);
            var section = extractor.Sections.Single();
            var graph = section.Graphs.Single(g => g.Root.StartsWith("TestCallgraph.RootClass.A"));
            Assert.False(graph.Truncated);
            // Expect edges A->B, A->C, B->D (since depth=2, from A depth0 we traverse to depth1 B,C and to depth2 D)
            var edges = graph.Edges.Select(e => e.Caller + "->" + e.Callee).OrderBy(s => s).ToArray();
            Assert.Contains(edges, e => e.Contains("RootClass.A"));
            Assert.Contains(edges, e => e.Contains("RootClass.B"));
    }

    [Fact]
    public void RespectsNodeLimit_Truncates()
    {
            var ctx = LoadContext();
            var extractor = new CallgraphExtractor(new[] { "TestCallgraph.RootClass.A" }, maxDepth: 5, maxNodes: 2, includeExternal: false, verbose: false, log: null);
            extractor.Extract(ctx);
            var graph = extractor.Sections.Single().Graphs.Single();
            Assert.True(graph.Truncated);
    }

    [Fact]
    public void IncludesExternal_WhenRequested()
    {
            var ctx = LoadContext();
            var extractor = new CallgraphExtractor(new[] { "TestCallgraph.ExternalCalls.UseLinq" }, maxDepth: 1, maxNodes: 50, includeExternal: true, verbose: false, log: null);
            extractor.Extract(ctx);
            var graph = extractor.Sections.Single().Graphs.Single();
            Assert.False(graph.Truncated);
            Assert.Contains(graph.Edges, e => e.Caller.Contains("ExternalCalls.UseLinq") && e.Callee.Contains("Select"));
    }

    [Fact]
    public void OverloadAmbiguity_WarnsWhenVerbose()
    {
            var ctx = LoadContext();
            var messages = new List<string>();
            var extractor = new CallgraphExtractor(new[] { "TestCallgraph.RootClass.Over" }, maxDepth: 0, maxNodes: 10, includeExternal: false, verbose: true, log: m => messages.Add(m));
            extractor.Extract(ctx);
            Assert.Contains(messages, m => m.Contains("ambiguous-root"));
    }

    [Fact]
    public void OverloadDisambiguation_ByArgCount()
    {
            var ctx = LoadContext();
            var extractor = new CallgraphExtractor(new[] { "TestCallgraph.RootClass.Over/1" }, maxDepth: 0, maxNodes: 10, includeExternal: false, verbose: false, log: null);
            extractor.Extract(ctx);
            var graph = extractor.Sections.Single().Graphs.Single();
            Assert.StartsWith("TestCallgraph.RootClass.Over(1)", graph.Root);
    }
}
