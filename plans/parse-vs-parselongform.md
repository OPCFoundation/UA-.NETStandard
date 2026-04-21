# Parse vs ParseLongForm — Analysis, Refactor & Coverage Plan

## Problem

`NodeId`, `ExpandedNodeId`, and `QualifiedName` each expose multiple `Parse`
overloads plus a `ParseLongForm(NamespaceTable, …)` overload. The behavioral
contract of each is subtly different. We need to:

1. Document the actual behavioral differences (input, behavior, output).
2. Decide whether the long-form parsers can share code without changing
   public semantics.
3. Add unit-test coverage for the `ParseLongForm` methods (currently
   **zero** direct tests in `Tests/Opc.Ua.Types.Tests`).

## Working assumptions

Confirmed by inspection of the current tree:

- **Zero external callers** of any `ParseLongForm` overload exist in the
  repository (only one internal cross-reference in an XML doc comment).
  Behavior may be tightened freely as long as the new behavior is the
  intended one.
- `IServiceMessageContext` is freely available — `ServiceMessageContext`
  lives in `Opc.Ua.Types` (Utils/ServiceMessageContext.cs:37) and exposes
  settable `NamespaceUris`/`ServerUris` properties, so a transient context
  can be constructed from the supplied table parameters at zero cost.
- The legacy "treat malformed remainder as a string identifier" fallback
  in `NodeId.ParseLongForm` (NodeId.cs:392-399) is **dropped from
  `ParseLongForm`** — the wrappers do not enable it. The behavior is
  still available as an opt-in `FallbackToStringIdentifier` flag on
  `NodeIdParsingOptions` for callers that explicitly need the legacy
  shape. Default is `false` everywhere.
- The lenient-URI behavior in `ExpandedNodeId.InternalTryParseWithContext`
  (silently keeps an unresolved nsu= URI as an absolute id, lines
  1494-1528) must be **opt-out** for `ParseLongForm` — see Option A below.

## Behavioral matrix

| Method | nsu= | svu=/svr= | n:shortcut | Lenient | Resolution source | Output URI form |
|---|---|---|---|---|---|---|
| `NodeId.Parse(string)` | ❌ throws | n/a | n/a | ❌ strict | none | n/a |
| `NodeId.Parse(IServiceMessageContext, …)` | ✅ | n/a | n/a | options-driven | context | local index |
| `NodeId.ParseLongForm(string, NamespaceTable)` | ✅ | n/a | n/a | ✅ unrecognized → string id | NamespaceTable | local index |
| `ExpandedNodeId.Parse(string)` | ✅ kept as URI | ✅ svr= kept as int | n/a | varies | none | URI string |
| `ExpandedNodeId.Parse(string, NamespaceTable)` → NodeId | resolves nsu→idx | n/a | n/a | n/a | NamespaceTable | local index |
| `ExpandedNodeId.Parse(string, NamespaceTable, NamespaceTable)` | translates ns | kept | n/a | n/a | two tables | translated URI |
| `ExpandedNodeId.Parse(IServiceMessageContext, …)` | ✅ | ✅ | n/a | options-driven | context | local index |
| `ExpandedNodeId.ParseLongForm(string, NamespaceTable, StringTable)` | ✅ | ✅ both | n/a | inherits NodeId leniency | tables | local index + server index |
| `QualifiedName.Parse(string)` | silently treated as name | n/a | ✅ | ✅ always succeeds | none | n/a |
| `QualifiedName.Parse(IServiceMessageContext, string, bool)` | ✅ | n/a | ✅ | ❌ throws | context | local index |
| `QualifiedName.ParseLongForm(string, NamespaceTable)` | ✅ | n/a | ✅ | ❌ throws | NamespaceTable | local index |

### One-line summary
- **`Parse`** = round-trip a self-formatted string (no external lookup).
- **`Parse(IServiceMessageContext, …)`** = full-featured parser with context
  table lookup and options.
- **`ParseLongForm`** = decode OPC UA Part 6 §5.1.12 wire-format text into a
  *local* indexed value using caller-supplied tables.

## Duplication identified

All three `ParseLongForm` methods independently re-implement prefix
stripping (`nsu=`, `svu=`, `svr=`) — and that exact same logic exists yet
again inside `NodeId.InternalTryParseWithContext`,
`ExpandedNodeId.InternalTryParseWithContext`, and
`QualifiedName.Parse(IServiceMessageContext, string, bool)`.

