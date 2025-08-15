using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CdIndex.Roslyn;
using Xunit;

namespace CdIndex.Extractors.Tests;

public sealed class CliCommandsExtractorTests
{
    private static string TestRepoRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "TestAssets"));
    private static string SlnPath => Path.Combine(TestRepoRoot, "CliApp", "CliApp.sln");

    [Fact]
    public async Task Placeholder_Finds_Literal_Command_With_Initializer_Option_And_Argument()
    {
        var ctx = await SolutionLoader.LoadSolutionAsync(SlnPath, TestRepoRoot);
        // Use ScanCommand inline logic via reflection? For placeholder we replicate minimal logic? For now just ensure solution loads.
        Assert.True(ctx.Solution.Projects.Any(), "CliApp solution not loaded");
        var project = ctx.Solution.Projects.First();
        var docs = project.Documents.ToList();
        Assert.Contains(docs, d => d.Name == "Program.cs");
        // Future: after extractor refactor, instantiate and run to assert results.
    }
}
