namespace CdIndex.Cli.Config;

public sealed class ScanConfig
{
    public ScanSection Scan { get; set; } = new();
    public TreeSection Tree { get; set; } = new();
    public DiSection DI { get; set; } = new();
    public CommandsSection Commands { get; set; } = new();
    public FlowSection Flow { get; set; } = new();

    public static ScanConfig Defaults() => new()
    {
        Scan = new ScanSection
        {
            Ignore = new() { "bin", "obj", ".git", "logs", "StrykerOutput", "tmp" },
            Ext = new() { ".cs", ".csproj", ".sln", ".json", ".yaml", ".yml" },
            NoTree = false,
            Sections = new() { "Tree", "DI", "Entrypoints", "Configs", "Commands", "MessageFlow" }
        },
        Tree = new TreeSection { LocMode = "physical", UseGitignore = true },
        DI = new DiSection { Dedupe = "keep-all" },
        Commands = new CommandsSection
        {
            Include = new() { "router", "attributes" },
            AttrNames = new() { "Command", "Commands" },
            RouterNames = new() { "Map", "Register", "Add", "On", "Route", "Bind" },
            Normalize = new() { "trim", "ensure-slash" },
            AllowRegex = "^/[a-z][a-z0-9_]*$",
            Dedup = "case-sensitive",
            Conflicts = "warn"
        },
        Flow = new FlowSection
        {
            DelegateSuffixes = new() { "Router", "Facade", "Service", "Dispatcher", "Processor", "Manager", "Module" },
            Method = "HandleAsync"
        }
    };
}

public sealed class ScanSection
{
    public List<string> Ignore { get; set; } = new();
    public List<string> Ext { get; set; } = new();
    public bool NoTree { get; set; }
    public List<string> Sections { get; set; } = new();
}

public sealed class TreeSection
{
    public string LocMode { get; set; } = "physical"; // physical|logical
    public bool UseGitignore { get; set; } = true;
}

public sealed class DiSection
{
    public string Dedupe { get; set; } = "keep-all"; // keep-all|keep-first
}

public sealed class CommandsSection
{
    public List<string> Include { get; set; } = new();
    public List<string> AttrNames { get; set; } = new();
    public List<string> RouterNames { get; set; } = new();
    public List<string> Normalize { get; set; } = new();
    public string? AllowRegex { get; set; }
    public string Dedup { get; set; } = "case-sensitive"; // case-sensitive|case-insensitive
    public string Conflicts { get; set; } = "warn"; // warn|error|ignore
}

public sealed class FlowSection
{
    public string? Handler { get; set; }
    public string Method { get; set; } = "HandleAsync";
    public List<string> DelegateSuffixes { get; set; } = new();
}
