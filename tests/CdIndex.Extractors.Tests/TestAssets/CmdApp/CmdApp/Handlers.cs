// Test assets for property-based command extraction pattern.
// Pattern: classes implementing ICommandHandler (or ending with CommandHandler)
// with a CommandName property returning a string literal (without leading slash).

public interface ICommandHandler
{
    string CommandName { get; }
}

public sealed class StartCommandHandler : ICommandHandler { public string CommandName => "start"; }
public sealed class StatsCommandHandler : ICommandHandler { public string CommandName => "stats"; }
public sealed class StatsAliasCommandHandler : ICommandHandler { public string CommandName => "stats"; }
// Case variant to trigger CI conflict grouping
public sealed class STATSCommandHandler : ICommandHandler { public string CommandName => "STATS"; }
public sealed class SuspiciousCommandHandler : ICommandHandler { public string CommandName { get { return "suspicious"; } } }
public sealed class SpamCommandHandler : ICommandHandler { public string CommandName => "spam"; }
public sealed class HamCommandHandler : ICommandHandler { public string CommandName => "ham"; }
public sealed class SayCommandHandler : ICommandHandler { public string CommandName => "say"; }
public sealed class CheckCommandHandler : ICommandHandler { public string CommandName => "check"; }

// Attribute-based commands (multi & array) for tests
[Commands("multi1", "Multi2")]
public sealed class MultiCommandHandler : ICommandHandler { public string CommandName => "multi1"; }

[Command("Alpha")]
public sealed class AlphaCommandHandler : ICommandHandler { public string CommandName => "Alpha"; }

[Commands(new[]{"beta","Gamma"})]
public sealed class BetaGammaCommandHandler : ICommandHandler { public string CommandName => "beta"; }

// Attribute stubs (simplified) so test compiles
public sealed class CommandAttribute : System.Attribute { public CommandAttribute(string value){} }
public sealed class CommandsAttribute : System.Attribute { public CommandsAttribute(params string[] values){} }
