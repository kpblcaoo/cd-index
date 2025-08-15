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
        var debug = new System.Text.StringBuilder();
        var extractor = new DiExtractor(debugLog: debug);

        // Act
        extractor.Extract(ctx);

        foreach (var line in debug.ToString().Split('\n'))
            if (!string.IsNullOrWhiteSpace(line)) Console.WriteLine("[DBGREG] " + line.Trim());

        // Assert - Check registrations
        var registrations = extractor.Registrations;
        Assert.NotEmpty(registrations);

        // Diagnostic dump to aid debugging of missing factory registration
        Console.WriteLine("[REGDUMP] " + string.Join(" || ", registrations.Select(r => $"{r.Interface}->{r.Implementation}:{r.Lifetime}@{r.File}:{r.Line}")));
        // Assert no global:: prefixes leak
        Assert.DoesNotContain(registrations, r => r.Interface.Contains("global::") || r.Implementation.Contains("global::"));

        // Find specific registrations
        var fooSingleton = registrations.FirstOrDefault(r =>
            r.Interface == "TestDiApp.IFoo" && r.Implementation == "TestDiApp.Foo" && r.Lifetime == "Singleton");
        if (fooSingleton == null)
        {
            var dump = string.Join(" | ", registrations.Select(r => $"{r.Interface}->{r.Implementation}:{r.Lifetime}"));
            Assert.Fail("Missing fooSingleton. Dump: " + dump);
        }
        Assert.Contains("DiApp/Program.cs", fooSingleton.File);
        Assert.True(fooSingleton.Line > 0);

        var barScoped = registrations.FirstOrDefault(r =>
            r.Interface == "TestDiApp.IBar" && r.Implementation == "TestDiApp.Bar" && r.Lifetime == "Scoped");
        Assert.NotNull(barScoped);
        Assert.Contains("DiApp/Program.cs", barScoped.File);

        var bazTransient = registrations.FirstOrDefault(r =>
            r.Interface == "TestDiApp.Baz" && r.Implementation == "TestDiApp.Baz" && r.Lifetime == "Transient");
        Assert.NotNull(bazTransient);
        Assert.Contains("DiApp/Program.cs", bazTransient.File);

        // Factory registration - should be IFoo -> Foo or IFoo -> (factory)
        var fooFactory = registrations.Where(r =>
            r.Interface == "TestDiApp.IFoo" && r.Lifetime == "Singleton" && r != fooSingleton).FirstOrDefault();
        Assert.NotNull(fooFactory);
        Assert.Equal("TestDiApp.Foo", fooFactory.Implementation);
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

        var myHosted = hostedServices.FirstOrDefault(h => h.Type.EndsWith("MyHosted"));
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
        var extractor = new ConfigExtractor(new[] { "DOORMAN_", "MYAPP_" });
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
        // constants + variable handler
        Assert.Contains(items, i => i.Command == "/ping" && i.Handler == "PingHandler");
        // dedup: /start should not appear twice with same handler
        Assert.True(items.Count(i => i.Command == "/start" && i.Handler == "StartHandler") == 1, "/start duplicate detected");
        // Sorted
        var sorted = items.OrderBy(i => i.Command, StringComparer.Ordinal).ThenBy(i => i.Handler, StringComparer.Ordinal).ToList();
        Assert.Equal(sorted.Select(x => x.Command + "|" + x.Handler), items.Select(x => x.Command + "|" + x.Handler));
    }

    [Fact]
    public async Task CommandsExtractor_CustomRouterNames()
    {
        var ctx = await SolutionLoader.LoadSolutionAsync(SlnPath, TestRepoRoot);
        // Provide a custom list including existing defaults to ensure no regression
        var extractor = new CommandsExtractor(new[] { "Map", "Register", "Add", "On", "Route", "Bind", "Hook" });
        extractor.Extract(ctx);
        var items = extractor.Items;
        Assert.Contains(items, i => i.Command == "/stats" && i.Handler == "StatsHandler");
    }

    [Fact]
    public async Task CommandsExtractor_PropertyBasedHandlers()
    {
        var ctx = await SolutionLoader.LoadSolutionAsync(SlnPath, TestRepoRoot);
        var extractor = new CommandsExtractor();
        extractor.Extract(ctx);
        var items = extractor.Items;
        // Property-based handlers should have been discovered and normalized with leading slash
        Assert.Contains(items, i => i.Command == "/start" && i.Handler == "StartCommandHandler");
        Assert.Contains(items, i => i.Command == "/stats" && i.Handler == "StatsCommandHandler");
        Assert.Contains(items, i => i.Command == "/stats" && i.Handler == "StatsAliasCommandHandler");
        Assert.Contains(items, i => i.Command == "/suspicious" && i.Handler == "SuspiciousCommandHandler");
        Assert.Contains(items, i => i.Command == "/spam" && i.Handler == "SpamCommandHandler");
        Assert.Contains(items, i => i.Command == "/ham" && i.Handler == "HamCommandHandler");
        Assert.Contains(items, i => i.Command == "/say" && i.Handler == "SayCommandHandler");
        Assert.Contains(items, i => i.Command == "/check" && i.Handler == "CheckCommandHandler");
        // Ensure duplicates are only by (command, handler) pair: both stats handlers present plus original StatsHandler
        var statsHandlers = items.Where(i => i.Command == "/stats").Select(i => i.Handler).ToHashSet(StringComparer.Ordinal);
        Assert.True(statsHandlers.Contains("StatsHandler"));
        Assert.True(statsHandlers.Contains("StatsCommandHandler"));
        Assert.True(statsHandlers.Contains("StatsAliasCommandHandler"));
    }
}

