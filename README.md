# cd-index

.NET 9 CLI tool that scans a C# solution/project and emits a deterministic JSON index (tree, DI, entrypoints, configs, commands, message flow, and more forthcoming) according to `schema/project_index.schema.json`.

## Features (P1 additions)
- Config extractor (`--scan-configs` + `--env-prefix`): collects environment variable keys (by prefix) and referenced app config interface properties.
- Commands extractor (`--scan-commands`): collects chat/command registrations (`/start`, etc.) from router registrations and simple comparison patterns (with multi-alias & conflict detection).
- Message flow extractor (`--scan-flow --flow-handler <Type> [--flow-method <Method>]`): linearizes top-level guards, delegate calls, returns inside specified handler method (see `README.flow.md`).

## Usage

Basic scan:
```
cd-index scan --sln path/to/solution.sln --out index.json
```

Optional sections:
```
# Configs
cd-index scan --sln path/to/ConfApp.sln --scan-configs --env-prefix DOORMAN_ --env-prefix MYAPP_ --out conf.json

# Commands
cd-index scan --sln path/to/CmdApp.sln --scan-commands --out cmd.json

# Message Flow
cd-index scan --sln path/to/FlowApp.sln --scan-flow --flow-handler MessageHandler --flow-method HandleAsync --out flow.json
```

Self-check (deterministic minimal output):
```
cd-index --selfcheck --scan-tree-only
```

## Determinism
All emitted JSON collections are sorted (ordinal). Paths are repo-relative with forward slashes. Re-running the same command yields byte-identical output except `GeneratedAt` timestamp.

## Schema & Docs
See `schema/project_index.schema.json`. New sections (v1.1): `Configs`, `Commands`, `MessageFlow`.

Additional docs:
- `README.flow.md` – flow extractor patterns, resolution strategy, verbose diagnostics.

## Tests
Run all tests:
```
dotnet test
```
Key suites:
- `ConfigExtractorTests`
- `CommandsExtractorTests`
- `FlowExtractorTests`
- Determinism: `SelfCheckDeterminismTests`

## Roadmap
Further extractors (callgraphs, test detection) to follow; focus remains on deterministic, minimal, dependency-light implementation.
