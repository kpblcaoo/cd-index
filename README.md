# cd-index

.NET 9 CLI tool that scans a C# solution/project and emits a deterministic JSON index (tree, DI, entrypoints, configs, commands, message flow, and more forthcoming) according to `schema/project_index.schema.json`.

## Features (P1 additions)
- Config extractor (`--scan-configs` + `--env-prefix`): collects environment variable keys (by prefix) and referenced app config interface properties.
- Commands extractor (`--scan-commands`): collects chat/command registrations (`/start`, etc.) from gated sources:
	- router registrations (default, gate with `--commands-include router`)
	- handler attributes (default, gate with `--commands-include attributes`)
	- simple comparison patterns (opt-in via `--commands-include comparison`)
	- source gating: omit a source by excluding it from the comma list (e.g. `--commands-include router,attributes` prevents comparison-derived constants leaking).
	- optional case-insensitive canonical conflict grouping when `--commands-dedup case-insensitive`.
	- regex whitelist (default `^/[a-z][a-z0-9_]*$`) filters out improbable tokens (e.g. `/X-Service-Token`). Future flag `--commands-allow-regex` will allow override (currently internal; set to relaxed by editing code if needed).
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

# Commands excluding comparison (explicitly list defaults) – equivalent to default
cd-index scan --sln path/to/CmdApp.sln --scan-commands --commands-include router,attributes --out cmd.json

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

## Ignoring Files & Extensions
- `--ignore` accepts comma or space separated tokens and can be repeated.
- `--gitignore` merges simple directory/file suffix patterns from `.gitignore` (comments `#` and negations `!` skipped; wildcard segments after the first `*` truncated in P1 simplification).
- `--ext` accepts comma-separated list (with or without leading dots) and can be repeated; duplicates removed.
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

Lowercase-regex accepted commands only (simulate by filtering items not matching pattern):
```
jq '.Commands.Items | map(select(.Command|test("^/[a-z][a-z0-9_]*$")))' index.json
```
