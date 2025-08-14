# Flow Extraction

P1-B robustness improvements (#32).

Patterns (top-level only):
- guard: any if(condition) ... (Detail = condition.ToString())
- delegate: invocation where target expression ends with Facade / Service or equals Router (Detail = Type.Method)
- return: top-level `return;` (not inside if blocks)

Supported method return types: void, Task, ValueTask. Async methods supported.

Type resolution:
1. If fully-qualified name provided (contains '.'), attempt `GetTypeByMetadataName`.
2. Fallback: collect all classes whose simple name matches last identifier; pick deterministically by fully-qualified name (ordinal smallest).

Method resolution: name match + allowed return type. If multiple overloads, pick deterministically by full signature string ordinal.

Verbose (`--verbose`) emits:
```
[flow] type: Namespace.MessageHandler
[flow] method: Namespace.MessageHandler.HandleAsync()
[flow] nodes: 6
```
Or `0 nodes; nothing matched top-level patterns` when empty.

Errors:
- Missing type or method: tool exits with code 5 (usage error) and error message on stderr.

Determinism:
- Node order preserved as encountered.
- Type/method choice stable via ordinal sorting.

Limitations:
- No descent into nested blocks beyond top-level of method body.
- No expression-bodied method support yet.
- Return statements inside if-blocks suppressed (guard already represents branch exit pattern).

Future ideas:
- Configurable delegate pattern suffixes.
- Optional capture of returns inside guards.
