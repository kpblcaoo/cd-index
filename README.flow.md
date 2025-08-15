# Flow Extraction

Robust symbol-based flow linearization (#32) capturing high-level guard & delegate sequence inside a handler method.

## Node Kinds
- guard: top-level `if (condition)` (and currently those that immediately `return` or `throw`) – Detail = source text of condition.
- delegate: call whose target type name ends with one of configurable suffixes (default: `Router,Facade,Service,Dispatcher,Processor,Manager,Module`) or matches Router exactly; Detail = `Type.Method`.

No explicit `return` nodes emitted (branch-ending guards implicitly represent early exits). Throw-only guards are currently still emitted as guard (subject to refinement).

## Supported Constructs
Single linear pass over:
- Top-level statements in method body (including expression-bodied methods)
- Collapsed patterns: `if (cond) { Delegate.Call(); return; }` -> single delegate node (guard suppressed)
- Switch sections: first qualifying delegate invocation inside each `case`
- Local variable initializers invoking delegates
- Awaited invocations (`await FooService.BarAsync()`) captured as delegates
- Flattening one level of wrapping blocks: `try { ... }`, `using (...) { ... }`, extra `{}`
- Limited loop scan (for/foreach/while): inspects first expression statements for delegates/guards (no deep iteration logic)

## Type & Method Resolution
1. Fully-qualified handler name -> `GetTypeByMetadataName`.
2. Otherwise gather all types with matching simple name and choose deterministically by fully-qualified metadata name (ordinal smallest) to guard against ambiguities.
3. Method: match by name + allowed return type (`void`, `Task`, `ValueTask`, synchronous or async); pick deterministically if multiple overloads via full signature ordinal.

## Delegate Detection
Semantic (Roslyn) symbol analysis of invocation target's containing type. If its simple name ends with any configured suffix (case-sensitive Ordinal) it's a delegate node. Provide custom list via `--flow-delegate-suffixes "Router,Facade,..."` (comma/space separated).

## Determinism
- Discovery order is stable: traversal order over syntax with deterministic ordering of ambiguous symbol groups.
- File paths repo-relative, forward slashes.
- Node `Order` field increments sequentially.

## CLI Flags
```
--scan-flow
--flow-handler <TypeName>
--flow-method <MethodName> (default HandleAsync)
--flow-delegate-suffixes <list> (override defaults)
```

## Verbose Diagnostics
When `--verbose`:
```
[flow] type: Namespace.MessageHandler
[flow] method: Namespace.MessageHandler.HandleAsync()
[flow] nodes: 13 (guards=10 delegates=3 returns=0)
```

## Error Modes
- Missing handler type or method -> exit code 5 with error message.

## Current Limitations / Future Enhancements
- Guard emission includes `if` blocks that end with `throw` (could classify separately or filter).
- Only first delegate per switch case captured (others ignored for simplicity).
- Loop scanning shallow (first-level statements only).
- No capture of plain return nodes.
- No configuration yet to emit raw condition text vs simplified string (potential `--flow-debug`).

Planned refinements: optional throw node kind, guard filtering, deeper switch/loop coverage, debug dump of unclassified top-level statements.
