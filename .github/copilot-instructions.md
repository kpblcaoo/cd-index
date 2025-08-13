# ​ Repository-wide Copilot Instructions

##  Context
You are working in the `cd-index` repo—a .NET 9-based project whose goal is to generate a deterministic JSON snapshot of project structure and dependency flows (tree, DI, entrypoints, config flags, etc.). Focus on minimal dependencies, SRP, reproducibility, stable JSON output and precise behavior.

##  Technology & Environment
- Target: **.NET 9**, SDK specified via `global.json`
- Modern C# (file-scoped namespaces, nullable enabled)
- Cross-platform (Windows/Linux). Normalize paths (`/`), use invariant culture, UTC timestamps.

##  Design Principles
- **Single Responsibility**: each module/project (Core, Roslyn, Extractors, Emit, CLI) has a clear boundary.
- **Determinism**: all output (e.g. JSON collections) must be sorted, locale-invariant, stable across runs.
- **Minimal dependencies**: no external packages unless necessary; prefer BCL and Roslyn workspace.
- **CLI UX**: predictable commands (`scan`, `validate`, `diff`), clean output, proper exit codes.

##  JSON & Emit Contracts
- Output strictly follows JSON schema (`project_index.schema.json`).
- Do *not* embed file contents—only metadata (sha256, loc, path).
- Paths are normalized to forward-slash (in repo-relative context).
- Dates/times in UTC ISO-8601; no culture-specific formats.

##  CI & Testing
- Every PR must pass build and tests; use consistent `.editorconfig`, code style.
- Write unit tests for core utilities: hashing, sorting, path normalization, Roslyn location resolution.
- Avoid snapshot-based testing where deterministic asserts suffice.

##  Error Handling & Logging
- Fail fast on fatal errors (e.g. cannot load SDK, sln), with clear message and non-zero exit code.
- Default logs minimal; verbose mode via `--verbose` flag if needed.
- No sensitive data should be output or stored.

##  Collaboration & PR Etiquette
- Use conventional commits (`feat:`, `fix:`, `chore:`).
- One feature per PR; include acceptance criteria and brief rationale.
- Prefer small, atomic changes—especially when evolving extractors or schema.

##  When in doubt
- Propose 2–3 options with pros/cons and surface potential side effects on determinism or CI.
- Prioritize correctness and reproducibility over bells-and-whistles.
