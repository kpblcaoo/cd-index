using System.IO;
using System.Threading.Tasks;
using CdIndex.Extractors;
using CdIndex.Roslyn;
using Xunit;

namespace CdIndex.Extractors.Tests;

public sealed class DiExtractorTests
{
    private static string TestRepoRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "TestAssets"));
    private static string SlnPath => Path.Combine(TestRepoRoot, "DiApp", "DiApp.sln");

    [Fact]
    public async Task Extract_DiApp_FindsAllRegistrations()
    {
        // Arrange
        var ctx = await SolutionLoader.LoadSolutionAsync(SlnPath, TestRepoRoot);
        var extractor = new DiExtractor();

        // Act
        extractor.Extract(ctx);

        // Assert - Check registrations
        var registrations = extractor.Registrations;
        Assert.NotEmpty(registrations);

        // Find specific registrations
        var fooSingleton = registrations.FirstOrDefault(r => 
            r.Interface == "IFoo" && r.Implementation == "Foo" && r.Lifetime == "Singleton");
        Assert.NotNull(fooSingleton);
        Assert.Contains("DiApp/Program.cs", fooSingleton.File);
        Assert.True(fooSingleton.Line > 0);

        var barScoped = registrations.FirstOrDefault(r => 
            r.Interface == "IBar" && r.Implementation == "Bar" && r.Lifetime == "Scoped");
        Assert.NotNull(barScoped);
        Assert.Contains("DiApp/Program.cs", barScoped.File);

        var bazTransient = registrations.FirstOrDefault(r => 
            r.Interface == "Baz" && r.Implementation == "Baz" && r.Lifetime == "Transient");
        Assert.NotNull(bazTransient);
        Assert.Contains("DiApp/Program.cs", bazTransient.File);

        // Factory registration - should be IFoo -> Foo or IFoo -> (factory)
        var fooFactory = registrations.Where(r => 
            r.Interface == "IFoo" && r.Lifetime == "Singleton" && r != fooSingleton).FirstOrDefault();
        Assert.NotNull(fooFactory);
        Assert.True(fooFactory.Implementation == "Foo" || fooFactory.Implementation == "(factory)");
    }

    [Fact]
    public async Task Extract_DiApp_FindsHostedServices()
    {
        // Arrange
        var ctx = await SolutionLoader.LoadSolutionAsync(SlnPath, TestRepoRoot);
        var extractor = new DiExtractor();

        // Act
        extractor.Extract(ctx);

        // Assert - Check hosted services
        var hostedServices = extractor.HostedServices;
        Assert.NotEmpty(hostedServices);

        var myHosted = hostedServices.FirstOrDefault(h => h.Type == "MyHosted");
        Assert.NotNull(myHosted);
        Assert.Contains("DiApp/Program.cs", myHosted.File);
        Assert.True(myHosted.Line > 0);
    }

    [Fact]
    public async Task Extract_DiApp_ResultsAreSorted()
    {
        // Arrange
        var ctx = await SolutionLoader.LoadSolutionAsync(SlnPath, TestRepoRoot);
        var extractor = new DiExtractor();

        // Act
        extractor.Extract(ctx);

        // Assert - Check sorting
        var registrations = extractor.Registrations;
        for (int i = 1; i < registrations.Count; i++)
        {
            var prev = registrations[i - 1];
            var curr = registrations[i];

            var interfaceCompare = StringComparer.Ordinal.Compare(prev.Interface, curr.Interface);
            if (interfaceCompare < 0) continue;
            if (interfaceCompare > 0) Assert.Fail("Registrations not sorted by Interface");

            var implCompare = StringComparer.Ordinal.Compare(prev.Implementation, curr.Implementation);
            if (implCompare < 0) continue;
            if (implCompare > 0) Assert.Fail("Registrations not sorted by Implementation");

            var fileCompare = StringComparer.Ordinal.Compare(prev.File, curr.File);
            if (fileCompare < 0) continue;
            if (fileCompare > 0) Assert.Fail("Registrations not sorted by File");

            Assert.True(prev.Line <= curr.Line, "Registrations not sorted by Line");
        }

        var hostedServices = extractor.HostedServices;
        for (int i = 1; i < hostedServices.Count; i++)
        {
            var prev = hostedServices[i - 1];
            var curr = hostedServices[i];

            var typeCompare = StringComparer.Ordinal.Compare(prev.Type, curr.Type);
            if (typeCompare < 0) continue;
            if (typeCompare > 0) Assert.Fail("HostedServices not sorted by Type");

            var fileCompare = StringComparer.Ordinal.Compare(prev.File, curr.File);
            if (fileCompare < 0) continue;
            if (fileCompare > 0) Assert.Fail("HostedServices not sorted by File");

            Assert.True(prev.Line <= curr.Line, "HostedServices not sorted by Line");
        }
    }

    [Fact]
    public async Task Extract_DiApp_PathsAreRepoRelativeAndNormalized()
    {
        // Arrange
        var ctx = await SolutionLoader.LoadSolutionAsync(SlnPath, TestRepoRoot);
        var extractor = new DiExtractor();

        // Act
        extractor.Extract(ctx);

        // Assert - Check path normalization
        foreach (var registration in extractor.Registrations)
        {
            Assert.DoesNotContain("\\", registration.File); // No backslashes
            Assert.False(Path.IsPathRooted(registration.File)); // Repo-relative
            Assert.Contains("/", registration.File); // Has forward slashes
        }

        foreach (var hostedService in extractor.HostedServices)
        {
            Assert.DoesNotContain("\\", hostedService.File); // No backslashes
            Assert.False(Path.IsPathRooted(hostedService.File)); // Repo-relative
            Assert.Contains("/", hostedService.File); // Has forward slashes
        }
    }

    [Fact]
    public async Task Extract_DiApp_FailsIfNoProjectsOrDocuments()
    {
        var ctx = await SolutionLoader.LoadSolutionAsync(SlnPath, TestRepoRoot);
        Assert.True(ctx.Solution.Projects.Any(), "No projects loaded from DiApp.sln. Ensure DiApp.sln builds in CI and TestAssets are present.");
        Assert.True(ctx.Solution.Projects.SelectMany(p => p.Documents).Any(), "No documents loaded from DiApp.sln. Ensure DiApp.sln builds in CI and TestAssets are present.");
    }

    [Fact]
    public async Task Extract_DiApp_DebugProjectAndDocumentCounts()
    {
        var ctx = await SolutionLoader.LoadSolutionAsync(SlnPath, TestRepoRoot);
        var projectCount = ctx.Solution.Projects.Count();
        var docCount = ctx.Solution.Projects.SelectMany(p => p.Documents).Count();
        Console.WriteLine($"[DiExtractorTests] Projects loaded: {projectCount}, Documents loaded: {docCount}");
        Assert.True(projectCount > 0, "No projects loaded from DiApp.sln. Check build and asset paths.");
        Assert.True(docCount > 0, "No documents loaded from DiApp.sln. Check build and asset paths.");
    }
}

