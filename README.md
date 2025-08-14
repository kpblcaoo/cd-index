# cd-index

.NET 9 CLI tool that scans a C# solution/project and emits a deterministic JSON index (tree, DI, entrypoints, configs, commands, message flow, and more forthcoming) according to `schema/project_index.schema.json`.

## Features (P1 additions)
- Config extractor (`--scan-configs` + `--env-prefix`): collects environment variable keys (by prefix) and referenced app config interface properties.
- Commands extractor (`--scan-commands`): collects chat/command registrations (`/start`, etc.) from:
	- router registrations (default)
	- handler attributes (default)
	- simple comparison patterns (opt-in via `--commands-include comparison`)
	- multi-alias & conflict detection (case-insensitive grouping when `--commands-dedup case-insensitive`)
- DI extractor: emits full interface & implementation display names; optional duplicate suppression via `--di-dedupe`; filters out Exception-derived implementations.
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

# Commands (router + attributes; comparison patterns disabled by default)
cd-index scan --sln path/to/CmdApp.sln --scan-commands --out cmd.json

# Commands including comparison-based discovery
cd-index scan --sln path/to/CmdApp.sln --scan-commands --commands-include comparison --out cmd.json

# Message Flow
cd-index scan --sln path/to/FlowApp.sln --scan-flow --flow-handler MessageHandler --flow-method HandleAsync --out flow.json

# Selective sections (light JSON):
cd-index scan --sln path/to/App.sln --sections DI,Entrypoints,Commands

# Shorthand without Tree:
cd-index scan --sln path/to/App.sln --no-tree
```

Self-check (deterministic minimal output):
```
cd-index --selfcheck --scan-tree-only
```

## Determinism
All emitted JSON collections are sorted (ordinal). Paths are repo-relative with forward slashes. Re-running the same command yields byte-identical output except `GeneratedAt` timestamp.
When `--di-dedupe` is set, the first occurrence of a (Interface,Implementation,Lifetime) triple is retained (stable first-wins) to avoid ordering jitter from different compilation/loading orders.

## Schema & Docs
See `schema/project_index.schema.json`. New sections (v1.1): `Configs`, `Commands`, `MessageFlow`.

Additional docs:
- `README.flow.md` – flow extractor patterns, resolution strategy, verbose diagnostics.
	- updated for configurable delegate suffixes & expanded patterns.

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
 - CLI section selection: `ScanSectionsTests`

## Roadmap
Further extractors (callgraphs, test detection) to follow; focus remains on deterministic, minimal, dependency-light implementation.

## Ignoring Files
- `--ignore` accepts comma or space separated tokens and can be repeated.
- `--gitignore` merges simple patterns from `.gitignore` (ignores comments `#` and negations `!`; trims after first `*` for simplicity P1).

## File Extensions
## Quick jq Verification
Examples for sanity-checking output JSON.

List top 10 commands:
```
jq '.Commands.Items | .[:10]' index.json
```

List comparison-derived commands only (after running with `--commands-include comparison`):
```
jq '.Commands.Items | map(select(.Origin == "Comparison"))' index.json
```

Show DI duplicates (should be empty when using `--di-dedupe`):
```
jq '.DI.Registrations
 | group_by([.Interface,.Implementation,.Lifetime])
 | map(select(length>1))' index.json
```

Confirm no Exception implementations:
```
jq '.DI.Registrations | map(select(.Implementation|endswith("Exception")))' index.json
```

Count env config keys by prefix:
```
jq '.Configs.EnvKeys | group_by(.Prefix) | map({prefix:.[0].Prefix,count:length})' index.json
```

- `--ext` accepts comma-separated list (with or without leading dots) and can be repeated; duplicates removed.
