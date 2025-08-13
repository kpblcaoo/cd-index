using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CdIndex.Roslyn;

public static class SolutionLoader
{
    public static async Task<RoslynContext> LoadSolutionAsync(string slnPath, string repoRoot, CancellationToken ct = default)
    {
        MsBuildBootstrap.EnsureRegistered();
        var workspace = MSBuildWorkspace.Create();
        var solution = await workspace.OpenSolutionAsync(slnPath, cancellationToken: ct);
        var compilations = await GetCompilationsAsync(solution, ct);
        return new RoslynContext(workspace, solution, compilations, repoRoot);
    }

    public static async Task<RoslynContext> LoadProjectAsync(string csprojPath, string repoRoot, CancellationToken ct = default)
    {
        MsBuildBootstrap.EnsureRegistered();
        var workspace = MSBuildWorkspace.Create();
        var project = await workspace.OpenProjectAsync(csprojPath, cancellationToken: ct);
        var solution = workspace.CurrentSolution;
        var compilations = await GetCompilationsAsync(solution, ct);
        return new RoslynContext(workspace, solution, compilations, repoRoot);
    }

    private static async Task<IReadOnlyDictionary<ProjectId, Compilation>> GetCompilationsAsync(Solution solution, CancellationToken ct)
    {
        var dict = new Dictionary<ProjectId, Compilation>();
        foreach (var project in solution.Projects)
        {
            var compilation = await project.GetCompilationAsync(ct);
            if (compilation != null)
                dict[project.Id] = compilation;
        }
        return dict;
    }
}
