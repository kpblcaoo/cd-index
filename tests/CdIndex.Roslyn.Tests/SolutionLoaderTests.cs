using System.Threading.Tasks;
using Xunit;
using CdIndex.Roslyn;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.IO;

public class SolutionLoaderTests
{
    private static string TestRepoRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "TestAssets"));
    private static string SlnPath => Path.Combine(TestRepoRoot, "MiniHostApp.sln");
    private static string CsprojPath => Path.Combine(TestRepoRoot, "MiniHostApp", "MiniHostApp.csproj");

    [Fact]
    public async Task LoadSolutionAsync_LoadsProjectsAndCompilations()
    {
        var ctx = await SolutionLoader.LoadSolutionAsync(SlnPath, TestRepoRoot);
        Assert.NotNull(ctx.Solution);
        Assert.NotEmpty(ctx.Compilations);
    }

    [Fact]
    public async Task GetLocation_ReturnsRepoRelativePathAndLine()
    {
        var ctx = await SolutionLoader.LoadSolutionAsync(SlnPath, TestRepoRoot);
        var programDoc = ctx.Solution.Projects
            .SelectMany(p => p.Documents)
            .FirstOrDefault(d => d.Name == "Program.cs");
        if (programDoc is null)
            throw new Xunit.Sdk.XunitException("Program.cs not found in loaded solution.");
        var root = await programDoc.GetSyntaxRootAsync();
        if (root is null)
            throw new Xunit.Sdk.XunitException("SyntaxRoot not found for Program.cs.");
        var mainMethod = root.DescendantNodes().OfType<MethodDeclarationSyntax>().First(m => m.Identifier.Text == "Main");
        var (path, line) = LocationUtil.GetLocation(mainMethod, ctx);
        Assert.Equal("MiniHostApp/Program.cs", path);
        Assert.True(line > 0);
    }

    [Fact]
    public async Task LoadSolutionAsync_InvalidPath_Throws()
    {
        await Assert.ThrowsAsync<System.IO.FileNotFoundException>(async () =>
        {
            await SolutionLoader.LoadSolutionAsync("bad_path.sln", TestRepoRoot);
        });
    }

    [Fact]
    public async Task LoadProjectAsync_WorksAnalogously()
    {
        var ctx = await SolutionLoader.LoadProjectAsync(CsprojPath, TestRepoRoot);
        Assert.NotNull(ctx.Solution);
        Assert.NotEmpty(ctx.Compilations);
    }
}
