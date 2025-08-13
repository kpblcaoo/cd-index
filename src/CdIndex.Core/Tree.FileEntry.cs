namespace CdIndex.Core.Tree;

public sealed record FileEntry(
    string Path,
    string Sha256,
    int Loc,
    string Kind
);
