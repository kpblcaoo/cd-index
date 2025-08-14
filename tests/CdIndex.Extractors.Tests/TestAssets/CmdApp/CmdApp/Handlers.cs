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
public sealed class SuspiciousCommandHandler : ICommandHandler { public string CommandName { get { return "suspicious"; } } }
public sealed class SpamCommandHandler : ICommandHandler { public string CommandName => "spam"; }
public sealed class HamCommandHandler : ICommandHandler { public string CommandName => "ham"; }
public sealed class SayCommandHandler : ICommandHandler { public string CommandName => "say"; }
public sealed class CheckCommandHandler : ICommandHandler { public string CommandName => "check"; }