public sealed class ConfigExtractorTests
{
    private static string TestRepoRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "TestAssets"));
    private static string SlnPath => Path.Combine(TestRepoRoot, "ConfApp", "ConfApp.sln");

    [Fact]
    public async Task ConfigExtractor_FindsEnvKeysAndAppProps()
    {
        var ctx = await SolutionLoader.LoadSolutionAsync(SlnPath, TestRepoRoot);
        var extractor = new ConfigExtractor();
        extractor.Extract(ctx);
        var section = extractor.CreateSection();
        Assert.Contains("DOORMAN_BOT_API", section.EnvKeys);
        Assert.Contains("DOORMAN_LOG_ADMIN_CHAT", section.EnvKeys);
        Assert.Contains("IAppConfig.AdminChatId", section.AppProps);
        Assert.Contains("IAppConfig.AiEnabled", section.AppProps);
        Assert.Contains("FeatureConfig.FeatureX", section.AppProps);
        // Sorted uniqueness
        var sortedEnv = section.EnvKeys.OrderBy(x => x, StringComparer.Ordinal).ToList();
        Assert.Equal(sortedEnv, section.EnvKeys.ToList());
    }

    [Fact]
    public async Task ConfigExtractor_CustomPrefix()
    {
        var ctx = await SolutionLoader.LoadSolutionAsync(SlnPath, TestRepoRoot);
        var extractor = new ConfigExtractor(new[]{"DOORMAN_", "MYAPP_"});
        extractor.Extract(ctx);
        var section = extractor.CreateSection();
        Assert.Contains("DOORMAN_BOT_API", section.EnvKeys);
    }
}

public sealed class CommandsExtractorTests
{
    private static string TestRepoRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "TestAssets"));
    private static string SlnPath => Path.Combine(TestRepoRoot, "CmdApp", "CmdApp.sln");

    [Fact]
    public async Task CommandsExtractor_FindsCoreCommands()
    {
        var ctx = await SolutionLoader.LoadSolutionAsync(SlnPath, TestRepoRoot);
        var extractor = new CommandsExtractor();
        extractor.Extract(ctx);
        var items = extractor.Items;
        Assert.Contains(items, i => i.Command == "/start" && i.Handler == "StartHandler");
        Assert.Contains(items, i => i.Command == "/stats" && i.Handler == "StatsHandler");
        Assert.Contains(items, i => i.Command == "/help" && i.Handler == null);
        Assert.Contains(items, i => i.Command == "/about" && i.Handler == null);
        Assert.Contains(items, i => i.Command == "/ban" && i.Handler == null);
        // Sorted
        var sorted = items.OrderBy(i => i.Command, StringComparer.Ordinal).ThenBy(i => i.Handler, StringComparer.Ordinal).ToList();
        Assert.Equal(sorted.Select(x => x.Command+"|"+x.Handler), items.Select(x => x.Command+"|"+x.Handler));
    }
}

public sealed class FlowExtractorTests
{
    private static string TestRepoRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "TestAssets"));
    private static string SlnPath => Path.Combine(TestRepoRoot, "FlowApp", "FlowApp.sln");

    [Fact]
    public async Task FlowExtractor_ExtractsExpectedSequence()
    {
        var ctx = await SolutionLoader.LoadSolutionAsync(SlnPath, TestRepoRoot);
        var extractor = new FlowExtractor("MessageHandler", "HandleAsync");
        extractor.Extract(ctx);
        var nodes = extractor.Nodes;
        Assert.True(nodes.Count >= 6, "Expected at least 6 nodes");
    string[] expectedKinds = { "guard", "guard", "delegate", "guard", "delegate", "delegate" };
    string[] expectedDetailsStarts = { "IsWhitelisted()", "IsDisabled()", "Router.Handle", "IsPrivate()", "JoinFacade.Handle", "ModerationService.Check" };
    for (int i = 0; i < expectedKinds.Length; i++)
        {
            Assert.Equal(expectedKinds[i], nodes[i].Kind);
            Assert.StartsWith(expectedDetailsStarts[i], nodes[i].Detail);
            Assert.True(nodes[i].Order == i);
            Assert.False(System.IO.Path.IsPathRooted(nodes[i].File));
        }
    }
}
