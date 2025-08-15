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
	- regex whitelist (default `^/[a-z][a-z0-9_]*$`) filters out improbable tokens (e.g. `/X-Service-Token`); override via `--commands-allow-regex <pattern>` or TOML `commands.allowRegex`.
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

# Override command allow regex (allow uppercase & dashes)
cd-index scan --sln path/to/CmdApp.sln --scan-commands --commands-allow-regex '^/[A-Za-z][A-Za-z0-9_\-]*$' --out cmd.json

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

## Configuration (TOML)

You can manage defaults with a `cd-index.toml` (or `.cd-index.toml`) at the repo root, or point to another file with `--config path/to/file.toml`. Environment variable `CD_INDEX_CONFIG` is a fallback when no explicit / root file is found.

Precedence (highest wins):
1. CLI flags / options
2. TOML file values
3. Built-in defaults

Generate a starter file:
```
cd-index config init            # creates ./cd-index.toml (use --force to overwrite)
```

Show merged configuration (defaults + TOML; note CLI overrides are NOT applied in this print mode):
```
cd-index config print
```

Example snippet:
```toml
[scan]
ignore = ["bin","obj",".git","tmp"]
ext = [".cs",".json"]
sections = ["Tree","DI","Entrypoints","Configs","Commands"]

[tree]
locMode = "physical"
useGitignore = true

[di]
dedupe = "keep-first"            # change default behavior

[commands]
include = ["router","attributes","comparison"]
routerNames = ["Map","Register","Bind"]
attrNames = ["Command","Commands"]
normalize = ["trim","ensure-slash"]
allowRegex = "^/[A-Z]{3}$"       # only allow three uppercase letters
dedup = "case-insensitive"
conflicts = "error"

[flow]
handler = "MyNamespace.MessageHandler"
method = "HandleAsync"
delegateSuffixes = ["Router","Dispatcher","Processor"]
```

Notes:
- Lists (e.g. `commands.include`) are replaced wholesale when present in TOML; CLI supplying a list option overrides the list again.
- `--sections` or `--no-tree` fully override TOML `scan.sections` when provided.
- `--commands-allow-regex` overrides TOML `commands.allowRegex`.
- `--di-dedupe` (CLI) forces dedupe even if TOML sets `keep-all`.

### Diagnostics Codes
Verbose mode (`--verbose`) emits machine-parsable codes to stderr:

Code | Area | Meaning
---- | ---- | -------
CFG001 | config | No config located; using built-in defaults
CFG010 | config | Loaded config file
CFG100 | config | TOML parser diagnostic (non-fatal)
CFG900 | config | Load failure; fallback to defaults
CFG901 | config | TOML parse exception; fallback to defaults
CMD001 | commands | Enabled command sources list
CMD002 | commands | Dedupe mode in effect
CMD003 | commands | Custom allowRegex override in effect
CMD300 | commands | Command conflict (case-insensitive canonicalization)
FLW001 | flow | Flow disabled
FLW010 | flow | Flow extraction parameters (handler/method/suffixes)

You can grep stderr for these codes in CI for policy enforcement.

## Determinism
All emitted JSON collections are sorted (ordinal). Paths are repo-relative with forward slashes. Re-running the same command yields byte-identical output except `GeneratedAt` timestamp.
When `--di-dedupe` is set, the first occurrence of a (Interface,Implementation,Lifetime) triple is retained (stable first-wins) to avoid ordering jitter from different compilation/loading orders.

## Schema & Docs
Schema version: **1.2**

Breaking change vs 1.1: only `Meta` and `Project` are required. All other sections are optional and omitted when not requested or when they would be empty. The emitted `Meta.Sections` array (optional) lists the section names actually present (excluding `Project`).

Implications:
- No more noise of large empty arrays in neutral scans.
- Validation only enforces presence & shape of existing sections.
- Diff tooling (future) should default to intersecting available sections; callers can enforce a set with a `--sections` flag.

Typical `Meta.Sections` example:
```jsonc
"Meta": {
	"Version": "0.0.1-dev",
	"SchemaVersion": "1.2",
	"GeneratedAt": "2025-01-01T00:00:00Z",
	"Sections": ["DI","Entrypoints","Tree"]
}
```

If no optional sections were produced, `Sections` may be an empty array (or omitted).

Additional docs:
- `README.flow.md` – flow extractor patterns, resolution strategy, verbose diagnostics.
	- updated for configurable delegate suffixes & expanded patterns.

### Internal Architecture Notes
- Extractors implement `IExtractor` (non-generic) and, where they surface a homogeneous collection, also `IExtractor<T>` to enable generic tooling / shared test helpers without sacrificing backwards compatibility.
- Synchronous Roslyn access is centralized via `RoslynSync` to keep deterministic single-threaded behavior explicit and localize any future async migration.
- Path normalization is centralized (`PathEx.Normalize`) ensuring all stored file paths are repo-relative and forward-slash; avoid ad-hoc `Replace("\\", "/")` usage elsewhere.
- Deterministic ordering is consistently enforced using ordinal comparisons; helper expansion (`Orderer`) exists for future chained key sorts but core extractors keep explicit comparers for clarity.

These internal utilities are intentionally minimal to preserve the low-dependency surface and keep reasoning about determinism straightforward.

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

List top 10 commands (if present):
```
jq '.Commands.Items | .[:10]' index.json
```

List comparison-derived commands only (if section present and run with `--commands-include comparison`):
```
jq '.Commands.Items | map(select(.Origin == "Comparison"))' index.json
```

Show DI duplicates (if DI present; should be empty when using `--di-dedupe`):
```
jq '.DI.Registrations
 | group_by([.Interface,.Implementation,.Lifetime])
 | map(select(length>1))' index.json
```

Confirm no Exception implementations:
```
jq '.DI.Registrations | map(select(.Implementation|endswith("Exception")))' index.json
```

Count env config keys by prefix (if Configs present):
```
jq '.Configs.EnvKeys | group_by(.Prefix) | map({prefix:.[0].Prefix,count:length})' index.json
```

Lowercase-regex accepted commands only (simulate by filtering items not matching pattern):
```
jq '.Commands.Items | map(select(.Command|test("^/[a-z][a-z0-9_]*$")))' index.json
```
