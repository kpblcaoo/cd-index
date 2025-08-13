namespace CdIndex.Core;

public sealed record DiRegistration(
    string Interface,
    string Implementation,
    string Lifetime,
    string File,
    int Line
);

public sealed record HostedService(
    string Type,
    string File,
    int Line
);
