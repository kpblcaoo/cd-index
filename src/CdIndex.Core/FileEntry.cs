namespace CdIndex.Core;

public sealed record FileEntry(
    string Path,
    string Kind,
    int Loc,
    string Sha256
);
