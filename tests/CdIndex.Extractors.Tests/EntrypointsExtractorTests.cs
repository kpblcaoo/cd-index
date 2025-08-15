using CdIndex.Extractors;
using CdIndex.Roslyn;
using Xunit;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace CdIndex.Extractors.Tests;

public class EntrypointsExtractorTests
{
    private static string TestRepoRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "TestAssets"));
    private static string SlnPath => Path.Combine(TestRepoRoot, "DiApp", "DiApp.sln");

    [Fact]
    public async Task Extract_Finds_ProgramMain_And_HostedServices()
    {
        Assert.True(File.Exists(SlnPath), $"Solution not found: {SlnPath}");
        MsBuildBootstrap.EnsureRegistered();
        var ctx = await SolutionLoader.LoadSolutionAsync(SlnPath, TestRepoRoot);

        var extractor = new EntrypointsExtractor();
        extractor.Extract(ctx);

        Assert.NotEmpty(extractor.Sections);
        var section = extractor.Sections.First(s => s.Project.Name == "DiApp");
        Assert.NotNull(section.ProgramMain);
        Assert.True(section.ProgramMain!.Line > 0);
        Assert.NotNull(section.ProgramMain.TypeName);
        Assert.Contains("Program", section.ProgramMain.TypeName);

        Assert.NotNull(section.HostedServices);
        // Hosted service types are fully-qualified (consistent with DI extractor)
        Assert.Contains(section.HostedServices, h => h.Type == "TestDiApp.MyHosted");

        // Deterministic sorting of hosted services
        var sorted = section.HostedServices
            .OrderBy(h => h.Type)
            .ThenBy(h => h.File)
            .ThenBy(h => h.Line)
            .ToList();
        Assert.Equal(sorted, section.HostedServices.ToList());
    }

    [Fact]
    public async Task Extract_Sorts_Sections_By_ProjectName_Then_File()
    {
        Assert.True(File.Exists(SlnPath));
        MsBuildBootstrap.EnsureRegistered();
        var ctx = await SolutionLoader.LoadSolutionAsync(SlnPath, TestRepoRoot);
        var extractor = new EntrypointsExtractor();
        extractor.Extract(ctx);

        var manual = extractor.Sections
            .OrderBy(s => s.Project.Name, StringComparer.Ordinal)
            .ThenBy(s => s.Project.File, StringComparer.Ordinal)
            .ToList();
        Assert.Equal(manual, extractor.Sections.ToList());
    }

    [Fact]
    public async Task Extract_Paths_Are_RepoRelative_And_Normalized()
    {
        MsBuildBootstrap.EnsureRegistered();
        var ctx = await SolutionLoader.LoadSolutionAsync(SlnPath, TestRepoRoot);
        var extractor = new EntrypointsExtractor();
        extractor.Extract(ctx);

        foreach (var sec in extractor.Sections)
        {
            Assert.DoesNotContain('\\', sec.Project.File);
            Assert.False(Path.IsPathRooted(sec.Project.File));
            if (sec.ProgramMain != null)
            {
                Assert.DoesNotContain('\\', sec.ProgramMain.File);
                Assert.False(Path.IsPathRooted(sec.ProgramMain.File));
            }
            foreach (var h in sec.HostedServices)
            {
                Assert.DoesNotContain('\\', h.File);
                Assert.False(Path.IsPathRooted(h.File));
            }
        }
    }
}
