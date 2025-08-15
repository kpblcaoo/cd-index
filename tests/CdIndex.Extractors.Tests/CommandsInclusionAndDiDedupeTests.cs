using System;
using System.Linq;
using System.Threading.Tasks;
using CdIndex.Extractors;
using CdIndex.Roslyn;
using Xunit;

namespace CdIndex.Extractors.Tests;

public sealed class CommandsInclusionAndDiDedupeTests
{
    private static string TestRepoRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "TestAssets"));
    private static string CmdSln => Path.Combine(TestRepoRoot, "CmdApp", "CmdApp.sln");
    private static string DiSln => Path.Combine(TestRepoRoot, "DiApp", "DiApp.sln");

    [Fact]
    public async Task Commands_Default_DoesNotInclude_Comparisons()
    {
        var ctx = await SolutionLoader.LoadSolutionAsync(CmdSln, TestRepoRoot);
        // Default extractor includes comparisons (legacy). New behavior: we simulate CLI gating by constructing with includeComparisons:false
        var extractor = new CommandsExtractor(
            routerNames: null,
            attrNames: null,
            caseInsensitive: false,
            allowBare: false,
            normalizeTrim: true,
            normalizeEnsureSlash: true,
            includeRouter: true,
            includeAttributes: true,
            includeComparisons: false,
            allowRegex: "^/[a-z][a-z0-9_]*$");
        extractor.Extract(ctx);
        // Commands that only appear via comparison patterns in Program.cs: /help, /about, /ban
        Assert.DoesNotContain(extractor.Items, i => i.Command == "/help");
        Assert.DoesNotContain(extractor.Items, i => i.Command == "/about");
        Assert.DoesNotContain(extractor.Items, i => i.Command == "/ban");
    }

    [Fact]
    public async Task Commands_WithComparison_Includes()
    {
        var ctx = await SolutionLoader.LoadSolutionAsync(CmdSln, TestRepoRoot);
        var extractor = new CommandsExtractor(
            routerNames: null,
            attrNames: null,
            caseInsensitive: false,
            allowBare: false,
            normalizeTrim: true,
            normalizeEnsureSlash: true,
            includeRouter: true,
            includeAttributes: true,
            includeComparisons: true,
            allowRegex: "^/[a-z][a-z0-9_]*$");
        extractor.Extract(ctx);
        Assert.Contains(extractor.Items, i => i.Command == "/help");
        Assert.Contains(extractor.Items, i => i.Command == "/about");
        Assert.Contains(extractor.Items, i => i.Command == "/ban");
    }

    [Fact]
    public async Task Di_Dedupe_Removes_Duplicates_And_Filters_Exception()
    {
        var ctx = await SolutionLoader.LoadSolutionAsync(DiSln, TestRepoRoot);
        // Run extraction with dedupe
        var extractor = new DiExtractor(diDedupe:true);
        extractor.Extract(ctx);
        // Assert heuristic: no implementation ending with Exception
        Assert.DoesNotContain(extractor.Registrations, r => r.Implementation.EndsWith("Exception", StringComparison.Ordinal));
        // Assert no duplicate (Interface, Implementation, Lifetime) tuples
        var keys = extractor.Registrations.Select(r => (r.Interface, r.Implementation, r.Lifetime)).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
    }
}