Net result: the parser logic is duplicated **at least twice** for every
prefix.

## One real behavioral difference vs the context parser (not a quirk)

`ExpandedNodeId.InternalTryParseWithContext` (lines 1494-1528) has a
silent-acceptance bug: when `nsu=<uri>;` is present and the URI does
**not** resolve (`GetIndex` returns `-1`), it falls through and builds an
absolute `ExpandedNodeId` keeping the unresolved URI string as
`IsAbsolute = true`. `ExpandedNodeId.ParseLongForm` on the same input
(correctly) throws `BadNodeIdInvalid`.

This is the strictness gap that `ParseLongForm` exists to enforce.

## Approach — Option A: tighten the shared parser, fold ParseLongForm into Parse

Add a single new option to the existing parser:

```csharp
public class NodeIdParsingOptions
{
    public bool UpdateTables { get; set; }
    public ushort[] NamespaceMappings { get; set; }
    public ushort[] ServerMappings { get; set; }

    /// <summary>
    /// When TRUE, an "nsu=" or "svu=" URI that cannot be resolved against
    /// the relevant table (and is not added because UpdateTables is FALSE)
    /// causes the parser to fail with BadNodeIdInvalid instead of silently
    /// returning an "absolute" ExpandedNodeId carrying the unresolved URI.
    /// </summary>
    public bool RequireResolvedUris { get; set; }

    /// <summary>
    /// When TRUE, if the identifier portion following an nsu= prefix
    /// cannot be parsed as a typed identifier (i=, s=, g=, b=), the
    /// remainder is wrapped as a string identifier instead of failing
    /// the parse. Preserves the legacy NodeId.ParseLongForm quirk.
    /// Default FALSE so that Parse(IServiceMessageContext, …) keeps its
    /// current strict behavior.
    /// </summary>
    public bool FallbackToStringIdentifier { get; set; }
}
```

Behavior:
- `RequireResolvedUris = false` (default) preserves all current
  `Parse(IServiceMessageContext, …)` semantics.
- `RequireResolvedUris = true` plus `UpdateTables = false` plus null
  mapping arrays = exactly what `ParseLongForm` should do.

Then collapse each `ParseLongForm` into a thin wrapper:

```csharp
// NodeId
public static NodeId ParseLongForm(string text, NamespaceTable namespaceTable)
{
    if (namespaceTable == null) throw new ArgumentNullException(nameof(namespaceTable));
    var ctx = new ServiceMessageContext { NamespaceUris = namespaceTable };
    var opts = new NodeIdParsingOptions
    {
        RequireResolvedUris = true,
        // FallbackToStringIdentifier intentionally left false —
        // ParseLongForm is strict; legacy quirk dropped.
    };
    return Parse(ctx, text, opts);
}

// ExpandedNodeId
public static ExpandedNodeId ParseLongForm(
    string text, NamespaceTable namespaceTable, StringTable serverUris = null)
{
    if (namespaceTable == null) throw new ArgumentNullException(nameof(namespaceTable));
    var ctx = new ServiceMessageContext
    {
        NamespaceUris = namespaceTable,
        ServerUris = serverUris ?? new StringTable()
    };
    var opts = new NodeIdParsingOptions { RequireResolvedUris = true };
    return Parse(ctx, text, opts);
}

// QualifiedName — Parse(context, text, updateTables) does not currently take
// NodeIdParsingOptions. Add a small overload that does, OR add a private
// "strict" flag, then route ParseLongForm through it.
public static QualifiedName ParseLongForm(string text, NamespaceTable namespaceTable)
{
    if (namespaceTable == null) throw new ArgumentNullException(nameof(namespaceTable));
    var ctx = new ServiceMessageContext { NamespaceUris = namespaceTable };
    return ParseStrict(ctx, text);  // new internal helper — see Phase 1
}
```

### Behavioral changes this introduces (intentional)

1. **NodeId.ParseLongForm** loses the legacy lenient string-id fallback.
   `nsu=<uri>;i=notanumber` → throws `BadNodeIdInvalid` instead of
   yielding `NodeId("i=notanumber", nsIndex)`. Callers that need the
   old behavior can opt back in via
   `NodeIdParsingOptions.FallbackToStringIdentifier = true` on the
   context-aware parser.
