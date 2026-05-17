# `!` Operator Audit — PR #3732 (`nullable4`)

## Summary

**Total: ~3,810 null-forgiving operators across 21 production-code projects.**

| Project | Count | Notes |
|---|---:|---|
| Libraries/Opc.Ua.Server | 904 | Late-init, lifecycle-dependent state |
| Applications/Quickstarts.Servers | 630 | Demo NodeManagers w/ late init |
| Stack/Opc.Ua.Types | 589 | Encoder/decoder, reflection helpers |
| Stack/Opc.Ua.Core | 544 | Encoding, transport buffers |
| Libraries/Opc.Ua.PubSub | 348 | EventArgs, decoder paths |
| Stack/Opc.Ua | 151 | Mix |
| Libraries/Opc.Ua.Client | 148 | Session lifecycle |
| Libraries/Opc.Ua.Gds.Server.Common | 119 | Cert lifecycle |
| Libraries/Opc.Ua.Configuration | 103 | Config loading |
| Other (11 projects) | 274 | <50 each |
| Libraries/Opc.Ua.Gds.Common, MinimalBoilerServer | 0 | Generated/empty |

## Pattern frequency (overlapping; rough)

| Pattern | Count |
|---|---:|
| `expr!` end-of-expression | 2,332 |
| `!.` chained access | 941 |
| `(T)expr!` | 388 |
| `null!` positional arg | 322 |
| `= null!` late-init | 270 |
| `(expr as T)!` | ~3 (down from 13 fixed) |

## Cluster analysis (15 clusters)

### Phase 1 — Quick wins, low risk (~325 `!`, ~9%)

1. **Encoder/decoder `null!` fieldName** (~250) — `WriteEncodeable(null!, ...)`. Make API param `string?`. Effort: 2d. Risk: very low.
2. **`Initialize(systemContext, null!, ...)` audit events** (~20) — Make `source` param nullable. Effort: 1d. Risk: very low.
3. **`ArraySegment<T>.Array!`** in TCP buffers (~50) — Add `GetArray()` extension w/ Debug.Assert. Effort: 1d. Risk: very low.
4. **`ToArray()!.Cast<T>`** (~5) — Replace with `OfType<T>()`. Effort: 1h. Risk: very low.
5. **2 latent bug `as T!` casts** — see below. Effort: 1h.

### Phase 2 — Medium impact, low risk (~480 `!`, ~13%)

6. **Late-init lifecycle properties** (~280) — `[MemberNotNull(nameof(Session))]` on `Attach()`/`Initialize()`/`StartUp()`. Effort: 2w. Risk: low.
7. **EventArgs late-init properties** (~120) — Use `required` (C# 11) or constructor injection. Effort: 1w. Risk: low.
8. **`Type.GetElementType()!`** post-IsArray (~40) — Pattern matching `is Type t`. Effort: 2d. Risk: very low.
9. **Dispose `m_field = null!`** (~30) — Make those fields nullable. Effort: 1w. Risk: very low.
10. **Reflection cache (`MakeGenericMethod`)** (~10) — Cache `MethodInfo` w/ `?? throw` at startup. Effort: 1d.

### Phase 3 — API evolution (next major) (~100 `!`)

11. **Obsolete API passthrough** (~15) — Accept until next major; `#pragma warning disable` if desired.
12. **`TypeInfo.GetDefaultValue` returning `(T)null!`** (~35) — Change return to nullable.
13. **Server lifecycle helpers** (~50) — Tighten method signatures where all callers pass non-null.

### Phase 4 — Long-term (~2,900 `!`)

14. **`!.` chained access** (~400 after Phase 1-3) — Most are symptoms of clusters #6/#7/#13; address root causes.
15. **Two-phase initialization refactor** — Architectural change; out of scope for this PR.

## 2 Latent bug candidates (recommend fixing)

### Bug A — `PubSubJsonDecoder.cs:928`
```csharp
serverIndex = ToServerIndex((serverUriToken as string)!);
```
If `serverUriToken` is not a `string`, `as` returns null, then `null!` lies to the compiler. `ToServerIndex` likely NREs on null. **Fix:** use `(string)serverUriToken` (throws cleanly) or `is string s` pattern.

### Bug B — `PubSubJsonDecoder.cs:1293`
```csharp
new Matrix((array.Value as Array)!, builtInType, dimensions)
```
Same pattern. The surrounding `try/catch (ArgumentException)` won't catch the resulting NRE. **Fix:** `(Array)array.Value` or pattern match.

## Recommended follow-up PRs

| PR | Phase | `!` removed | Effort | Risk |
|---|---|---:|---|---|
| `nullable-cleanup-1`: encoder fieldName + Initialize source | 1 | ~270 | 3d | Very low |
| `nullable-cleanup-2`: ArraySegment helper + OfType refactor + 2 latent-bug fixes | 1 | ~57 | 2d | None (one is bug fix) |
| `nullable-cleanup-3`: `[MemberNotNull]` on lifecycle methods | 2 | ~280 (justified) | 2w | Low |
| `nullable-cleanup-4`: required EventArgs properties | 2 | ~120 | 1w | Low |
| `nullable-cleanup-5`: Dispose fields nullable + GetElementType pattern | 2 | ~70 | 1w | Very low |
| `nullable-major`: TypeInfo defaults + obsolete API removal | 3 | ~100 | 1w | Major version |

**Total addressable: ~900 `!` (24%) via 6 follow-up PRs without architectural changes.**

The remaining ~2,900 (~76%) are deeply tied to OPC UA spec types, two-phase initialization patterns, and decoder semantics where null is a legitimate intermediate state — these largely should remain with justifying comments.
