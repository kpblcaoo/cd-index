// Minimal CLI stub for --version
using System;
using System.CommandLine;
using System.CommandLine.Parsing;

namespace CdIndex.Cli;

class Program
{
    static int Main(string[] args)
    {
    const string ScanUsage = @"Usage: cd-index scan (--sln <file.sln> | --csproj <project.csproj>) [options]

Options:
  --sln <path>              Path to solution file (mutually exclusive with --csproj)
  --csproj <path>           Path to project file (mutually exclusive with --sln)
  --out <file>              Write JSON to file (default stdout)
  --ext <extension>         Additional file extension to include (repeatable)
  --ignore <glob|prefix>    Path prefix or suffix to ignore (repeatable)
  --loc-mode <physical|logical>  LOC counting mode (default physical)
  --scan-tree / --no-scan-tree            Enable/disable tree section (default on)
  --scan-di / --no-scan-di                Enable/disable DI extraction (default on)
  --scan-entrypoints / --no-scan-entrypoints  Enable/disable entrypoints (default on)
  --verbose               Verbose diagnostics to stderr
  -h, --help              Show this help
";

    bool IsHelp(string a) => a == "--help" || a == "-h" || a == "help";

        var versionOption = new Option<bool>("--version")
        {
            Description = "Show tool version and exit"
        };
        var selfCheckOption = new Option<bool>("--selfcheck")
        {
            Description = "Emit minimal deterministic JSON for self-check"
        };
        var scanTreeOnlyOption = new Option<bool>("--scan-tree-only")
        {
            Description = "Only scan tree section (for selfcheck)"
        };
        var scanDiOption = new Option<bool>("--scan-di")
        {
            Description = "Include DI extraction in selfcheck"
        };
        var scanEntrypointsOption = new Option<bool>("--scan-entrypoints")
        {
            Description = "Include Entrypoints extraction (Program.Main + HostedServices)"
        };
    var rootCommand = new RootCommand("cd-index CLI tool");
        rootCommand.Options.Add(versionOption);
        rootCommand.Options.Add(selfCheckOption);
        rootCommand.Options.Add(scanTreeOnlyOption);
        rootCommand.Options.Add(scanDiOption);
        rootCommand.Options.Add(scanEntrypointsOption);

        // Manual 'scan' command handling (simpler & stable across System.CommandLine betas)
        if (args.Length > 0 && string.Equals(args[0], "scan", StringComparison.OrdinalIgnoreCase))
        {
            // scan help
            if (args.Length == 1 || args.Skip(1).Any(IsHelp))
            {
                Console.WriteLine(ScanUsage);
                return 0;
            }
            var slnPath = (string?)null;
            var csprojPath = (string?)null;
            FileInfo? outFile = null;
            var exts = new List<string>();
            var ignores = new List<string>();
            var locMode = "physical";
            bool scanTree = true, scanDi = true, scanEntrypoints = true, verbose = false;
            for (int i = 1; i < args.Length; i++)
            {
                var a = args[i];
                switch (a)
                {
                    case "--sln": if (i + 1 < args.Length) slnPath = args[++i]; else return 5; break;
                    case "--csproj": if (i + 1 < args.Length) csprojPath = args[++i]; else return 5; break;
                    case "--out": if (i + 1 < args.Length) outFile = new FileInfo(args[++i]); else return 5; break;
                    case "--ext": if (i + 1 < args.Length) exts.Add(args[++i]); else return 5; break;
                    case "--ignore": if (i + 1 < args.Length) ignores.Add(args[++i]); else return 5; break;
                    case "--loc-mode": if (i + 1 < args.Length) locMode = args[++i]; else return 5; break;
                    case "--scan-tree": scanTree = true; break; // defaults true
                    case "--scan-di": scanDi = true; break;
                    case "--scan-entrypoints": scanEntrypoints = true; break;
                    case "--no-scan-tree": scanTree = false; break;
                    case "--no-scan-di": scanDi = false; break;
                    case "--no-scan-entrypoints": scanEntrypoints = false; break;
                    case "--verbose": verbose = true; break;
                    case "--help":
                    case "-h":
                    case "help":
                        Console.WriteLine(ScanUsage);
                        return 0;
                    default:
                        Console.Error.WriteLine($"Unknown option for scan: {a}");
                        return 5;
                }
            }
            var code = ScanCommand.Run(
                slnPath != null ? new FileInfo(slnPath) : null,
                csprojPath != null ? new FileInfo(csprojPath) : null,
                outFile,
                exts.ToArray(),
                ignores.ToArray(),
                locMode,
                scanTree,
                scanDi,
                scanEntrypoints,
                verbose);
            return code;
        }

        var parseResult = rootCommand.Parse(args);
        if (parseResult.GetValue(versionOption))
        {
            Console.WriteLine("cd-index v0.0.1-dev");
            return 0;
        }
        if (parseResult.GetValue(selfCheckOption))
        {
            var scanTreeOnly = parseResult.GetValue(scanTreeOnlyOption);
            var scanDi = parseResult.GetValue(scanDiOption);
            var scanEntrypoints = parseResult.GetValue(scanEntrypointsOption);
            EmitSelfCheck.Run(scanTreeOnly, scanDi, scanEntrypoints);
            return 0;
        }
        if (args.Length == 0 || parseResult.Errors.Count > 0 || args.Any(IsHelp))
        {
            Console.WriteLine("cd-index - project index tool\n\nCommands:\n  scan    Scan solution/project and emit JSON index\n\nOptions:\n  --version         Show tool version and exit\n  --selfcheck       Emit deterministic self-check JSON\n  --scan-tree-only  Only include Tree in selfcheck\n  --scan-di         Include DI in selfcheck\n  --scan-entrypoints Include Entrypoints in selfcheck\n  -h, --help        Show help\n\nRun 'cd-index scan --help' for scan options.");
            foreach (var error in parseResult.Errors)
                Console.Error.WriteLine(error.Message);
            return 1;
        }
        // ...дополнительная логика команд...
        return 0;
    }
}