2. **ExpandedNodeId.Parse(IServiceMessageContext, …)** — when the new
   `RequireResolvedUris` flag is **off** (the default), behavior is
   unchanged. When ON, the silent-acceptance gap is closed.
3. **All three ParseLongForm overloads** become trivial wrappers; bodies
   shrink to ~5 lines each.
4. **`InternalTryParseWithContext`** in NodeId and ExpandedNodeId picks
   up two extra checks: when an `nsu=`/`svu=` lookup returns `-1`,
   honour `RequireResolvedUris` to decide between fall-through and
   parse-error; when the inner identifier fails to parse, honour
   `FallbackToStringIdentifier` to decide between wrapping the
   remainder as a string id and propagating the parse failure.

### Why Option A over an internal helper class

- Eliminates duplication **at the source** (the context parser already
  does the prefix stripping correctly; `ParseLongForm` was a parallel
  implementation).
- Surfaces strictness as an explicit, reusable option for other callers
  (validators, GDS, source generators) instead of hiding it behind a
  third entry point.
- Closes a real silent-acceptance gap in the lenient `ExpandedNodeId`
  context parser for any future caller that opts in.
- Matches the user's intent: "no caller today, can use
  `IServiceMessageContext`, can construct one from the table parameters."

Add a new test class in
`Tests/Opc.Ua.Types.Tests/BuiltIn/ParseLongFormTests.cs` (or extend the
existing `NodeIdTests.cs`/`ExpandedNodeIdTests.cs`/`QualifiedNameTests.cs`
files with a `#region ParseLongForm` block) to cover **all** of the
following — these are the tests Phase 3 needs to add (and Phase 1's
parser changes must not regress).

#### Common (each of the three types)
- Null/empty `text` → expected default (NodeId.Null / ExpandedNodeId.Null /
  QualifiedName(name="", ns=0)).
- `null` `namespaceTable` → `ArgumentNullException`.
- Bare identifier (no `nsu=`, no `n:`) → ns 0.
- `nsu=<uri>;<id>` with URI in the table → ns resolved to the table index.
- `nsu=<uri>;<id>` with URI **not** in the table → throws
  `ServiceResultException` (`BadNodeIdInvalid`) — the `RequireResolvedUris`
  contract.
- `nsu=` with **no `;` separator** → throws `ServiceResultException`.
- `nsu=` with percent-escaped URI (e.g. `nsu=urn:tag%3Bs;i=1`) → URI
  unescaped before lookup.

#### NodeId.ParseLongForm specific
- Each typed identifier following nsu= : `i=`, `s=`, `g=`, `b=`.
- **Strict by default**: `nsu=<uri>;i=notanumber` via `ParseLongForm`
  → throws `BadNodeIdInvalid` (legacy quirk dropped).
- **Opt-in quirk path**: same input via
  `Parse(context, opts={RequireResolvedUris=true, FallbackToStringIdentifier=true})`
  → yields `NodeId("i=notanumber", nsIndex)` (proves the option still
  works for callers that explicitly want the legacy shape).
- nsu= followed by `ns=<n>;<id>` — current code prefers the inner `ns=`
  when the parsed inner already has a non-zero index — assert that.
- Round-trip with `Format(IServiceMessageContext, useNamespaceUri: true)` →
  parse back via ParseLongForm → equivalent NodeId.

#### ExpandedNodeId.ParseLongForm specific
- `svu=<uri>;…` with URI in serverUris → server index resolved.
- `svu=<uri>;…` with URI **not** in serverUris → throws.
- `svu=…` without `serverUris` argument (i.e. caller passed `null`) →
  throws (BadNodeIdInvalid). The wrapper substitutes an empty
  `StringTable` so the lookup naturally fails under
  `RequireResolvedUris`; assert the resulting status code/message.
- `svu=` with no `;` → throws.
- `svr=42;…` → server index 42.
- `svr=notanumber;…` and `svr=` (no `;`) → throws.
- Combined `svu=…;nsu=…;<id>` and `svr=…;nsu=…;<id>` round-trip.
- Round-trip with `Format(IFormatProvider)` for an absolute ExpandedNodeId.
- Regression coverage for the **default** `Parse(IServiceMessageContext, …)`
  path: with `RequireResolvedUris == false` (default), `nsu=<unknown-uri>;…`
  still produces an absolute `ExpandedNodeId` carrying the unresolved URI
  string (proves the lenient path is unaffected).