public sealed class FlowExtractorTests
{
    private static string TestRepoRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "TestAssets"));
    private static string SlnPath => Path.Combine(TestRepoRoot, "FlowApp", "FlowApp.sln");
    private const string SwitchFixtureCode = @"public sealed class SwitchHandler {\n  public void HandleAsync() {\n    if (A()) return;\n    if (B()) return;\n    switch(State()) {\n      case 0: _processor.Process(); break;\n      case 1: _dispatcher.Dispatch(); break;\n      case 2: _manager.Run(); break;\n    }\n    _service.Do();\n  }\n  int State()=>0; bool A()=>false; bool B()=>false; private readonly DemoProcessor _processor=new(); private readonly EventDispatcher _dispatcher=new(); private readonly TaskManager _manager=new(); private readonly CoreService _service=new(); }\npublic sealed class DemoProcessor { public void Process(){} }\npublic sealed class EventDispatcher { public void Dispatch(){} }\npublic sealed class TaskManager { public void Run(){} }\npublic sealed class CoreService { public void Do(){} }";

    [Fact]
    public async Task FlowExtractor_ExtractsExpectedSequence()
    {
        var ctx = await SolutionLoader.LoadSolutionAsync(SlnPath, TestRepoRoot);
        var extractor = new FlowExtractor("MessageHandler", "HandleAsync");
        extractor.Extract(ctx);
        var nodes = extractor.Nodes;
        // New behavior: no explicit return nodes inside guards, so expected sequence shrinks
        // Collapse heuristic turns 'if(IsCommand()){ Router.Handle(); return; }' into only delegate (no guard for that one)
        string[] expectedKinds = { "guard", "guard", "delegate", "guard", "delegate", "delegate" };
        string[] expectedDetailsStarts = { "IsWhitelisted()", "IsDisabled()", "Router.Handle", "IsPrivate()", "JoinFacade.Handle", "ModerationService.Check" };
        Assert.Equal(expectedKinds.Length, nodes.Count);
        for (int i = 0; i < expectedKinds.Length; i++)
        {
            Assert.Equal(expectedKinds[i], nodes[i].Kind);
            Assert.StartsWith(expectedDetailsStarts[i], nodes[i].Detail);
            Assert.True(nodes[i].Order == i);
            Assert.False(System.IO.Path.IsPathRooted(nodes[i].File));
        }
    }

    [Fact]
    public async Task FlowExtractor_AsyncTask_Method()
    {
        var ctx = await SolutionLoader.LoadSolutionAsync(SlnPath, TestRepoRoot);
        var extractor = new FlowExtractor("MessageHandlerAsync", "HandleAsync");
        extractor.Extract(ctx);
        Assert.NotEmpty(extractor.Nodes);
        Assert.Contains(extractor.Nodes, n => n.Kind == "guard");
        Assert.Contains(extractor.Nodes, n => n.Kind == "delegate");
    }

    [Fact]
    public async Task FlowExtractor_ValueTask_Method()
    {
        var ctx = await SolutionLoader.LoadSolutionAsync(SlnPath, TestRepoRoot);
        var extractor = new FlowExtractor("MessageHandlerVT", "HandleAsync");
        extractor.Extract(ctx);
        Assert.NotEmpty(extractor.Nodes);
        Assert.Contains(extractor.Nodes, n => n.Kind == "guard");
    }

    [Fact]
    public async Task FlowExtractor_Fallback_SimpleName_ChoosesDeterministic()
    {
        var ctx = await SolutionLoader.LoadSolutionAsync(SlnPath, TestRepoRoot);
        var extractor = new FlowExtractor("MessageHandler", "HandleAsync"); // ambiguous simple name
        extractor.Extract(ctx);
        // Fallback should choose the deterministic (global namespace) handler variant producing 6 nodes
        Assert.Equal(6, extractor.Nodes.Count);
    }

    // Placeholder for future Switch & instance invocation test removed for stability.

    [Fact]
    public async Task FlowExtractor_Error_On_MissingType()
    {
        var ctx = await SolutionLoader.LoadSolutionAsync(SlnPath, TestRepoRoot);
        var ex = Assert.Throws<InvalidOperationException>(() => new FlowExtractor("NoSuchHandler", "HandleAsync").Extract(ctx));
        Assert.Contains("flow handler type not found", ex.Message);
    }

    [Fact]
    public async Task FlowExtractor_Error_On_MissingMethod()
    {
        var ctx = await SolutionLoader.LoadSolutionAsync(SlnPath, TestRepoRoot);
        var ex = Assert.Throws<InvalidOperationException>(() => new FlowExtractor("MessageHandler", "DoWork").Extract(ctx));
        Assert.Contains("flow method not found", ex.Message);
    }
}
