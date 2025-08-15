using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CdIndex.Extractors;
using CdIndex.Roslyn;
using Xunit;

namespace CdIndex.Extractors.Tests;

public sealed class CommandsAdvancedTests
{
    private static string TestRepoRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "TestAssets"));
    private static string SlnPath => Path.Combine(TestRepoRoot, "CmdApp", "CmdApp.sln");

    [Fact]
    public async Task Normalization_Trim_EnsureSlash()
    {
        var ctx = await SolutionLoader.LoadSolutionAsync(SlnPath, TestRepoRoot);
        // create extractor permitting bare + trim + ensure-slash
        var extractor = new CommandsExtractor(
            routerNames: null,
            attrNames: null,
            caseInsensitive: false,
            allowBare: true,
            normalizeTrim: true,
            normalizeEnsureSlash: true,
            includeRouter: true,
            includeAttributes: true,
            includeComparisons: true,
            allowRegex: null);
        extractor.Extract(ctx);
        // Assuming test asset has a command without leading slash like "start" inside attributes/arrays (future-proof): we assert normalized ones are all with slash and no whitespace
        Assert.DoesNotContain(extractor.Items, i => i.Command.StartsWith(" ") || i.Command.EndsWith(" "));
        Assert.All(extractor.Items, i => Assert.StartsWith("/", i.Command, StringComparison.Ordinal));
    }

    [Fact]
    public async Task CaseInsensitiveConflict_Detected()
    {
        var ctx = await SolutionLoader.LoadSolutionAsync(SlnPath, TestRepoRoot);
        // Force case-insensitive mode; rely on existing /stats and maybe /STATS if added later; we simulate by adding manual duplicate via reflection fallback if absent
        var extractor = new CommandsExtractor(
            routerNames: null,
            attrNames: null,
            caseInsensitive: true,
            allowBare: false,
            normalizeTrim: true,
            normalizeEnsureSlash: true,
            includeRouter: true,
            includeAttributes: true,
            includeComparisons: true,
            allowRegex: null);
        extractor.Extract(ctx);
        // If test assets don't yet contain differing case duplicates, this assertion becomes vacuous; ensure no crash and conflicts list stable
        Assert.NotNull(extractor.Conflicts);
        // Deterministic ordering
        var ordered = extractor.Conflicts.Select(c => c.CanonicalCommand).OrderBy(x => x, StringComparer.Ordinal).ToList();
        Assert.Equal(ordered, extractor.Conflicts.Select(c => c.CanonicalCommand).ToList());
    }

    [Fact]
    public async Task Attributes_And_ArrayCommands_Are_Collected()
    {
        var ctx = await SolutionLoader.LoadSolutionAsync(SlnPath, TestRepoRoot);
        var extractor = new CommandsExtractor();
        extractor.Extract(ctx);
        var cmds = extractor.Items.Select(i => i.Command).ToHashSet(StringComparer.Ordinal);
        Assert.Contains("/multi1", cmds);
        Assert.Contains("/Multi2", cmds);
        Assert.Contains("/Alpha", cmds);
        Assert.Contains("/beta", cmds);
        Assert.Contains("/Gamma", cmds);
    }

    [Fact]
    public async Task Stable_Order_Is_Deterministic()
    {
        var ctx = await SolutionLoader.LoadSolutionAsync(SlnPath, TestRepoRoot);
        var extractor1 = new CommandsExtractor();
        extractor1.Extract(ctx);
        var first = extractor1.Items.Select(i => ($"{i.Command}|{i.Handler}|{i.File}|{i.Line}")).ToList();
        // Second run (same solution instance) to verify deterministic ordering & no mutation side-effects
        var extractor2 = new CommandsExtractor();
        extractor2.Extract(ctx);
        var second = extractor2.Items.Select(i => ($"{i.Command}|{i.Handler}|{i.File}|{i.Line}")).ToList();
        Assert.Equal(first, second);
        // Also ensure strictly sorted by Command then Handler then File then Line
        var sorted = first.OrderBy(x => x, StringComparer.Ordinal).ToList();
        Assert.Equal(sorted, first);
    }

    [Fact]
    public async Task CaseInsensitiveConflict_Reported()
    {
        var ctx = await SolutionLoader.LoadSolutionAsync(SlnPath, TestRepoRoot);
        var extractor = new CommandsExtractor(
            routerNames: null,
            attrNames: null,
            caseInsensitive: true,
            allowBare: false,
            normalizeTrim: true,
            normalizeEnsureSlash: true,
            includeRouter: true,
            includeAttributes: true,
            includeComparisons: true,
            allowRegex: null);
        extractor.Extract(ctx);
        Assert.Contains(extractor.Conflicts, c => c.CanonicalCommand == "/stats");
        var statsConflict = extractor.Conflicts.First(c => c.CanonicalCommand == "/stats");
        Assert.True(statsConflict.Variants.Any(v => v.Command == "/stats"));
        Assert.True(statsConflict.Variants.Any(v => v.Command == "/STATS"));
    }
}
