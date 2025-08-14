# Commands Extraction (Experimental)

CLI flags (all optional unless noted):

- --scan-commands enable commands section extraction.
- --commands-router-names <names> override router method names (comma/space list). Defaults: Map, Register, Add, On, Route, Bind.
- --commands-attr-names <names> attribute simple names (without Attribute) to scan for handler classes. Defaults: Command, Commands.
- --commands-normalize <rules> normalization rules (comma/space). Supported: trim, ensure-slash. Default applies both.
- --commands-dedup <mode> case-sensitive (default) or case-insensitive.
- --commands-conflicts <mode> conflict reporting when case-insensitive dedup active: warn (default), error, ignore.
- --commands-conflict-report <file> write JSON array with detected conflicts (no schema change to main index).

Conflict report JSON shape example:
[
  {
    "canonical": "stats",
    "variants": [
      { "command": "/stats", "handler": "StatsHandler", "file": "src/.../StatsHandler.cs", "line": 12 },
      { "command": "/Stats", "handler": "StatsHandler", "file": "src/.../StatsHandler.cs", "line": 20 }
    ]
  }
]

Determinism:
- Commands list sorted by command, handler, file, line.
- Conflicts sorted by canonical key; variants sorted by command.

Exit Codes:
- 0: success (warn/ignore)
- 12: conflicts + --commands-conflicts error

Notes:
- No schema changes; conflicts external only.
- Normalization (trim + ensure-slash) applied before dedup.
- Bare commands accepted only when ensure-slash normalization active.
