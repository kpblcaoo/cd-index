using System.CommandLine;

var root = new RootCommand("root desc");

// simple command with inline option + argument
var scan = new Command("scan", "scan desc")
{
    new Option<string>("--path", () => ".", "path to scan"),
    new Argument<string>("pattern")
};
scan.AddAlias("sc");

// method-added option & argument defined separately
var validate = new Command("validate");
var configOpt = new Option<string>("--config");
var fileArg = new Argument<string>("file");
validate.AddOption(configOpt);
validate.AddArgument(fileArg);

// variable-defined command later augmented
var diff = new Command("diff");
diff.AddAlias("compare");
var depthOpt = new Option<int>("--depth");
diff.AddOption(depthOpt);

// nested command hierarchy (should still list each individually) - currently only top-level scanning
root.AddCommand(scan);
root.AddCommand(validate);
root.AddCommand(diff);

return 0;
