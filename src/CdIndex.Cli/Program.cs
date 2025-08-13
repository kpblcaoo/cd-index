// Minimal CLI stub for --version
using System;
using System.CommandLine;
using System.CommandLine.Parsing;

namespace CdIndex.Cli;

class Program
{
    static int Main(string[] args)
    {
        var versionOption = new Option<bool>("--version")
        {
            Description = "Show tool version and exit"
        };
        var rootCommand = new RootCommand("cd-index CLI tool");
        rootCommand.Options.Add(versionOption);

        var parseResult = rootCommand.Parse(args);
        if (parseResult.GetValue(versionOption))
        {
            Console.WriteLine("cd-index v0.0.1-dev");
            return 0;
        }
        if (args.Length == 0 || parseResult.Errors.Count > 0)
        {
            Console.WriteLine("Use --help to see available commands.");
            foreach (var error in parseResult.Errors)
                Console.Error.WriteLine(error.Message);
            return 1;
        }
        // ...дополнительная логика команд...
        return 0;
    }
}