#### QualifiedName.ParseLongForm specific
- `n:name` shortcut (e.g. `2:Foo`).
- Bare `name` → ns 0.
- `nsu=<uri>;Foo` → ns resolved.
- `nsu=` with bad colon → still treated as bare name.
- Round-trip with `Format(IServiceMessageContext, useNamespaceUri: true)`.

### Phase 4 — Verify

- Build `Opc.Ua.Types` cleanly (TreatWarningsAsErrors).
- Run `dotnet test Tests/Opc.Ua.Types.Tests/Opc.Ua.Types.Tests.csproj
  -c Release -f net10.0 --filter "FullyQualifiedName~ParseLongForm"`.
- Run the **full** `Opc.Ua.Types.Tests` suite to verify the existing
  `Parse`/`TryParse` paths still pass (proves Phase 1's
  `RequireResolvedUris` introduction is non-breaking on the default path).
- Run `dotnet test UA.slnx -c Release -f net10.0` (or at minimum
  `Opc.Ua.Core.Tests` + `Opc.Ua.Client.Tests` + the encoder/PubSub
  projects) since the wire decoder uses `InternalTryParseWithContext` —
  the change there is additive (one new branch behind a default-false
  flag) but worth a smoke test.

## Out of scope

- Changing the **default** behavior of
  `Parse(IServiceMessageContext, …)` for any of the three types — the
  silent-acceptance lenient path remains the default for backward
  compatibility. Strictness is opt-in via `RequireResolvedUris`.
- Unifying `Parse(string)` (the no-context overloads) with the context
  parsers — they intentionally serve different round-trip contracts and
  remain untouched.
- Touching the wire-format encoders/decoders themselves.
- Adding `RequireResolvedUris` support to `NodeIdParsingOptions` consumers
  outside the three Parse paths (e.g. encoder side); follow-up work.

## Todos

- `pl-add-require-resolved-flag` — Add **two** boolean options to
  `NodeIdParsingOptions`: `RequireResolvedUris` (strict URI lookup) and
  `FallbackToStringIdentifier` (preserves the NodeId.ParseLongForm
  legacy quirk). Both default `false` so `Parse(IServiceMessageContext, …)`
  retains current strict semantics.
- `pl-tighten-context-parser` — Update
  `NodeId.InternalTryParseWithContext`,
  `ExpandedNodeId.InternalTryParseWithContext`, and the
  `QualifiedName.Parse(IServiceMessageContext, …)` body (via a new
  internal `ParseInternal` that takes the flags) to honour
  `RequireResolvedUris` for unresolved `nsu=`/`svu=` lookups and
  `FallbackToStringIdentifier` for malformed inner identifiers.
  Default behavior must be byte-identical to today.
- `pl-rewrite-parselongform` — Replace the bodies of all three
  `ParseLongForm` overloads with thin wrappers that build a transient
  `ServiceMessageContext` from the supplied tables and call the context
  parser with `RequireResolvedUris = true`. The wrappers do **not**
  enable `FallbackToStringIdentifier`; ParseLongForm is strict.
  Delete the now-dead `NodeId.TryParseLongForm` helper, the duplicated
  `svu=`/`svr=` block in `ExpandedNodeId.ParseLongForm`, and the
  duplicated `nsu=` block in `QualifiedName.ParseLongForm`.
- `pl-tests-nodeid` — Add NodeId.ParseLongForm tests including the
  *new strict* behavior (`nsu=<uri>;i=notanumber` throws via
  `ParseLongForm`) **and** a paired test that the same input via
  `Parse(context, opts={RequireResolvedUris=true, FallbackToStringIdentifier=true})`
  still yields `NodeId("i=notanumber", nsIndex)` — proving the legacy
  quirk remains accessible to opt-in callers.
- `pl-tests-expandednodeid` — Add ExpandedNodeId.ParseLongForm tests
  (including a regression test that proves the default
  `Parse(context)` path stays lenient).
- `pl-tests-qualifiedname` — Add QualifiedName.ParseLongForm tests.
- `pl-regression` — Run full `Opc.Ua.Types.Tests` plus a smoke run of
  `Opc.Ua.Core.Tests`/`Opc.Ua.Client.Tests` to confirm zero regression
  on the default Parse(context) path.
