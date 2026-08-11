# Server Design Exploration, Round 2 — Delivery Strategy

> **Status: design exploration / proposal — not shipped API.**
> Round 2 of the Design It Twice exercise, run under a **different premise** from
> [round 1](ServerDesignExplorationAlt.md): backward compatibility with 1.5.378 is no longer
> required, and breaking changes are carried by the migration guide plus the
> `Opc.Ua.MigrationAnalyzer` tooling. Nothing here has been implemented.

Produced against commit `e73e71184` on `master`, 6 Aug 2026.

## Related documents

| Document | Scope |
|---|---|
| [NodeManagerAnalysis.md](NodeManagerAnalysis.md) | Evidence: the node-manager plugin surface |
| [ServerRuntimeAnalysis.md](ServerRuntimeAnalysis.md) | Evidence: the server runtime cluster |
| [ServerDesignExplorationAlt.md](ServerDesignExplorationAlt.md) | Round 1 (alternative): four interface designs under the back-compat constraint |
| **This document** | Round 2: **how to deliver** — full rewrite vs side-by-side fork, plus the `NodeState` question round 1 refused |

## Table of contents

- [What changed from round 1](#what-changed-from-round-1)
- [Method](#method)
- [The migration infrastructure that now carries the burden](#the-migration-infrastructure-that-now-carries-the-burden)
- [Option A — Full rewrite](#option-a--full-rewrite)
- [Option B — Side-by-side fork](#option-b--side-by-side-fork)
- [The NodeState question](#the-nodestate-question)
- [Cross-cutting findings](#cross-cutting-findings)
  - [New finding: a second lock leak](#new-finding-a-second-lock-leak)
  - [Adjudicated disagreement: is NodeState client-coupled?](#adjudicated-disagreement-is-nodestate-client-coupled)
  - [What all three designers converged on](#what-all-three-designers-converged-on)
- [Comparison](#comparison)
- [Recommendation — staged convergence](#recommendation--staged-convergence)
  - [The sequence](#the-sequence)
  - [The gate that decides everything](#the-gate-that-decides-everything)
  - [When to switch to a pure option](#when-to-switch-to-a-pure-option)
  - [Honest caveats](#honest-caveats)

## What changed from round 1

Round 1 ran under a hard constraint: **1.5.378 compatibility, additive changes only,
`[Obsolete]` rather than remove.** Every design was shaped by it, and two designers
explicitly declined work because of it — most notably the `NodeState` re-cut:

> *"I did not try to make `NodeState` itself composable-not-inherited — source generators
> emit `NodeState` subclasses and that is a 15k-line load-bearing contract; re-cutting it is
> out of scope and higher-risk than it is worth."* — round 1, `design-flexible`

Round 2 lifts that constraint. The question is no longer *what should the interface be* —
round 1 answered that, and its four designers converged strongly. The question is now
**how to get there**, with two candidate strategies:

- **Option A — full rewrite.** The existing implementation is replaced. One implementation
  at the end. Consumers migrate via analyzer + guide.
- **Option B — side-by-side fork.** The existing architecture is retained and a new
  implementation ships alongside it in the same assembly. Consumers move at their own pace.

## Method

Three designers worked in parallel, read-only, from an identical evidence base (the two
analysis documents plus round 1's outcome), differing only in assignment:

| Designer | Assignment |
|---|---|
| `r2-rewrite` | Build the strongest Option A and price it honestly |
| `r2-fork` | Build the strongest Option B and price it honestly |
| `r2-nodestate` | Decide whether `NodeState` should be re-cut — permitted to conclude "no" |

Both option-designers were told their counterpart existed and that *an oversold proposal
loses to a fairly-priced one*. Both were required to produce balanced pros/cons and a
falsifiable "what would make this the right choice" section. `r2-fork` was explicitly
invited to say so if round 1's *soft* fork dominated its assigned *hard* fork.

Their proposals are reproduced below **verbatim and in full**.

## The migration infrastructure that now carries the burden

Since the premise shifts the cost onto tooling, its actual reach is decisive. Measured:

| Asset | Reach |
|---|---|
| **20 analyzers** `UA0001`–`UA0023` | Types, client, config, certificates, GDS, PubSub |
| **14 automated code fixes** | ~70% auto-fix rate |
| Runtime shim `Opc.Ua.MigrationAnalyzer.Core`, marked `[OpcUaShim]` | Client `Session` (1,374 lines), classic `Subscription`, `ApplicationInstance`, `ChannelBase`, `TransportChannel`, `ServerBase`, `ApplicationConfiguration`, `EncodeableFactory`, `BuiltInType`, `CertificateIdentifier`, 3 GDS clients |
| `MigrationGenerator.cs` (381 lines) | Source-generated `<Type>Collection` shims |
| `docs/migrate/2.0.x/` | 15 per-area guides, including **`node-states.md`** |

**Every existing analyzer and every shim targets the client, types, configuration,
certificates or GDS surfaces. None targets the node-manager or `NodeState` authoring
surface.** The tooling has never been pointed at the server authoring model. Both option
designers were required to price that gap rather than assume the 70% rate carries over.

---

## Option A — Full rewrite

**Assignment:** *Design and evaluate a full rewrite of `Opc.Ua.Server`. The existing implementation is replaced; one implementation at the end. Build the strongest possible version, and be rigorous and HONEST about its costs — an oversold proposal loses to a fairly-priced one.*

> Reproduced verbatim as delivered. Section numbering is the designer's own.

### Option A — Full Rewrite of `Opc.Ua.Server`

*Design proposal, commit `e73e71184`. Read-only investigation; nothing modified. Measured facts are cited inline.*

**Scope reminder.** `Opc.Ua.Server` is **323 files / 133,695 lines**. `NodeState` and the information-model classes are **not** in it — they live in `Opc.Ua.Types/State` (22 files / ~15,434 lines) and are emitted by the source generator into `Opc.Ua.Core`, shared with the *client*. That boundary is load-bearing for everything below.

---

#### 1. The design, freed from back-compat

Round 1's hybrid was shaped by one compromise it could not escape: it kept **rung 4** — the entire `INodeManager`/`AsyncCustomNodeManager` surface — un-obsoleted and first-class, "because plugin surface with 33 subclasses." With compatibility lifted, the single most important change is:

> **There is no rung 4. The full node-manager surface is deleted, not retained.** One authoring model, one runtime, one test surface.

Everything else follows from removing that escape valve. The four round-1 seams (portable handle, `IServerContext`, data/behaviour split, interceptor chain) were *already* the right shape; the constraint change lets them become the **only** shape instead of a second one layered beside the old.

##### 1.1 The composition root (unchanged verbs, but now the only path)

```csharp
public interface IOpcUaServerBuilder
{
    IServiceCollection Services { get; }

    // Rung 1/2 — inline, no class. Closes the measured defect: today's fluent Variable<T>
    // only *resolves* and throws if absent; these verbs *mint* nodes.
    IOpcUaServerBuilder AddNodeSource(
        string namespaceUri, Func<INodeSourceBuilder, CancellationToken, ValueTask> build);

    // Rung 1/2 — class form (DI deps / state).
    IOpcUaServerBuilder AddNodeSource<[DynamicallyAccessedMembers(PublicConstructors)] TSource>()
        where TSource : class, INodeSource;

    // Rung 3 — browse-on-demand over an external/huge/HA-backed space.
    IOpcUaServerBuilder AddNodeProvider<[DynamicallyAccessedMembers(PublicConstructors)] TProvider>()
        where TProvider : class, INodeProvider;

    // Cross-cutting — audit / auth / rate-limit compose across ALL sources from one registration.
    IOpcUaServerBuilder AddInterceptor<[DynamicallyAccessedMembers(PublicConstructors)] TInterceptor>()
        where TInterceptor : class;
}
```

`[DynamicallyAccessedMembers(PublicConstructors)]` + `ActivatorUtilities` is AOT/trim-safe (today's `AddNodeManager<TFactory>` already uses exactly this). Direct-construct fallback: `new ServerComposition(context, sources, providers, interceptors)` — every part is constructor-injectable without a container.

##### 1.2 `INodeSource` + `INodeSourceBuilder` — the authoring floor (commoncase, now with no base class beneath it)

```csharp
/// <summary>
/// Contributes a partition of the address space. The ONLY node-authoring seam.
/// There is no base class to inherit and no INodeManager to implement.
///
/// INVARIANTS
///  * NamespaceUris is fixed for the source lifetime; the framework assigns indexes
///    BEFORE BuildAsync and no source's namespaces may overlap another's (router
///    enforces at startup — BadInvalidState).
///  * BuildAsync runs exactly once, single-threaded, at activation, before the source
///    serves any request. After it returns the runtime SEALS the source: dispatch tables
///    are frozen and read-only, so no synchronization primitive is ever exposed.
///  * On an HA standby BuildAsync may run again after a snapshot hydrate: it MUST be
///    idempotent. Behaviour (delegates) is re-attached here; data is hydrated by the runtime.
/// ERROR MODES
///  * A throw from BuildAsync aborts server start (fail-fast). Double-wiring the same
///    node category throws ServiceResultException(BadNodeIdExists) at build time, surfacing
///    author error at startup rather than first request.
/// </summary>
public interface INodeSource
{
    ArrayOf<string> NamespaceUris { get; }
    ValueTask BuildAsync(INodeSourceBuilder builder, CancellationToken ct = default);
}

public interface INodeSourceBuilder
{
    IServerContext Context { get; }                 // §1.5 — NOT IServerInternal
    ushort NamespaceIndex { get; }

    // CREATE (the fix: these MINT nodes; today's Variable<T> only resolves + throws)
    IFolderNode      Folder(QualifiedName browseName, NodeId parent = default);   // default => Objects
    IObjectNode      Object(QualifiedName browseName, NodeId typeDefinition = default, NodeId parent = default);
    IVariableNode<T> Variable<T>(QualifiedName browseName, NodeId parent = default);
    IMethodNode      Method(QualifiedName browseName, NodeId parent = default);

    // IMPORT a NodeSet2 / companion-spec model; returns a binder to wire behaviour onto model nodes
    IModelBinder Import(NodeModel model);

    // RESOLVE nodes a model already created
    IVariableNode<T> Bind<T>(NodeId nodeId);
    INode            Node(NodeId nodeId);

    INodeSourceBuilder UseNodeIdScheme(INodeIdScheme scheme);   // retires the `New` virtual (12 sites)
}
```

The three read shapes — verified against `BaseVariableState.OnReadValue` (sync, line 559) and `OnReadValueAsync` (async, runs **without** `lock(this)`, line 578), so none of this is sync-over-async:

```csharp
public interface IVariableNode<T> : INode
{
    IVariableNode<T> Value(T initialValue);                                   // static: no delegate, no async
    IVariableNode<T> OnRead(Func<T> read);                                    // genuine sync path
    IVariableNode<T> OnRead(Func<IServerContext, CancellationToken, ValueTask<T>> read); // async, lock-free
    IVariableNode<T> OnWrite(Func<T, CancellationToken, ValueTask> write);
    IVariableNode<T> Observe(out IValueUpdater<T> updater);                   // reuse existing seam
    IVariableNode<T> Writable(bool writable = true);
    IVariableNode<T> Historize();                                            // opt this node into Part 11
}
```

##### 1.3 `INodeProvider<THandle>` — browse-on-demand (ports + commoncase), no legacy floor to fall through to

Modelled bit-for-bit on `IHistorianProvider` (2 members over ~5,900 lines). First-match-wins routing **is** the replacement for the `Processed` protocol — the invariant is carried by a resolved handle, not by prose restated 6× in `INodeManager.cs`.

```csharp
public interface INodeProvider
{
    ArrayOf<string> NamespaceUris { get; }
    /// <summary>Recognise id syntax WITHOUT blocking I/O. First provider returning true owns the node.
    /// Returns a portable value handle (§1.4) — never `object`.</summary>
    bool TryResolve(NodeId nodeId, out NodeManagerHandle handle);
    ValueTask<NodeMetadata> DescribeAsync(in NodeManagerHandle h, CancellationToken ct);  // NodeMetadata.Unknown when not owned
}

// Opt-in per service; an unimplemented service auto-yields BadNotSupported (historian pattern).
public interface IBrowseProvider : INodeProvider
{
    /// <summary>ONE page of children. The runtime owns paging, maxReferences, dedup, filtering:
    /// you receive a token and return the next. You never mutate a caller-owned IList that
    /// "may already contain references".</summary>
    ValueTask<BrowsePage> BrowseAsync(in NodeManagerHandle h, in BrowseFilter filter,
                                      ContinuationToken token, CancellationToken ct);
}
public interface IValueProvider : INodeProvider
{
    ValueTask<DataValue>     ReadAsync (in NodeManagerHandle h, in ReadFilter f, CancellationToken ct);
    ValueTask<ServiceResult> WriteAsync(in NodeManagerHandle h, in DataValue v, CancellationToken ct);
}
public interface ICallProvider : INodeProvider   { ValueTask<CallResult> CallAsync(in NodeManagerHandle h, in CallRequest r, CancellationToken ct); }
public interface IObserveProvider : INodeProvider { IAsyncEnumerable<DataValue> ObserveAsync(in NodeManagerHandle h, in MonitorRequest r, CancellationToken ct); }
```

##### 1.4 `NodeManagerHandle` — portable value (ports), replacing `object? GetManagerHandle`

```csharp
public readonly record struct NodeManagerHandle
{
    public NodeId NodeId { get; init; }
    public int OwningNamespaceIndex { get; init; }   // the partition; portable across replicas
    public ulong Token { get; init; }                // provider-local, opaque to the runtime, never boxed
    // NO in-process pointer. Valid on any replica hosting the same namespace partition.
}
```

The runtime router resolves each `ReadValueId`/`WriteValue`/etc. to its single owning handle, then dispatches a **partition** (that provider's items only, locally 0-indexed) to exactly that provider. This deletes, in one move: the `Processed` flag, the index-aligned parallel `IList` accumulators, and the shared-mutable-array fan-out at `MasterNodeManager.cs:4053`.

##### 1.5 `IServerContext` — replaces the 57-member `IServerInternal` service locator (ports)

```csharp
/// <summary>Read-only ambient facts. Frozen after the single bind phase. No Set*, no object, no exposed lock.</summary>
public interface IServerContext
{
    IServiceMessageContext MessageContext { get; }
    NamespaceTable         NamespaceUris  { get; }
    IEncodeableFactory     Factory        { get; }
    ITelemetryContext      Telemetry      { get; }   // ILogger via source-gen [LoggerMessage]
    TimeProvider           TimeProvider   { get; }
    ServerSystemContext    DefaultSystemContext { get; }
    ServerState            State          { get; }

    ValueTask<ServerState> TransitionStateAsync(ServerState target, LocalizedText reason, CancellationToken ct = default);
    ValueTask ReportEventAsync(IFilterTarget e, CancellationToken ct = default);
    ValueTask<ServerDiagnosticsSummaryDataType> GetServerDiagnosticsAsync(CancellationToken ct = default); // snapshot; private Lock
}
```

Subsystems (`ISessionManager`, `ISubscriptionManager`, role/identity, the `IHistorianRegistry`) are **resolved from DI by the components that need them**, not handed out by a locator. The 12 `Set*` mutators and `ServerInternalData`'s two-phase construction are replaced by a single ordered bind phase; the hook stops leaking the locator:

```csharp
public interface IServerStartupTask
{
    ValueTask OnServerStartedAsync(IServerContext context, IServiceProvider services, CancellationToken ct = default);
}
// Ordering: DI registration order. Any port bind after ServerState.Running throws BadInvalidState
// (replaces silent re-entrant SetSessionManager). Redundancy.Server already uses this pipeline (18 refs).
```

##### 1.6 `INodeBehavior` / `INodeBehaviorSource` — the data/behaviour split (ports), now an *internal* seam

This is the crux for HA, and the one place I keep `NodeState` — see §1.8. The 18 `NodeState` delegates collapse to one node-local provider that is re-attached after hydration; **the serialized payload carries no delegate, no `object`, no `Lock`.**

```csharp
internal interface INodeBehavior   // internal: it is engine depth, not authoring surface
{
    ValueTask<AttributeReadResult>  OnReadValueAsync (ISystemContext c, in ReadValueId id, CancellationToken ct = default);
    ValueTask<AttributeWriteResult> OnWriteValueAsync(ISystemContext c, in WriteValue  v,  CancellationToken ct = default);
    ValueTask<CallMethodResult>     OnCallAsync      (ISystemContext c, in CallMethodRequest r, CancellationToken ct = default);
}
internal interface INodeBehaviorSource { bool TryGetBehavior(NodeId nodeId, out INodeBehavior behavior); }
```

##### 1.7 Interceptor chain — cross-cutting at batch granularity (flexible)

```csharp
public interface IReadInterceptor  { ValueTask InvokeAsync(ReadBatch batch, ReadPipeline next, CancellationToken ct); }
public interface IWriteInterceptor { ValueTask InvokeAsync(WriteBatch batch, WritePipeline next, CancellationToken ct); }
/// <summary>Struct cursor: (interceptor[] chain, int index, terminal). No allocation to advance.
/// Stack grows O(interceptor count) PER SERVICE CALL, never per node — 10k monitored items + 3
/// interceptors = 3 frames, not 30 000. ORDERING: registration order is semantically significant
/// (new obligation vs. today's implicit call order).</summary>
public readonly struct ReadPipeline { public ValueTask InvokeAsync(ReadBatch batch, CancellationToken ct); }
```

##### 1.8 My explicit answers to the three questions the brief demands

| Question | Answer | Why |
|---|---|---|
| **Delete `INodeManager`/`2`/`3`, `IAsyncNodeManager`?** | **Yes, outright.** | One direct external impl exists (`SampleNodeManager`); by the one-adapter rule the seam is *hypothetical*. `NodeManagerRoutingTable` already normalises everything to one internal contract before dispatch. |
| **`CustomNodeManager2` / `AsyncCustomNodeManager` cease to exist?** | **Yes, as public API.** Absorbed as the `internal sealed` engine behind rungs 1–3. `MasterNodeManager` → internal router. | 93 virtuals bought ~5 used members; 55 of 80 never overridden. The *implementation* is deep and stays; only the interface is deleted. |
| **Should `NodeState` be re-cut?** | **Apply the data/behaviour split; do NOT fold a full `NodeState` re-cut into this option.** Hide `NodeState` behind the ladder (rungs 1–2 never name it) and behind `INodeBehavior`. | `NodeState` is in `Opc.Ua.Types`, **shared with the client** and **emitted by the generator into `Opc.Ua.Core`**. Re-cutting it breaks the client and every companion generator simultaneously. The 2.0 `node-states.md` already documents that *modest* `NodeState` edits produced **silent binary-incompatible** regressions (virtual-signature no-ops). Coupling that program to the server rewrite multiplies two big-bangs. **Decouple them.** I defer the full `NodeState` re-cut to the colleague and require only that the server rewrite not *depend* on it. |

That last row is the sharpest departure from a naïve "constraint's gone, change everything" reading, and it is where an honest Option A must hold its line: **the strongest full rewrite of `Opc.Ua.Server` still treats `Opc.Ua.Types/State` as a dependency it hides, not one it re-cuts.**

---

#### 2. What gets deleted

Inventory (measured line counts). "Absorbed" = the implementation survives as `internal sealed` engine behind the new seams; "Deleted" = ceases to exist; "Unchanged" = untouched.

| Element | Lines | Fate |
|---|---:|---|
| `INodeManager.cs` (22 interfaces: `INodeManager`/`2`/`3`, `IAsyncNodeManager`, 15 capability ifaces, 2 factories) | 1,158 | **Deleted** (public); internal router contract replaces it |
| `AsyncCustomNodeManager` | 8,028 | **Absorbed** (internal engine); 93 virtuals → 0 public |
| `CustomNodeManager2` | 6,302 | **Deleted** (the sync twin; async is the only runtime) |
| `MasterNodeManager` (public 36) | 7,601 | **Absorbed** → internal sealed router; `Processed`, fan-out broadcast, handle resolution deleted |
| `FluentNodeManagerBase : AsyncCustomNodeManager` | 280 + Fluent (35 files/8,814) | **Re-based** onto the sealed runtime; the `: AsyncCustomNodeManager` inheritance deleted |
| `IServerInternal` (57) + `ServerInternalData` | 1,355 | **Deleted**; `IServerContext` (~10) + DI + frozen bind |
| 5 diagnostics-lock members + 88 `lock (object)` statements (7 files incl. a sample) | — | **Deleted**; private `System.Threading.Lock` + snapshot getters |
| `ISubscription` publish-protocol (11 of 42) | ~5,000 (Subscription+queues) | **Absorbed** to internal seam; `ISubscription` 42→~25 |
| `ISession` `object` continuation members (2 leaks) | — | **Deleted**; routed to existing `IContinuationPointStore` |
| `ImpersonateUser`/`ValidateSessionLessRequest` mutable-event-args | — | **Deleted**; routed to existing `IUserTokenAuthenticator`/`IServerIdentityRegistry` (both already exist) |
| `IStandardServer` (9) + factory/lifecycle virtuals | 5,575 | **Unchanged** — the one healthy seam (16/37 virtuals used by 3 subclasses) |
| `NodeState` + `Opc.Ua.Types/State` | 15,434 | **Unchanged as data**; only an internal `INodeBehavior` split |

##### Deletion test applied

1. **`IServerInternal` (57 members).** Delete it → complexity *vanishes at the seam* (it has zero depth: 1,355 lines of property storage behind 57 members) but *reappears as wiring* in ~65 core files. That wiring is real — but it is DI's job, not a locator's. Verdict: **delete the locator, keep the wiring in DI.** The `SetNodeManager`→silently-populates-3-more and re-entrant `SetSessionManager` hazards disappear with it.
2. **The `Processed` protocol + `object` handle.** Delete → nothing reappears *if* routing pre-resolves owners (portable handle). The cooperative-multiplex existed *only* because `MasterNodeManager` broadcasts one shared mutable array to every manager. Owner-pre-split makes it unnecessary. Verdict: **pure prose-enforced complexity; delete.**
3. **`CustomNodeManager2` (the sync twin, 6,302 lines).** Delete → nothing reappears: `NodeManagerRoutingTable` is already `IReadOnlyList<IAsyncNodeManager>`; everything is normalised to async before dispatch. The sync base is a *technology* duality, not a domain one. Verdict: **delete.**
4. **`INodeManager`/`2`/`3` (versioned by suffix twice).** Delete → little reappears; routing runs on the internal async contract, and exactly one external adapter exists. Verdict: **compatibility surface, not a load-bearing seam; delete.**
5. **The 88 `object` locks / 5 diagnostics-lock members.** Delete → nothing reappears; the guarded state is owned by `Subscription`/`Session`/`ServerInternalData` themselves, and `UpdateServerStatus(Action<T>)` already demonstrates the owner-side replacement. The `DiagnosticsWriteLock` getter that runs `ForceDiagnosticsScan()` *outside* the lock it returns becomes inexpressible. Verdict: **pure leak; delete.**

---

#### 3. Blast radius and migration story

##### 3.1 Measured blast radius (counted this session)

| Consumer category | Count | Detail |
|---|---:|---|
| **Node-manager derivations, production** | **~24** | 6 core impls + 12 sibling-lib + ~6 samples. Sibling libs: GDS (3), XRegistry (3), WotCon (2), PubSub (1), ISA95 (1), DI (1), Positioning (1) |
| **Node-manager derivations, tests** | **~17** | incl. the giant `NodeManagerLifecycleTests.cs` (8,406) and `AsyncCustomNodeManagerTests.cs` (5,821) |
| **Direct `INodeManager` impls** | **1** | `samples/Quickstarts.Servers/SampleNodeManager` |
| **`IServerInternal` referencing files** | **268** | core 65 · sib 35 · sample 18 · **test 150** |
| **`[NodeManager]` generator entry points** | **7** | template hard-wired to `FluentNodeManagerBase` (`NodeManagerTemplates.cs:61`) |
| **Companion server libraries** | **12** | Di, Gds.Server.Common, Lds, ISA95, OpenUsd (×2), Positioning, PubSub, Redundancy, Robotics, WotCon, XRegistry |
| **Server test files ≥ removed surface** | **5** | 8,406 + 5,821 + 2,499 + 1,639 + 1,355 = **~19,720 lines** testing *through* the deleted surface |
| **External consumers** | **unknown & uncounted** | out-of-repo servers that derive `CustomNodeManager2` — the real risk |

##### 3.2 The structural fact that dominates the migration story

**The `[OpcUaShim]` runtime bridge that carried the client/types/config migration is structurally unavailable for the node-manager surface.** Verified: every shim in `MigrationAnalyzer.Core` is an **extension method or thin re-implementation over the *new* API** (e.g. `ServerBaseObsolete.Start(this IServerBase) => StartAsync(...).GetAwaiter().GetResult()`). That works when the old API is a veneer over the new one. But a node-manager author *inherits* an 8,028-line base and *overrides* virtuals whose bodies call `MasterNodeManager`/`NodeState`/`IServerInternal` internals — none of which exist after the rewrite. To shim it you would have to **ship the old base classes with their old runtime beneath them** — which is precisely Option B relocated into a NuGet package. **A base-class inheritance surface cannot be shimmed without retaining its implementation.** So for the authoring surface, category (c) is ~0.

##### 3.3 Honest category split

I split by *kind of consumer code*, because the same server project mixes both.

**(i) Non-authoring churn** (client calls, `byte[]`→`ByteString`, `==null`→`.IsNull`, `EncodeableFactory` rename, `Session.Call` params) — already covered by UA0001–UA0023, **70% auto-fixed**. This is real and it discounts the *total* migration effort of a full server project. But it is not the hard part and it exists identically under Option B.

**(ii) Node-manager / `IServerInternal` authoring code** — the part this rewrite creates. My honest estimate for *this slice*:

| Category | Share | Justification |
|---|---:|---|
| **(a) Roslyn auto-fix** | **~10%** | Base-type rename in trivial passthrough managers; 1:1 renames (`Clone`→`CreateCopy`); mechanical signature additions. Cannot restructure a body. |
| **(b) Analyzer-detect + human judgement** | **~30%** | Locate every `: AsyncCustomNodeManager`, every override of a removed virtual (25 distinct names), every `IServerInternal.X` read and map it to its injected seam. The tool points; the human rewrites. |
| **(c) `[OpcUaShim]` runtime bridge** | **~0–5%** | Structurally unavailable for the base class (§3.2). Only isolated helper calls (`GetServerDiagnostics` snapshot) are bridgeable. |
| **(d) Genuine consumer rewrite** | **~55–60%** | `CreateAddressSpaceAsync` bodies re-expressed as `BuildAsync`+builder; browse-on-demand managers re-expressed as `INodeProvider`; behaviour re-attachment; `Processed`/handle round-trips removed. No tool can write these. |

**Why (d) dominates and I will not inflate (a).** The existing analyzers work on `OperationKind.Invocation` — "you *called* removed symbol X." The node-manager migration is "your *override body* must be reshaped," which is not an invocation pattern. The one precedent that already exists — 2.0's `node-states.md` — migrated `NodeState` changes **entirely by hand**, with explicit *"⚠ Silent regression … No runtime exception is thrown"* warnings. That is the ceiling of what tooling does here, and it is low.

##### 3.4 New analyzers I would build (and which carry a code fix)

| Rule | Detects | Code fix? |
|---|---|---|
| **UA0024** | `: CustomNodeManager2 / AsyncCustomNodeManager / FluentNodeManagerBase / INodeManager*` | **Partial** — rename base only for the ~10% trivial-passthrough shape; otherwise *no fix*, link `docs/migrate/2.x/node-sources.md` |
| **UA0025** | `override` of any of the 25 removed virtuals (`CreateAddressSpaceAsync`, `New`, `LoadPredefinedNodesAsync`, `AddBehaviourToPredefinedNodeAsync`, …) | **No** — body restructuring; detect-and-point only |
| **UA0026** | `IServerInternal` member access; maps each of ~20 subsystems to its new injected seam | **Partial** — 1:1 property reads (`.NamespaceUris`, `.Telemetry`); *no fix* for `Set*` / two-phase |
| **UA0027** | `object GetManagerHandle` round-trip / `Processed` flag reads | **No** — rewrite to `INodeProvider.TryResolve` |
| **UA0028** | `.DiagnosticsLock` / `.DiagnosticsWriteLock` acquisition (incl. the sample) | **Partial** — read-only snapshot → `GetServerDiagnosticsAsync` |
| **UA0029** | Imperative `NodeState` tree-building in an override (`CreateVariable`/`AddChild`/`AddReference`) | **No** — link builder recipe |

So of six new rules, **two carry partial fixes** and none carries a full body-rewriting fix — consistent with the ~10% auto-fix ceiling. Contrast the shipped 14/20 (70%) rate, which was achievable *only* because those targets were renames and wraps.

##### 3.5 What the migration guide must carry that tooling cannot

- **Recipe: `CreateAddressSpaceAsync` override → `BuildAsync` + creational builder** (worked example per node class).
- **Decision guide: rung-1 materialize vs. rung-3 `INodeProvider`** for a given manager (the analyzer cannot judge address-space size).
- **The `IServerInternal`-subsystem → new-home map** (all ~20 rows), because most have no 1:1 property.
- **Behaviour re-attachment** and why delegates must not be in the replicated graph.
- **Interceptor ordering** semantics (a new obligation) and **frozen-bind** timing (`BadInvalidState` after `Running`).
- The **silent-regression classes** that have *no* build break (binary-incompat overrides), which only a recompile surfaces — the single most dangerous item, and tooling cannot see it in a pre-compiled external assembly.

---

#### 4. Pros and cons of the full-rewrite option

**Implementation cost & elapsed time.** Rewriting the runtime + authoring surface within `Opc.Ua.Server` (~134k lines, of which the node-manager/master/server-internal cluster is ~24k directly re-cut and much of the rest re-wired) is, realistically, a **multi-quarter program for a small senior team**, gated on: (2) `IServerContext`+frozen bind, (3) creational builder + sealed runtime, (4) portable handle + router, (5) behaviour split — *plus* rewriting the generator template (`NodeManagerTemplates.cs`), regenerating 7 `[NodeManager]` models, and migrating 12 companion libraries and all samples **in lockstep**. This is strictly more than round 1's sequencing, because there is no rung-4 fallback to defer behind.

**Risk to correctness / OPC UA compliance.** High and concentrated. Browse continuation, `Read`/`Write` index-alignment, `Call`, `HistoryRead`, Publish, subscription transfer, and `TranslateBrowsePath` semantics are fixed by the spec and are today validated *through the very surface being deleted*. The CTT / conformance-unit suite (`ConformanceUnitsManager`) is the safety net, but CTT exercises the wire protocol, not the authoring API — so a rewrite can pass CTT while silently changing authoring semantics, and can regress a conformance unit that no unit test now covers because the unit tests are being deleted with the surface. This is the **big-bang integration risk** in concrete form: nothing ships until core + generator + 12 libs + samples + a rebuilt test suite all pass together.

**In-flight work / open PRs.** Any open PR touching `AsyncCustomNodeManager`, `CustomNodeManager2`, `MasterNodeManager`, `IServerInternal`, `Subscription`, or `Session` conflicts irreconcilably — these are the highest-churn files in the assembly. The rewrite must land as a coordinated freeze; long-lived branches against the old surface are write-offs. Option B does not force this.

**Test-suite story.** The heaviest liability. **~19,720 lines across 5 files** (`NodeManagerLifecycleTests` 8,406; `AsyncCustomNodeManagerTests` 5,821; `MasterNodeManagerNodeManagementTests` 2,499; `MasterNodeManagerDeterministicTests` 1,639; `CustomNodeManagerDeterministicTests` 1,355) test *through* the deleted surface by subclassing it — they cannot be adapted, only rewritten. **150 test files** reference `IServerInternal`. The mocked diagnostics locks (`SubscriptionTests`, `SessionSecurityTests`) verify zero mutual exclusion and must be deleted, not ported. `IStandardServer` tests survive; most node-manager and server-internal tests do not.

**Coverage requirement (must not decrease, 80%+).** This is a genuine hazard, not a footnote. Coverage is measured on the *shipping* assembly. The moment the old surface is deleted, ~19,720 lines of tests stop compiling; until equivalent tests exist against the narrow seam, coverage of the *new* engine is low. Because "coverage MUST NOT decrease," the new tests are **on the critical path of the same PR**, not a follow-up — inflating the big-bang further. The narrow interface is a *better* long-term test surface (fewer, deeper tests), but the transition trough is real.

**External consumers not in this repo.** They get the worst deal of any option: no shim (§3.2), ~55–60% genuine rewrite, and a class of **silent binary-incompatible** regressions that only a recompile reveals. Their only safety net is the analyzer's detect-and-point and the migration guide. This is the cost Option B exists to avoid.

**The honest pro.** Option A is the *only* option that actually pays the debt down. Option B keeps **~22,000 lines** of old node-manager code (`AsyncCustomNodeManager` + `CustomNodeManager2` + `MasterNodeManager`) alive beside the new implementation **forever**: every future fix is done twice, every security patch twice, and the "second surface for tests to fall through to" — the exact `FluentNodeManagerBase : AsyncCustomNodeManager` failure mode round 1 flagged — persists permanently. Option A's `internal sealed` runtime makes the shallow-facade failure *impossible by construction* (compiler-enforced, not documented). One implementation, one interface-as-test-surface, one thing to reason about for HA. If the assembly is going to be maintained for another decade, Option A is the only choice that stops the bleeding rather than adding a second wound.

**Net.** Option A has the **lower total-cost-of-ownership and the higher one-time cost and risk.** It trades a large, concentrated, schedulable, mostly-internal cost for the elimination of a permanent recurring one — while pushing an un-shimmable, mostly-manual migration onto ~24 in-repo and an unknown number of external node-manager authors.

---

#### 5. What would make this the right choice

**Clearly correct when:**

1. **`Opc.Ua.Server` will be maintained for years and HA is a committed goal.** The data/behaviour split makes replication correct *by construction* (delegates are code, cannot be serialised, so must not be in the replicated graph). A fork cannot deliver that guarantee — it keeps two graphs. If HA is real, A is the only correct answer. If HA is aspirational, this pillar collapses and B suffices.
2. **The external node-manager population is small or coordinated** (mostly first-party companion specs + a known handful of downstreams). Falsifiable: if telemetry/issues show few external `CustomNodeManager2` subclasses, (d)'s ~55–60% manual rewrite lands on a countable set and the analyzer+guide carry it. If that population is large and uncoordinated, the un-shimmable rewrite is a community-breaking event and B wins.
3. **A single coordinated release train exists** that can land core + generator + 12 libs + samples + rebuilt tests together, with a freeze on the touched files. Falsifiable: if the project cannot freeze `MasterNodeManager`/`IServerInternal`/`Subscription` for the duration, the big-bang cannot be integrated and B's incremental path is mandatory.
4. **The team will fund the test rewrite inside the same train** (the ~19,720-line suite + the 150 `IServerInternal` test files), so the coverage floor is never breached on the shipping assembly. Falsifiable: if the plan defers tests to "after," the 80%-no-decrease rule blocks the merge — a hard stop.
5. **The `NodeState` re-cut is kept out of scope of this train** (my §1.8 position). If a decision-maker insists on folding the `Opc.Ua.Types/State` re-cut *into* the server rewrite, the two big-bangs compound: the generator, the client, and every companion model break at once, and the silent-binary-incompat class multiplies. That combination moves A from "hard but right" to "reckless."

**Clearly *not* the right choice when:**

- **Back-compat was lifted on paper but downstream reality has not changed** — i.e. large external `CustomNodeManager2` deployments that cannot recompile on your schedule. Then the missing shim (§3.2) is disqualifying and B's side-by-side path is correct.
- **The near-term driver is the runtime debt, not the authoring model** — the diagnostics-lock leak (88 locks), the `IServerInternal` locator, the `ISubscription` publish-protocol leak. **All of these are fixable without deleting the node-manager surface at all** (they were round 1's steps 1–2, "worth doing regardless"). If that is the actual goal, a full rewrite is massive over-reach for it.
- **There is no appetite for a multi-quarter freeze or a coverage trough.** A's costs are front-loaded and indivisible; a team that needs steady incremental delivery cannot absorb them, and B's ability to ship the new surface beside the old — trading permanent duplication for schedulability — is the rational trade.

**One-line honest verdict.** Choose Option A if you are optimising the *next decade* of maintenance and HA correctness and can pay a large, coordinated, one-time cost with an un-shimmable external-migration tail. Choose the fork if you are optimising the *next year* of downstream stability and incremental delivery. The technical design above is the strongest A I can build; its price is a genuine consumer rewrite that tooling can detect and document but cannot perform.

---

## Option B — Side-by-side fork

**Assignment:** *Design and evaluate a fork inside `Opc.Ua.Server` — existing architecture retained, new implementation shipping alongside it. Answer where the fork line falls, whether both can coexist in one address space, the double-maintenance cost, and the exit strategy. Say plainly if round 1's soft fork dominates.*

> Reproduced verbatim as delivered. Section numbering is the designer's own.

### Option B — A Side‑by‑Side Fork Inside `Opc.Ua.Server`

#### Summary judgment up front (so it can't be lost in the length)

The decisive fact I verified is that **`Opc.Ua.Server` already multiplexes heterogeneous node managers behind one seam.** `StandardServer.CreateMasterNodeManagerAsync` (StandardServer.cs:4913) builds a sync list *and* an async list of node managers and hands both to a single `MasterNodeManager`, which normalizes everything to `IAsyncNodeManager` (via the 812‑line `AsyncNodeManagerAdapter`) before dispatch. `NodeManagerRoutingTable` is already `IReadOnlyList<IAsyncNodeManager>`.

That changes the whole character of Option B. **The fork line for a side‑by‑side model falls at `IAsyncNodeManager`, and everything below it — `MasterNodeManager`, `Session`, `Subscription`, `SubscriptionManager`, `StandardServer`, `ServerBase`/transport, and `NodeState` — is *shared*, not duplicated.** A "hard fork inside the assembly" is therefore not a fork of the ~130,000‑line runtime; it is a fork of only the **~15,000‑line authoring surface**, joined to the shared runtime by one new bridge adapter.

This makes Option B far cheaper than the phrase "two live implementations" suggests — and it is exactly why, once priced honestly, **the *soft* fork (round‑1's un‑obsoleted rung 4: one node‑serving implementation, layered entry points) dominates the hard fork for every goal except one narrow case** (a second, structurally‑incompatible node‑serving engine for fully‑remote address spaces). I build the strongest hard‑fork design below, then show where it loses.

---

#### 1. The design and the fork line

##### 1.1 What changes now that 1.5.378 compatibility is lifted

Start from the round‑1 hybrid (commoncase ladder + ports data/behaviour split + `IServerContext`). With back‑compat gone, three things change:

1. **Delete, don't `[Obsolete]`.** The sync `INodeManager`/`INodeManager2`/`INodeManager3` chain, `CustomNodeManager` (the 5,489‑line sync base), `SyncNodeManagerAdapter` (294 lines), and `IAsyncNodeManager.SyncNodeManager` can be **removed outright**, normalizing the whole stack to async. That is a genuine simplification the lifted constraint unlocks — but it forces the one direct `INodeManager` implementer (`SampleNodeManager`, Quickstarts.Servers) and its two subclasses to move.
2. **No shim library, no obligatory analyzer.** In Option B the old authoring surface *keeps working as itself*, so there is nothing to shim. Contrast the existing `Opc.Ua.MigrationAnalyzer.Core` shims, which exist only for surfaces that were *removed* (client `Session`, `ServerBase`, `EncodeableFactory`, …).
3. **The choice is now real.** In round 1 rung 4 was kept un‑obsoleted "because we must." Now we keep the old authoring surface **because we choose to** — which is precisely the hard‑fork decision, and it must earn its place rather than be grandfathered.

##### 1.2 The fork line, exactly

```
   AUTHORING SURFACE  (FORKED — two implementations live here)
   ┌───────────────────────────────┬────────────────────────────────────┐
   │  OLD (frozen-but-alive)        │  NEW (Opc.Ua.Server.Nodes)          │
   │  AsyncCustomNodeManager 7,080  │  INodeSource / INodeSourceBuilder    │
   │  FluentNodeManagerBase         │  INodeProvider<THandle> (+caps)      │
   │  NodeState 18-delegate surface │  internal sealed NodeSourceRuntime   │
   └───────────────┬───────────────┴──────────────────┬─────────────────┘
                   │  is-an IAsyncNodeManager          │  bridged to IAsyncNodeManager
                   │                                    │  by NodeSourceManagerAdapter (NEW, ~800 ln)
   ════════════════▼════════════════════════════════════▼═════════════════  ← THE FORK LINE
   IAsyncNodeManager  (the normalization seam — SHARED, unchanged)
   ┌───────────────────────────────────────────────────────────────────────┐
   │  SHARED RUNTIME (one implementation, ~130,000 lines, NOT duplicated)    │
   │  MasterNodeManager 6,791 · Session 1,368 · Subscription 3,039           │
   │  SubscriptionManager 2,523 · StandardServer 4,963 · ServerBase 1,630    │
   │  publish pipeline · monitored items · NodeState data core (5,075) ·      │
   │  IServerInternal/ServerInternalData · continuation/subscription stores   │
   └───────────────────────────────────────────────────────────────────────┘
```

**The fork line is `IAsyncNodeManager`.** Above it, an author picks one of two authoring surfaces. Below it, there is exactly one runtime. The new surface reaches the runtime through **one new adapter** that satisfies `IAsyncNodeManager`, exactly as `AsyncNodeManagerAdapter` does today for sync managers.

##### 1.3 The new authoring surface (real C#)

The internal runtime uses a **portable handle** (ports design) so it is HA‑ready and never round‑trips `object`:

```csharp
namespace Opc.Ua.Server.Nodes;

/// <summary>
/// Portable node identity used inside the new runtime. No in-process pointer,
/// so it is valid on any replica hosting the same namespace partition.
/// </summary>
public readonly record struct NodeSourceHandle
{
    public NodeId NodeId { get; init; }
    public int OwningNamespaceIndex { get; init; }
}
```

The common‑case authoring seam (commoncase ladder, now free of any obsolete baggage). `INodeSourceBuilder` *mints* nodes — the round‑1‑verified gap that today's `Variable<T>(...)` only resolves and throws:

```csharp
namespace Opc.Ua.Server.Nodes;

public interface INodeSource
{
    ArrayOf<string> NamespaceUris { get; }
    ValueTask ConfigureAsync(INodeSourceBuilder builder, CancellationToken ct = default);
}

public interface INodeSourceBuilder
{
    IServerContext Context { get; }

    IFolderNode      Folder(string browseName);
    IObjectNode      Object(string browseName, NodeId typeDefinition = default);
    IVariableNode<T> Variable<T>(string browseName);   // mints, returns typed builder
    IMethodNode      Method(string browseName);

    INode            Node(NodeId nodeId);              // resolve a model-authored node
    IVariableNode<T> Bind<T>(NodeId nodeId);

    INodeSourceBuilder UseNodeIdScheme(INodeIdScheme scheme);   // retires the `New` virtual (12 sites)
}

public interface IVariableNode<T> : INode
{
    IVariableNode<T> Value(T initialValue);                                   // static: no delegate, no async
    IVariableNode<T> OnRead(Func<T> read);                                    // genuine sync path
    IVariableNode<T> OnRead(Func<ISystemContext, CancellationToken, ValueTask<T>> read);  // async, lock-free
    IVariableNode<T> OnWrite(Func<T, CancellationToken, ValueTask> write);
    IVariableNode<T> Writable(bool writable = true);
}
```

The advanced seam is a provider, modelled on `IHistorianProvider` (first‑match‑wins, no `Processed`, `BrowsePage` instead of an append accumulator):

```csharp
namespace Opc.Ua.Server.Nodes;

public interface INodeProvider<THandle> where THandle : notnull
{
    ArrayOf<string> NamespaceUris { get; }
    bool TryResolve(NodeId nodeId, out THandle handle);           // cheap, no I/O
    ValueTask<NodeMetadata> DescribeAsync(THandle handle, CancellationToken ct);
}

public interface IValueProvider<THandle>
{
    ValueTask<DataValue>     ReadAsync(THandle handle, in ReadFilter filter, CancellationToken ct);
    ValueTask<ServiceResult> WriteAsync(THandle handle, in DataValue value, CancellationToken ct);
}

public interface IBrowseProvider<THandle>
{
    ValueTask<BrowsePage> BrowseAsync(
        THandle handle, in BrowseFilter filter, ContinuationToken token, CancellationToken ct);
}
```

The narrow server façade (ports design — replaces `IServerInternal`'s 57 members with the ~6 authors used; no `Set*`, no exposed lock, no `object`):

```csharp
namespace Opc.Ua.Server.Nodes;

public interface IServerContext
{
    NamespaceTable    NamespaceUris { get; }
    ITelemetryContext Telemetry     { get; }
    TimeProvider      TimeProvider  { get; }
    ISystemContext    SystemContext { get; }
    IHistorianRegistry Historians   { get; }
    IFileSystemProvider FileSystem  { get; }
}
```

##### 1.4 The bridge — the one artifact that makes Option B work (real C#)

The new runtime never sees the `Processed` protocol, index‑aligned accumulators, or the `object` handle. **The bridge translates the runtime's clean first‑match‑wins model into the shared `MasterNodeManager` fan‑out contract.** This is the paradigm analogue of `AsyncNodeManagerAdapter.ReadAsync` (AsyncNodeManagerAdapter.cs:542‑560):

```csharp
namespace Opc.Ua.Server.Nodes;

/// <summary>
/// Presents a new-model node source to the shared MasterNodeManager as an
/// IAsyncNodeManager. This adapter — and only this adapter — carries the
/// Processed / index-aligned-accumulator protocol on behalf of the new surface.
/// </summary>
internal sealed class NodeSourceManagerAdapter : IAsyncNodeManager
{
    private readonly NodeSourceRuntime m_runtime;

    public NodeSourceManagerAdapter(NodeSourceRuntime runtime)
        => m_runtime = runtime;

    public IEnumerable<string> NamespaceUris => m_runtime.NamespaceUris;

    // Read: MasterNodeManager fans the SHARED values/errors/nodesToRead lists to every
    // manager and relies on the Processed flag. The runtime below is first-match-wins;
    // this method is where the two contracts meet.
    public async ValueTask ReadAsync(
        OperationContext context,
        double maxAge,
        ArrayOf<ReadValueId> nodesToRead,
        IList<DataValue> values,
        IList<ServiceResult> errors,
        CancellationToken cancellationToken = default)
    {
        for (int ii = 0; ii < nodesToRead.Count; ii++)
        {
            ReadValueId nodeToRead = nodesToRead[ii];

            if (nodeToRead.Processed)
            {
                continue;   // already served by an earlier manager
            }

            if (!m_runtime.TryResolve(nodeToRead.NodeId, out NodeSourceHandle handle))
            {
                continue;   // not ours — leave Processed = false for the next manager
            }

            nodeToRead.Processed = true;

            var filter = new ReadFilter(
                nodeToRead.AttributeId, nodeToRead.ParsedIndexRange, nodeToRead.DataEncoding, context);

            (DataValue value, ServiceResult error) =
                await m_runtime.ReadAsync(handle, filter, cancellationToken).ConfigureAwait(false);

            values[ii] = value;
            errors[ii] = error;
        }
    }

    // The frozen seam still types the handle as object (see §2 deletion test #4);
    // the bridge boxes the portable struct because it does NOT touch the shared seam.
    public ValueTask<object> GetManagerHandleAsync(NodeId nodeId, CancellationToken ct = default)
        => new(m_runtime.TryResolve(nodeId, out NodeSourceHandle h) ? h : null!);

    // Browse/Write/Call/History/monitored-item members: identical shape — resolve, set
    // Processed, translate BrowsePage↔ref-ContinuationPoint, index-align. (~800 lines total,
    // matching AsyncNodeManagerAdapter's 812.)
}
```

**Invariants, ordering, error modes carried by the bridge (stated once here, not restated six times as in `INodeManager.cs`):**
- The bridge **must** skip `Processed == true` items and **must** set `Processed = true` for any it serves — this is the `MasterNodeManager` fan‑out contract (MasterNodeManager.cs:4051‑4073). A bug here means either double‑service or a spurious `BadNodeIdUnknown`.
- `TryResolve` **must not** block on I/O (same rule the existing `GetManagerHandleAsync` doc‑comment states). First‑match‑wins ownership is decided by namespace partition, so two node sources owning the same namespace index is a startup error.
- Browse translation **must** honour that the shared `Browse(ref ContinuationPoint, IList<ReferenceDescription>)` list "may already contain references" — the bridge appends, the runtime returns a fresh `BrowsePage`.
- Reads on the local serving path stay **synchronous and allocation‑free** where `Value(T)`/`OnRead(Func<T>)` is used, via the existing `BaseVariableState.OnReadValue` sync path; only `OnRead(Func<…ValueTask<T>>)` awaits.

##### 1.5 Can one server host BOTH an old‑style and a new‑style node manager in one address space?

**Yes, provably, with zero runtime changes.** Registration is symmetric — a new source is just another `IAsyncNodeManagerFactory`:

```csharp
services.AddOpcUa()
    .AddServer(o => { o.ApplicationUri = "urn:plant:line-a"; })
    .AddNodeManager<LegacyBoilerNodeManagerFactory>()     // OLD surface (AsyncCustomNodeManager)
    .AddNodeSource("urn:acme:line1", b =>                  // NEW surface (bridged)
    {
        b.Folder("Plant").Object("Tank1")
         .Variable<double>("Level").OnRead((_, ct) => modbus.ReadHoldingAsync(40001, ct));
    });
```

Both land in `StandardServer`'s factory lists; `CreateMasterNodeManagerAsync` builds both; `MasterNodeManager` fans out to both. The `HostedNodeManagerLifecycle` even supports **adding and hot‑reloading** either kind at runtime (`AddAsync(IAsyncNodeManagerFactory)` / `AddAsync(INodeManagerFactory)`).

**The cost of coexistence is the bridge, and it is paid once in the framework, not per consumer.** The two invariants a mixed address space must uphold are (a) disjoint namespace partitions between the old and new managers (already the default `MasterNodeManager` ownership rule) and (b) cross‑manager references stitched through `CreateAddressSpaceAsync`'s `externalReferences` dictionary — which works identically for both because both are `IAsyncNodeManager`.

##### 1.6 Does the new implementation reuse `Session`/`Subscription`/`SubscriptionManager`/`MasterNodeManager`, or fork them?

**It reuses all four, and forking any of them is rejected** (deletion tests in §2). This is the single most important design decision and the thing that keeps Option B affordable:

- **`MasterNodeManager` (6,791 lines): shared.** It is the multiplexer; the bridge is designed *precisely* so the new surface plugs into its existing fan‑out. Forking it would duplicate request routing, reference stitching, `TranslateBrowsePath`, and — fatally — split OPC UA service‑set semantics into two code paths that must each pass CTT.
- **`Session`/`Subscription`/`SubscriptionManager` (1,368 / 3,039 / 2,523): shared.** These sit *below* the node‑manager seam — the publish pipeline samples whatever `IAsyncNodeManager` serves. A new‑model node is a `NodeState` under the hood (produced by the builder), so monitored‑item sampling, Publish, Republish, and durable‑subscription restore all work unchanged.
- **`NodeState` (5,075‑line core; 13,485‑line `State` folder in `Opc.Ua.Types`): shared.** The new builder *produces* `NodeState` instances; it hides the 155‑member/18‑delegate authoring surface behind `Value/OnRead/OnWrite`, but the underlying node objects, `NodeStateSerializer`, and the client‑side `NodeState` cache are one shared implementation.

---

#### 2. What gets duplicated, shared, and frozen

##### 2.1 Inventory with line counts (verified)

| Bucket | Module | Lines | Disposition |
|---|---|---:|---|
| **FORKED — new** | `INodeSource`/builder/providers + `NodeSourceRuntime` | ~6,000–9,000 new | The new authoring surface (comparable to today's 8,814‑line `Fluent` folder) |
| **FORKED — new** | `NodeSourceManagerAdapter` (the bridge) | ~800 new | The only new runtime‑touching artifact; models `AsyncNodeManagerAdapter` (812) |
| **FROZEN‑BUT‑ALIVE** | `AsyncCustomNodeManager` | 7,080 | Kept as the old authoring base; receives no new features |
| **FROZEN‑BUT‑ALIVE** | `FluentNodeManagerBase` + `Fluent/*` | ~8,814 | Kept; the old fluent layer |
| **FROZEN‑BUT‑ALIVE** | `NodeState` 18‑delegate authoring surface | (subset of 5,075) | Delegates stay for old authors; new authors never see them |
| **DELETED** (back‑compat lifted) | `CustomNodeManager` (sync base), `SyncNodeManagerAdapter`, sync `INodeManager`/`2`/`3` chain, `IAsyncNodeManager.SyncNodeManager` | 5,489 + 294 + ~450 | Async‑only normalization; forces the 1 direct `INodeManager` implementer to move |
| **SHARED — untouched** | `MasterNodeManager` | 6,791 | The multiplexer |
| **SHARED — untouched** | `Session` / `Subscription` / `SubscriptionManager` | 1,368 / 3,039 / 2,523 | Publish pipeline, below the seam |
| **SHARED — untouched** | `StandardServer` / `ServerBase` (Core) | 4,963 / 1,630 | Lifecycle + transport |
| **SHARED — untouched** | `NodeState` data core + `State` folder | 5,075 / 13,485 | In `Opc.Ua.Types`; shared with the client |
| **SHARED — untouched** | `IServerInternal`/`ServerInternalData` | 1,355 | New surface wraps it in `IServerContext`; object graph shared |

**Headline: Option B duplicates ~10% of the assembly (the authoring layer) and shares ~90% (the runtime).** The phrase "two live implementations" is true only of the authoring surface; the runtime, and therefore the wire‑visible OPC UA semantics, are single‑sourced.

##### 2.2 Deletion test, applied four ways (two *for*, two *against* forking)

1. **Delete the bridge `NodeSourceManagerAdapter` → complexity reappears ×N.** Without it, every new‑model consumer would have to implement `IAsyncNodeManager` (14 capability interfaces + `Processed` + index alignment) itself. Concentrating it in one framework adapter is a pure **locality** and **leverage** win. **Verdict: the bridge earns its keep — it is the load‑bearing new module.** *(For the fork.)*
2. **Delete the *frozen* `AsyncCustomNodeManager` today → complexity reappears in 33 subclasses + several framework‑internal managers (`CoreNodeManager`, `DiagnosticsNodeManager`, `AliasNameNodeManager`, `FileSystemNodeManager`).** It cannot be deleted now. **Verdict: it earns its keep *today* — which is the justification for keeping it, but also (§4) the reason it cannot truly freeze.** *(Honest both ways.)*
3. **Delete a *hypothetical forked* `MasterNodeManager` (i.e., choose NOT to fork it) → nothing reappears, because the shared one already multiplexes heterogeneous managers.** A second `MasterNodeManager` would only *duplicate* routing and split CTT. **Verdict: forking `MasterNodeManager` fails the deletion test — do not fork it.** *(Against forking.)*
4. **Delete a *hypothetical forked* `NodeState` (a "NodeState2") → complexity reappears massively:** `NodeStateSerializer`, the client‑side `INodeCache`, and every companion spec bind to `NodeState`, which lives in shared `Opc.Ua.Types`. A parallel node type would fork the type system itself. **Verdict: forking `NodeState` fails the deletion test — the new surface must produce the shared `NodeState`.** *(Against forking.)*

The two "against" verdicts are what make Option B a *shallow* fork (authoring only). Any proposal that deepens the fork line below `IAsyncNodeManager` should be rejected on these grounds.

---

#### 3. Blast radius and migration story

##### 3.1 Consumers, counted precisely

**In‑repo authored node managers (round‑1 census: 33 non‑test subclasses + 1 direct `INodeManager`):**

| Group | Projects / count | Base today |
|---|---|---|
| Companion‑spec servers (`src`) | `XRegistry.Server` (3 × `CustomNodeManager2`), `WotCon.Server` (2), `Gds.Server.Common` (1 + 2 base types), `Di.Server` (1), `ISA95.Server` (1), `Positioning.Server` (1), `PubSub.Server` (1), `Robotics.Server` (1) | mix of `AsyncCustomNodeManager` / `CustomNodeManager2` / `FluentNodeManagerBase` |
| Framework‑internal (`Opc.Ua.Server`) | `CoreNodeManager`, `DiagnosticsNodeManager`, `AliasNameNodeManager`, `FileSystemNodeManager`, `ConfigurationNodeManager`, `FluentNodeManager` | `AsyncCustomNodeManager` / `FluentNodeManagerBase` |
| Samples (~10) | `ReferenceNodeManager`, `TestDataNodeManager`, `AlarmNodeManager`, `SampleNodeManager` (+`Boiler`,`MemoryBuffer`), `HaSampleNodeManager`, `FlatTagNodeManager`, `PumpNodeManager`, Minimal* | mostly `AsyncCustomNodeManager`; `SampleNodeManager` is the **1** direct `INodeManager` |
| Tests (~15 subclasses) | `Opc.Ua.Server.Tests`, `Opc.Ua.Redundancy.Server.Tests` | `AsyncCustomNodeManager` / `CustomNodeManager2` / `FluentNodeManagerBase` |

**Consumers of the deleted sync surface** (forced to move even in Option B): `SampleNodeManager` + its 2 subclasses (`Boiler`, `MemoryBuffer`), and any external sync `INodeManager` implementer. Everyone else on `AsyncCustomNodeManager`/`FluentNodeManagerBase` **stays put and keeps compiling.**

##### 3.2 Who moves, who stays

| Consumer | Move? | Why |
|---|---|---|
| The 30+ `AsyncCustomNodeManager`/`FluentNodeManagerBase` subclasses | **Stay** | Their base is frozen‑but‑alive; no source change required |
| `SampleNodeManager` + Boiler + MemoryBuffer | **Must move** | Sync `INodeManager` chain is deleted (async‑only normalization) |
| Framework‑internal managers (`CoreNodeManager` …) | **Stay initially; must eventually move** | They keep the old base alive → they are the reason it can't freeze (§4). Migrating them is the exit trigger |
| New device/greenfield servers | **New surface** | The point of the fork |
| HA/Redundancy (`Redundancy.Server`) | **Stay** | It binds below the seam (`ILocalAddressSpace`, stores); portable‑handle benefits only accrue to new‑surface nodes |

**Mixed state is stable and supported.** A codebase with some managers on the old base and some on the new source is not a transient hazard — it is the *steady state* the design intends, guaranteed by the single shared `MasterNodeManager`. This is Option B's genuine strength over a rewrite: no big‑bang migration, no flag day.

##### 3.3 Analyzer work — quantifiably less than Option A

The existing tooling (20 analyzers, 14 with fixes, the `Opc.Ua.MigrationAnalyzer.Core` shim library, the `<Type>Collection` generator) targets **client/types/config/certificates/GDS** — surfaces that were *removed*. Crucially, **there is no analyzer and no shim for the node‑manager authoring surface today.**

- **Option A (rewrite)** would need that missing suite built from scratch: analyzers to detect `: AsyncCustomNodeManager`, overrides of `CreateAddressSpaceAsync`/`New`/`LoadPredefinedNodesAsync`, the `object` handle, the `Processed` idiom; code fixes for the ~70% that are mechanical; and an `[OpcUaShim]`‑marked runtime shim re‑implementing the old base over the new runtime. Call it **~6–10 new analyzers + 2–3 fixers + a node‑manager shim assembly.**
- **Option B needs *zero mandatory* analyzers.** The old surface keeps compiling and running, so nothing must be rewritten. The only useful analyzers are **optional "nudge" diagnostics at `Info` severity** — e.g., `UA0101 (prefer INodeSource for new node managers)` — with **no shim library at all.** Realistically **0–2 optional analyzers, 0 fixers, 0 shim** versus A's suite.

**But price the flip side honestly:** an optional nudge analyzer only helps people who *choose* to move. In Option A the analyzer is doing load‑bearing work (unblocking a forced migration); in Option B it is a marketing device. The migration *guide* does the real work: a new `docs/migrate/…/node-authoring.md` next to the existing `node-states.md`, documenting the ladder, the deleted sync surface, and a side‑by‑side "old vs new" for each of the common‑6 overrides.

---

#### 4. Pros and cons of the side‑by‑side fork

##### Pros

- **Lowest migration risk of any option.** Nothing on `AsyncCustomNodeManager`/`FluentNodeManagerBase` breaks. 30+ subclasses and all external consumers keep compiling. The mixed state is the supported steady state, not a transition hazard.
- **The runtime is shared, so the scariest costs don't materialize.** Bug fixes to `MasterNodeManager`/`Session`/`Subscription`/publish are written **once** and benefit both surfaces. There is no "fix every bug twice" for the ~130,000‑line runtime.
- **CTT compliance is mostly shared.** Because both surfaces funnel through one `MasterNodeManager` + `Session`/`Subscription`, the transport, service dispatch, and publish semantics are single‑sourced. You need CTT green on **one new reference server built on the new surface** (to validate the bridge + new runtime's Browse/Read/Call/History resolution); the existing old‑surface reference server's CTT baseline stays valid untouched. Cost ≈ **1.3× CTT, not 2×** — *provided the old surface truly freezes.*
- **Incremental, shippable, reversible.** The new surface can ship behind one namespace with a handful of nodes proven first; if it stalls, the old surface is untouched. Elapsed time to a *usable* new surface is short because it is additive (weeks‑to‑months for the ladder + bridge), versus a rewrite's all‑or‑nothing cutover.
- **Back‑compat lifted buys one real deletion:** async‑only normalization removes `CustomNodeManager` (5,489) + `SyncNodeManagerAdapter` (294) + the sync interface chain — a net simplification even inside a "keep the old" option.

##### Cons (the honest price)

- **Double *authoring‑layer* maintenance is real, even if the runtime isn't.** Two node‑authoring test suites (the new surface, plus the frozen `AsyncCustomNodeManagerTests` 5,821 lines / `NodeManagerLifecycleTests` 8,406 lines stay), two doc sets (`node-states.md` + a new `node-authoring.md`), and two mental models. Every node‑authoring example, sample, and tutorial must pick a side or show both.
- **"Old is frozen" is aspirational, not real — and the code proves it.** Several **framework‑internal** node managers (`CoreNodeManager`, `DiagnosticsNodeManager`, `ConfigurationNodeManager`, `AliasNameNodeManager`, `FileSystemNodeManager`) derive from `AsyncCustomNodeManager`. The server *cannot start* without them. So the "frozen" base keeps receiving fixes and forward‑ports as long as the framework itself rides on it. It only truly freezes after the framework's own managers migrate to the new surface — which is most of the work of a rewrite anyway.
- **Assembly size and trimming get worse, and I won't pretend otherwise.** Both authoring surfaces are public API rooted through `AddNodeManager(IAsyncNodeManagerFactory)` *and* used by the framework‑internal managers, so the IL linker **cannot** trim the old base classes away — a consumer who uses only the new surface still ships `AsyncCustomNodeManager` (7,080) + `FluentNodeManagerBase` + `Fluent/*` (~8,814). NativeAOT/trimming compatibility is preserved (no reflection), but **size is strictly larger than either a rewrite or the pre‑fork baseline.** A `[FeatureSwitchDefinition]` trim switch could gate the old surface *only after* the framework‑internal managers stop using it — i.e., only after the exit is reached.
- **Confusion cost is high and compounds.** Two ways to do everything means newcomers must choose before they understand the tradeoff; Stack‑Overflow answers and blog posts split; and — specifically relevant here — **AI coding agents degrade.** With both `AsyncCustomNodeManager` and `INodeSource` valid and both present in‑repo, a model retrieving "how to write a node manager" will find and often copy the *old* 93‑virtual pattern, because there is more of it in the corpus. A rewrite gives one answer; a fork gives two and lets the more‑common (older) one win by default.
- **Parity risk → permanent fork.** If the new surface never reaches feature parity for some hard case (bespoke sampling‑group scheduling, cross‑namespace id ownership, exotic history), authors keep dropping to the old base, the old base never freezes, and the "temporary" fork becomes **permanent double authoring‑layer maintenance** with no trimming win and a doubled doc/test burden forever.
- **The `object` handle leak and other frozen‑seam defects are *inherited*, not fixed.** Because Option B‑minimal does not touch `IAsyncNodeManager`, the bridge must implement `GetManagerHandleAsync : ValueTask<object>` (see §1.4) — the exact `object`‑in‑public‑API defect round 1 wanted gone. The new surface hides it from authors, but it remains in the shared seam. A rewrite deletes it.

##### Exit strategy (and what happens without one)

**The exit is a major version (3.0):** delete `AsyncCustomNodeManager`/`FluentNodeManagerBase` once (a) all framework‑internal managers are re‑expressed on the new surface, (b) all in‑repo companion specs and samples are migrated, and (c) an adoption/telemetry threshold shows external usage has crossed over. Only then can the trim feature‑switch drop the old IL.

**The trap:** step (a) is most of a rewrite. If the organization keeps deferring it because "the old one still works," the fork never closes. **A fork with no funded, scheduled exit is not a migration strategy — it is a permanent tax dressed as one.** The exit must be committed *before* the fork ships, or Option B silently becomes the most expensive option of all.

##### Packaging / namespacing

Same package (`OPCFoundation.NetStandard.Opc.Ua.Server`), **same assembly**, **distinct namespace** `Opc.Ua.Server.Nodes`. A separate package is impossible without a circular dependency, because the new surface's bridge references the shared runtime and the shared runtime's DI extensions reference the new registration. The consumer's `.csproj` is unchanged (same `PackageReference`); their `using` list gains one line:

```xml
<!-- unchanged -->
<PackageReference Include="OPCFoundation.NetStandard.Opc.Ua.Server" Version="…" />
```
```csharp
using Opc.Ua.Server;         // shared runtime, StandardServer, hosting
using Opc.Ua.Server.Nodes;   // NEW authoring surface; omit to stay on the old one
```

The distinct namespace is deliberate: keeping both surfaces in `Opc.Ua.Server` would put two `Variable<T>` verbs and two node‑manager idioms in one IntelliSense list — maximizing the confusion cost above.

##### Source generators

They currently emit against the **old** surface (`[NodeManager]` → typed `Configure(I…Builder)` traversal, ultimately `FluentNodeManagerBase`). Under Option B they must **emit for both** during the fork's life: keep the old template working (frozen), and add a new template that emits `INodeSource` + the typed traversal + the `TryGetBehavior` switch for the data/behaviour split. That is **generator double‑maintenance** — a concrete instance of the fork's cost landing in tooling, not just docs.

---

#### 5. What would make this the right choice — and where the soft fork wins

##### Option B (hard fork) is clearly correct when:

1. **A second, structurally‑incompatible node‑serving engine is a hard requirement.** If you need an address space that is fundamentally remote/async — where the shared runtime's synchronous `ILocalAddressSpace.TryGetNode` fast path and in‑process `NodeState` materialization are *wrong*, not just inconvenient — then the new runtime is genuinely a different implementation, and bridging it at `IAsyncNodeManager` is the honest way to run it beside the old one. This is the *only* justification that survives scrutiny, because it is the one thing the soft fork cannot do.
2. **You have a funded, scheduled exit** (a committed 3.0 that migrates the framework‑internal managers), so the double maintenance is bounded and the trimming win eventually lands.
3. **A large, un‑migratable external consumer base** makes any forced migration (Option A) commercially unacceptable, and you accept permanent‑ish authoring‑layer duplication as the price of never breaking them.

##### It is clearly the wrong choice when:

1. **The goal is authoring ergonomics** (which the evidence says it is: 93 virtuals, 155‑member `NodeState`, 55 of 80 overrides never used). Ergonomics is fixed by a *facade*, not a second engine.
2. **There is no committed exit.** Without one, you get doubled docs/tests/generators and a strictly larger, un‑trimmable assembly, forever.
3. **The HA/portable‑handle gains are the motivation** — because round 1's ports design shows those can be retrofitted into the *shared* `MasterNodeManager` (portable `NodeManagerHandle`, `Processed` retired by handle‑routing) **without a second engine at all.**

##### The soft fork dominates — say it plainly

Round 1's `commoncase` rung 4 (the un‑obsoleted old surface as a *documented step down*, with an `internal sealed NodeSourceRuntime` **facade over the same shared bases**) is the *soft* fork: **one node‑serving implementation, two layered entry points.** Compare it to the hard fork I designed above:

| | Soft fork (facade) | Hard fork (Option B) |
|---|---|---|
| Node‑serving implementations | **1** (facade drives the shared engine) | **2** (new runtime + old bases) |
| New bridge adapter | not needed (facade *is* an `AsyncCustomNodeManager`) | **required** (~800 lines) |
| `Processed`/`object`‑handle defects | fixable in the one shared engine | inherited by the bridge |
| CTT | shared (one engine) | 1.3× (bridge + new runtime need a gate) |
| Doc/test/generator duplication | ergonomic layer only | full authoring layer |
| Assembly size | ~baseline | strictly larger (un‑trimmable old bases) |
| Ergonomic benefit delivered | **~90%** (ladder, minted nodes, narrow `IServerContext`, three read shapes) | ~100% |

The hard fork buys the last ~10% of ergonomic polish and one capability the soft fork lacks — a genuinely separate engine — **at the price of a second implementation, a bridge, inherited seam defects, 1.3× CTT, and doubled docs/tests/generators.** Unless requirement (1) above is real (a structurally different remote address space), **that last 10% is not worth a permanent second implementation.**

**My honest recommendation, even though Option B is my assigned option:** absent a hard requirement for a second node‑serving engine, adopt the **soft fork** — one shared engine, the new `INodeSource` ladder as a facade over it, the old `AsyncCustomNodeManager` kept un‑obsoleted as rung 4, and the round‑1 ports improvements (`IServerContext`, frozen bind phase, portable `NodeManagerHandle`, `Processed` retirement) applied *in place* to the shared runtime. That captures the entire measured win (the 93→~6 authoring collapse, the service‑locator removal, HA‑correctness) with one implementation to test, document, generate for, and trim. Reserve the hard fork for the day a fully‑remote address space actually demands a second engine — and if that day comes, the bridge in §1.4 is exactly how you bolt it on beside the first without a flag day.

---

## The NodeState question

**Assignment:** *Determine whether `NodeState` should be re-cut — the work round 1 explicitly refused. Permitted to conclude it should NOT be. Feed the conclusion to both delivery options in a form they can consume.*

> Reproduced verbatim as delivered. Section numbering is the designer's own.

### Re-cutting `NodeState` — a State-Placement Proposal

#### 1. Inventory

##### 1.1 What is in the 5,824 lines

`NodeState.cs` declares `public abstract class NodeState : IFormattable, ICloneable`. The file has **182 `public` declarations** (of which 18 are delegates and 3 are result structs), leaving **~155 public members on the class**, plus **26 protected**, **64 virtual + 2 abstract (≈65 virtual/abstract)**, **28 private fields**, **2 events**, and **18 public delegates**. I categorised every member from the LSP symbol table:

| # | Concern | Members | ~Lines | What it is |
|---|---|---:|---:|---|
| 1 | **Serialisation** | ~42 methods + `AttributesToSave` enum | ~1,500 | `SaveAsBinary`/`LoadAsBinary`, `SaveAsXml`/`LoadFromXml`(×4), `Save`/`Update`/`SaveChildren`/`SaveReferences`/`LoadNode`/`UpdateUnknownChild`/`LoadUnknownNode`, `Export`(×2), `Clone`/`CreateCopy`/`CopyTo`/`DeepEquals`/`DeepGetHashCode` |
| 2 | **Attribute interception** | 31 delegate fields (`OnRead*`/`OnWrite*`, `OnValidate`) | ~300 | *code*, per-attribute read/write hooks; `BaseVariableState` adds ~20 more (`OnReadValue`, `OnReadValueAsync`, …) |
| 3 | **Lifecycle / creation** | ~26 | ~700 | ctor, `Initialize`(×6), `Create`/`CreateInternal`/`CreateAsPredefinedNode`, `Delete`, `AssignNodeIds`(×2), `OnBefore/AfterCreate/Delete` |
| 4 | **Topology (parent/child)** | ~24 | ~700 | `FindChild`(×3), `CreateChild`, `AddChild`/`RemoveChild`, `SetChildValue`(×10), `GetChildren`, `GetInstanceHierarchy` |
| 5 | **Attribute storage (data)** | ~20 properties + fields | ~400 | `NodeId`, `NodeClass`, `BrowseName`, `DisplayName`, `WriteMask`, `RolePermissions`, `AccessRestrictions`, … |
| 6 | **Attribute read/write dispatch** | 11 methods | ~600 | `ReadAttribute`(×2), `ReadAttributeAsync`, `WriteAttribute`(×2), `WriteAttributeAsync`, `ReadValueAttribute` |
| 7 | **References** | 10 methods + `m_references` + 2 delegates | ~300 | `AddReference`/`RemoveReference`/`ReferenceExists`/`GetReferences`/`UpdateReferenceTargets` |
| 8 | **Events / conditions** | 10 methods + `Notifier` + 4 delegates | ~350 | `ReportEvent`/`ReportEventAsync`, `AddNotifier`, `ConditionRefresh`, `AreEventsMonitored`, `FindMethod` |
| 9 | **Change tracking** | ~6 + `NodeStateChangeMasks` enum + 2 events | ~200 | `ChangeMasks`, `ClearChangeMasks`/`Async`, `RaiseStateChangedAsync`, `StateChanged`(`Async`) |
| 10 | **Browsing** | 3 methods + 2 delegates | ~250 | `CreateBrowser`, `CreateDefaultNodeBrowser`, `PopulateBrowser` |
| 11 | **Validation / type-system** | 2 + `IsPartOfTypeHierarchy` | ~80 | `Validate`, `ValidationRequired` |

**Two findings that dominate the rest of this proposal:**

- **Serialisation (concern 1) is the single largest bucket — ~42 methods / ~1,500 lines** — and it is pure data marshalling that already exists *twice more* outside the type (`NodeStateSerializer`, `UANodeSetHelpers`).
- **Locks.** The 4 collection locks are compliant `System.Threading.Lock` (`m_referencesLock`, `m_childrenLock`, `m_notifiersLock`, `m_areEventsMonitoredLock`, lines 5473-5476). But `ReadAttributeAsync`/`WriteAttributeAsync` take **`lock(this)`** (lines 3926, 4255) under an explicit `#pragma warning disable CA2002, RCS1059` with a `// weak-identity lock on 'this' is intentional: external callers synchronise via lock(source)` comment and a TODO. This is a **published synchronization contract in the interface**: `AsyncCustomNodeManager` (12 sites), `BaseVariableState` (6), `CustomNodeManager` (1) all do `lock(source)` on the node. It is the largest single obstacle to the re-cut, and it cannot cross a replica.

##### 1.2 Hierarchy — depth and counts

The spine is shallow structurally but deep on two branches (verified against `State/readme.md`):

```
NodeState (abstract)
├─ BaseTypeState → {BaseObjectTypeState, BaseVariableTypeState, ReferenceTypeState, DataTypeState}
└─ BaseInstanceState (IFilterTarget)
   ├─ BaseObjectState → FolderState → [Core.Types behaviour: ConditionState → AcknowledgeableConditionState
   │                                    → AlarmConditionState → LimitAlarmState → ExclusiveLimitAlarmState]
   ├─ BaseVariableState → BaseDataVariableState → BaseDataVariableState<T> → .Implementation<TBuilder>
   │                    → PropertyState → PropertyState<T> → .Implementation<TBuilder>
   ├─ MethodState
   └─ ViewState
```

- **Deepest chain = 7** (`DataTypeDescriptionState : BaseDataVariableState<string>.Implementation<VariantBuilder>` — verified in generated `Opc.Ua.NodeStates.g.cs`).
- **Hand-written subclasses: 28** (22 in `Opc.Ua.Types/State`, 2 base + the condition/alarm/state-machine behaviour types in `Opc.Ua.Core.Types/State` — **18 files / 6,023 lines** including `AlarmConditionState` 878+816, `ConditionState` 835, `FiniteStateMachineState` 806, `AcknowledgeableConditionState` 570).
- `Opc.Ua.Types/State` = **22 files / 15,434 lines** (matches brief); the full state surface is ~21,457 lines across the two folders.

##### 1.3 Generated surface bound to the shape

Concrete subclasses are **overwhelmingly generated**, not authored:

- Core model alone: **456 generated `partial class …State`** in `Opc.Ua.NodeStates.g.cs` (**9.2 MB**), plus **`Opc.Ua.NodeStates.ex.g.cs` (22.65 MB)** — an imperative factory of `CreateXxx(context) → state.CreateAsPredefinedNode(context) → nodes.Add(state)` calls, i.e. the address space materialised in code. Per-namespace `Add<Ns>(this NodeStateCollection, ISystemContext)`.
- Base-class distribution across the 456 (verified): **116 `MethodState`, 77 `BaseObjectState`, 9 `BaseDataVariableState<T>`**, and deep chains through the hand-written behaviour types (`AlarmConditionState`, `LimitAlarmState`, `ExclusiveLimitAlarmState`, `BaseEventState`, `FiniteStateMachineState` …). **The generator emits inheritance that mirrors the OPC UA type tree**, and for the alarm/condition/state-machine families it inherits ~4,000 lines of *hand-written behaviour*.
- Every companion spec repeats this: DI, GDS, PubSub, Robotics, ISA95, WoT, Positioning, xRegistry, OpenUsd (`Opc.Ua.<Spec>.NodeStates.g.cs`). The generator is the **primary delivery mechanism** for models.

##### 1.4 Non-server dependants

`NodeState` lives in `Opc.Ua.Types`, but the client barely touches it:

- **`INodeCache` returns `Node`/`INode`** (`FetchNodeAsync : ValueTask<Node?>`), backed by the separate lightweight `Node`/`NodeTable`/`TypeTable`/`ReferenceTable` model in `Opc.Ua.Types/Nodes` (`Node : IEncodeable, ILocalNode`). **Client complex types use zero `NodeState`.**
- `NodeState` leaks into the client **only** through the cold `CoreClientUtils.NodeSetExport.cs` path (`INode → NodeState → NodeSet2`).
- `UANodeSetHelpers` (shared) `Import(NodeStateCollection)` / `Export(NodeState)` is the NodeSet2-XML ↔ graph bridge — itself a serializer.

**Consequence:** a data-oriented, NodeId-keyed, `IEncodeable` node representation *already exists and ships* (it is what the client uses). The re-cut is a **server-side** concern; the client does not constrain it.

---

#### 2. Verdict: re-cut — but along the **data/behaviour** seam, not the inheritance seam

**Re-cut: YES, partially.** Split the *object* into a **`NodeData`** core (topology, references, attribute values, change byte — data, sealed, serialisable, no delegates, no `lock(this)`) and an **`INodeBehavior`** bundle (the 50+ delegates, custom browse, method handlers, condition/alarm/state-machine logic — code, node-local, re-attached per replica). Extract serialisation onto the data core. Keep the typed C# subclass as a **typed accessor over `NodeData` + a bound behaviour**, emitted by the generator.

**Do NOT** attempt to make `NodeState` "composable-not-inherited" wholesale — `design-flexible` was right to decline *that* framing. The inheritance spine is not the disease; the **conflation of behaviour and data inside one mutable, self-locking object** is.

##### Deletion test, per concern (N = where complexity reappears)

| Concern | Delete from the node? | Verdict |
|---|---|---|
| **Serialisation** (42 methods) | Complexity reappears in **N = 2** modules (binary + NodeSet/XML) that already exist (`NodeStateSerializer`, `UANodeSetHelpers`). Pure marshalling over data. | **EXTRACT** — biggest win, −1,500 lines from the type |
| **Attribute interception** (31+ delegates) | It is *code*, not data — reappears only at the **25 distinct override names actually used** (round 1: 55 of 80 virtuals never overridden). | **EXTRACT to `INodeBehavior`** — the HA-correctness win |
| **Change tracking** (events) | Reappears as **N = 1** address-space change feed (already `NodeStateChange`/`IAddressSpaceSynchronizer` in Redundancy). The `ChangeMasks` byte stays as data. | **RELOCATE** to the address space |
| **`lock(this)` contract** | Reappears as a private `System.Threading.Lock` + the already-existing async no-lock path. **N = 19** framework sites stop doing `lock(source)`. | **DELETE the contract** |
| **Browsing** | Default browse is a *function of reference data* → runtime `NodeBrowser` (exists). Custom browse is behaviour. | **SPLIT**: default → runtime, custom → `INodeBehavior` |
| **Topology / References / Attribute storage** | Delete and it reappears **everywhere** — this *is* the node. Data. | **KEEP as `NodeData`**, slim the mutation API |
| **Lifecycle (Create/AssignNodeIds)** | Reappears in the generator factory + node managers. A build protocol. | **KEEP slim** (generator needs `CreateAsPredefinedNode`) |
| **Events/conditions, state machines** | Reappears as ~6,000 lines of stateful behaviour reused by 60-80 leaves. | **KEEP as behaviour** (see below) |

##### Inheritance: load-bearing, or inheritance-as-code-reuse? Both — honestly.

- **For the structural NodeClass spine** (`Object`/`Variable`/`Method`/`View`/`Type`) and the **typed value generic** (`BaseDataVariableState<T>.Implementation<TBuilder>`): inheritance is thin, structural, and **fits the domain**. The `<T>`+builder form was *already re-cut in 2.0* to be zero-allocation and reflection-free (`node-states.md`). Keep it.
- **For alarm/condition/state-machine**: inheritance carries ~4,000 lines of **real behaviour** reused by 60-80 generated leaves (`ExclusiveLimitAlarmState` genuinely reuses `AlarmConditionState`→`ConditionState`→`BaseEventState`). This is the honest counter-argument to "sealed by default" — here inheritance is **load-bearing behaviour reuse that matches the standard's type tree** (`ExclusiveLimitAlarmType → LimitAlarmType → …`). **But** it is exactly the least-replicable state (an alarm's acked/active state is code-driven). The reconciliation: **keep the behaviour inheritance, but express it as a behaviour *provider* keyed by `TypeDefinitionId`, attached to `NodeData`, not baked into the data object.**
- **The domain type tree itself is already data, not C# inheritance**, for instances: a `ServerObjectState` is `: BaseObjectState`, and its `ServerType`-ness lives in `TypeDefinitionId` — verified. So modelling the *information model* does not require the node's C# class to inherit; only *typed access* and *behaviour reuse* do.

**Net:** the inheritance is ~70% justified (structure + typed value + genuine behaviour families) and ~30% accidental (behaviour delegates and serialisation riding on the data object). Re-cut the 30%.

---

#### 3. The design

##### 3.1 `NodeData` — the data core (Category C: replicable)

```csharp
namespace Opc.Ua;

/// <summary>
/// The replicable data of one node: identity, attribute values, topology and references.
/// No delegates, no events, no lock(this), no `object`. Serialisable and AOT-safe.
/// Invariants: NodeId is immutable after Create; NodeClass is immutable for life;
/// references and children are mutated only through the owning IAddressSpaceWriter,
/// which is the single writer (Category C is single-writer or CRDT).
/// Reads are synchronous and allocation-free; writes go through the writer and raise
/// a NodeStateChange on the address-space feed (not a per-node event).
/// </summary>
public sealed class NodeData
{
    public NodeData(NodeClass nodeClass) => NodeClass = nodeClass;

    public NodeId NodeId { get; init; } = NodeId.Null;      // INullable → .IsNull, never Nullable<T>
    public NodeClass NodeClass { get; }
    public QualifiedName BrowseName { get; set; } = QualifiedName.Null;
    public LocalizedText DisplayName { get; set; } = LocalizedText.Null;
    public AttributeWriteMask WriteMask { get; set; }
    public ArrayOf<RolePermissionType> RolePermissions { get; set; }   // ArrayOf<T>, not IReadOnlyList
    public AccessRestrictionType? AccessRestrictions { get; set; }

    /// <summary>The Value attribute for Variable/VariableType nodes; Variant.Null otherwise.</summary>
    public Variant Value { get; set; } = Variant.Null;      // Variant, never object
    public StatusCode StatusCode { get; set; }
    public DateTimeUtc SourceTimestamp { get; set; }

    // Data-only topology + references. Mutated only by IAddressSpaceWriter.
    public ArrayOf<NodeId> Children { get; internal set; }
    public ReferenceSet References { get; internal set; }   // today's ReferenceDictionary, minus behaviour

    public NodeStateChangeMasks ChangeMask { get; internal set; }   // stays as a data byte
}
```

`NodeData` is what `NodeStateSerializer` already round-trips (it reconstructs a *generic base state* from `NodeClass` + `SaveAsBinary` and re-attaches behaviour — verified). Serialisation moves onto it as a free function: `NodeDataSerializer.Save(in NodeData, BinaryEncoder)` / `Load(NodeClass, BinaryDecoder)`.

##### 3.2 `INodeBehavior` — the behaviour bundle (Category A: node-local, never replicated)

```csharp
namespace Opc.Ua.Server;

/// <summary>
/// All node behaviour that today lives as NodeState delegates/virtuals. Code, never
/// serialised. Re-attached locally by the owning node manager (INodeBehaviorSource)
/// after a replica hydrates NodeData. A node with no custom behaviour has NO bundle
/// (null), so the common case allocates nothing beyond NodeData.
///
/// Ordering: OnReadValueAsync/OnWriteValueAsync own their own thread-safety and run
/// WITHOUT any node lock (this is already true of BaseVariableState.OnReadValueAsync,
/// BaseVariableState.cs:578). A handler that returns Handled=true is the value source;
/// the runtime does NOT post-process (index range/encoding) — same contract as today.
/// Error mode: a handler returns ServiceResult; it never throws for a Bad status.
/// </summary>
public interface INodeBehavior
{
    ValueTask<AttributeReadResult>  OnReadValueAsync (ISystemContext ctx, in ReadValueId id, CancellationToken ct);
    ValueTask<AttributeWriteResult> OnWriteValueAsync(ISystemContext ctx, in WriteValue  wr, CancellationToken ct);
    ValueTask<CallMethodResult>     OnCallAsync      (ISystemContext ctx, in CallMethodRequest r, CancellationToken ct);

    /// <summary>Custom browse; null-return means "use the data-driven default browser".</summary>
    NodeBrowser? CreateBrowser(ISystemContext ctx, in BrowseDescriptor descriptor);
}

/// <summary>
/// Attaches behaviour to nodes by NodeId. Implemented by a node manager AND emitted by
/// the source generator. Two adapters today ⇒ a real seam: hand-written + generated.
/// </summary>
public interface INodeBehaviorSource
{
    bool TryGetBehavior(NodeId nodeId, [NotNullWhen(true)] out INodeBehavior? behavior);
}
```

The condition/alarm/state-machine families become **behaviour implementations keyed by `TypeDefinitionId`** (a `ConditionBehavior`, `AlarmConditionBehavior : ConditionBehavior`, …). Inheritance survives *there* — where it is genuine behaviour reuse — but it is off the data path, so a replica hydrates `NodeData` and re-binds `AlarmConditionBehavior` locally. This is precisely what `NodeStateSerializer`'s doc-comment already promises ("re-attached by the owning node manager on the active replica").

##### 3.3 What the source generator emits

Today the generator emits (a) a typed subclass and (b) an imperative factory. Under the split it emits the **same two things, decoupled**:

```csharp
// (a) Typed accessor — a readonly struct VIEW over NodeData, zero-inheritance, AOT-safe.
public readonly struct BoilerType
{
    private readonly NodeData m_node;
    public BoilerType(NodeData node) => m_node = node;

    public NodeData Node => m_node;
    public AnalogUnitType Temperature => new(m_node.Child(BrowseNames.Temperature));  // typed child access preserved
    public double TemperatureValue => m_node.Child(BrowseNames.Temperature).Value.GetValueOrDefault<double>();
}

// (b) Data factory — builds NodeData (not a behaviour graph). Still imperative, still AOT,
//     but now emits pure data the replicator can also emit as a NodeSet2 blob.
public static NodeData CreateBoilerType(ISystemContext ctx) { /* set attributes, children, refs */ }

// (c) Behaviour bundle — separate, only when the type has methods/handlers.
internal sealed class BoilerTypeBehavior : INodeBehavior { /* method handlers */ }
```

The generator *wants* two things above all: **typed access to children/values** and **an AOT, reflection-free construction path**. Both survive. The struct-view form is strictly friendlier to AOT and to the `internal sealed` runtime than a 456-deep inheritance tree.

##### 3.4 Hot path — Read and Browse

```csharp
// READ (single node, common case: no behaviour). No lock, no await, no allocation.
public ServiceResult ReadValue(NodeData node, in NumericRange range, ref DataValue value)
{
    if (m_behaviorSource.TryGetBehavior(node.NodeId, out INodeBehavior? b))   // dictionary hit only for custom nodes
    {
        return b.OnReadValueAsync(...).AsResult();   // async path, no node lock (as today)
    }
    value = new DataValue(node.Value, node.StatusCode, node.SourceTimestamp);  // pure field read
    return ApplyRange(ref value, range);
}

// BROWSE. Default browser walks NodeData.References (data). Custom browse only if a behaviour exists.
public NodeBrowser Browse(NodeData node, in BrowseDescriptor d)
    => (m_behaviorSource.TryGetBehavior(node.NodeId, out var b) ? b.CreateBrowser(ctx, d) : null)
       ?? DataBrowser.Over(node.References, d);   // struct enumerator over ReferenceSet
```

**Performance characteristics (the interface, not the impl):** the common-case Read is a field read with no lock and no `await`; today the same read either takes `lock(this)` (sync path) or allocates a `ValueTask` (async default). Browse over data is a struct enumerator. Behaviour costs exactly **one dictionary probe** and is paid only by nodes that have custom behaviour.

---

#### 4. Cost, risk, and migration

##### 4.1 What breaks

- Every `node.OnReadValue = h` / `OnWriteValue` / `OnSimpleReadValue` (and the 31 `NodeState` + ~20 `BaseVariableState` handler fields) moves to a behaviour bundle.
- Custom subclasses overriding protected virtuals (`ReadNonValueAttribute`, `PopulateBrowser`, `ConditionRefresh`, …) — the 25 distinct override names — must move logic into `INodeBehavior`.
- The 19 framework `lock(source)` sites must stop locking the node.
- The 42 serialisation methods leave the type (callers use `NodeDataSerializer`/`UANodeSetHelpers`).

##### 4.2 What the analyzer can carry — honest split

`Opc.Ua.MigrationAnalyzer` ships UA0001-UA0023 with ~70% auto-fix and a `[OpcUaShim]` runtime library — **but none target the node-manager/`NodeState` authoring surface** (verified: no analyzer references `NodeState`/`NodeManager`). New rules would be needed:

| Migration class | Mechanism | Share |
|---|---|---|
| `node.OnXxx = handler` field assignments | **Auto-fix**: rewrite to `behavior.OnXxx = handler` (delegate shape preserved) | ~35% |
| Old serialisation calls (`node.SaveAsBinary`) | **Auto-fix**: redirect to `NodeDataSerializer.Save(node)` | ~10% |
| `[OpcUaShim] NodeState` facade exposing legacy `OnXxx` fields forwarding to a bundle | **Shim** (transition window; same pattern as the existing client/config shims) | ~20% overlap |
| Protected-virtual overrides → `INodeBehavior` | **Detect-only** (CS0115-style) + guidance | ~20% |
| Condition/alarm/state-machine behaviour relocation | **Manual rewrite** | ~15% |

Realistic: **~45% auto-fix/shim-carried, ~20% compiler-guided rename, ~35% manual** — heavier on manual than the average 2.0 sub-migration, because behaviour relocation is semantic.

##### 4.3 Precedent already set by `docs/migrate/2.0.x/node-states.md`

This is direct precedent that re-cutting `NodeState` is **feasible and accepted**:

- **`NodeState` no longer implements `IDisposable`** — "Node states do not manage resources, they access resources… management of resources must be done in a node manager." **This is the data/behaviour direction already shipped.**
- **Typed `BaseVariableState<T>`/`BaseVariableTypeState<T>` re-cut** to zero-allocation, reflection-free **builder structs** (`VariantBuilder`, `StructureBuilder<T>`, `EnumBuilder<T>`).
- **`Clone()` → `CreateCopy()` + `CopyTo()`** (new `protected abstract NodeState CreateCopy()`).
- **`BaseVariableState` `Read(object, ref object)`/`Write(object)` removed.**
- **Predefined nodes are now source-generated**; `AddBehaviorToPredefinedNode` receives the generated instance state and *attaches behaviour to it* — the authoring model is already "generated data + attached behaviour".

Every one of these was delivered with **compiler-guided breaks (CS0115), recompilation, and manual guidance — no analyzer/shim**. The team has already broken this exact surface, repeatedly, this way.

##### 4.4 Performance risk — with numbers

- **Win:** a node with no custom behaviour drops **~50 nullable delegate slots** (`NodeState` 31 + `BaseVariableState` ~20). At 8 bytes each that is **~400 bytes/node**; at 100k nodes ≈ **~40 MB** and materially better cache locality on Browse/Read (the fields are gone from the hot object). The common-case Read also loses `lock(this)`.
- **Cost:** custom-behaviour nodes pay **one dictionary probe** per Read/Browse/Call to reach `INodeBehavior`. Mitigation: `NodeData` may hold a single `object?`-free `INodeBehavior?` reference field (behaviour is server-side, not replicated, so a local reference is legitimate) turning the probe into a null-check — but that reintroduces one reference field per node, so it is a tunable (field vs side-table).
- **Reasoning where I lack numbers:** no design was benchmarked (round 1's caveat holds). The claim "common-case Read gets cheaper" rests on: fewer fields + no lock + no `await` when no behaviour exists. The claim "custom Read unchanged" rests on the async no-lock path already being live (`AsyncCustomNodeManager` calls `ReadAttributeAsync` at 3505/3697/3824; `MonitoredNode` at 462/751 — verified).

---

#### 5. Feed to the two delivery options

**Direct answers for the two designers.**

##### Does this make a full rewrite MORE or LESS attractive? — **MORE.**

The data/behaviour split is far cheaper to *build greenfield* than to retrofit onto a 5,824-line self-locking type. A rewrite of `Opc.Ua.Server` can adopt `NodeData` + `INodeBehavior`/`INodeBehaviorSource` + generator-emits-both from day one, and **reuse machinery that already exists**: the client's `Node`/`NodeTable`/`TypeTable` data model, `NodeStateSerializer` (already does data-only round-trip), `UANodeSetHelpers`, `INodeStateStore`/`NodeStateChange`/`IAddressSpaceSynchronizer` (Redundancy). The rewrite does **not** need to invent the data model — it needs to *promote the client's data model to the server* and attach behaviour beside it. Recommend the full rewrite take `NodeData` as its address-space primitive and treat `INodeBehavior` as the only extension seam.

##### Does this make a side-by-side fork MORE or LESS viable? — **Viable, but only at namespace/node-manager granularity, and it forces the `lock` cut to be global.**

- **Can old-style and new-style `NodeState` coexist in ONE address space? Yes — at the routing boundary, No — within one node graph.** `MasterNodeManager` already routes by `NodeId`/namespace over `NodeManagerRoutingTable : IReadOnlyList<IAsyncNodeManager>`. A new-style node manager (serving `NodeData`) can sit beside an old-style `CustomNodeManager2` (serving `NodeState`); the master routes per node. **But a single parent/child graph must be homogeneous**, and the generator emits one shape — so migration is a **hard cut per node manager, a soft cut per server**. Companion specs migrate one at a time.
- **The one thing a fork cannot defer is the `lock(source)` contract.** If new-style nodes drop `lock(this)`, any framework code that does `lock(source)` on a node it did not author will break. So the runtime (`MasterNodeManager`, browsers, `AsyncCustomNodeManager`) must **stop taking `lock(source)` before any new-style node exists** — a coordinated change, but bounded to the **19 measured sites**. Sequence it first; it is a prerequisite for the fork, not a consequence of it.
- **`NodeState` can front `NodeData` during the fork.** Because `NodeStateSerializer` proves data is reconstructable into a generic base, an `[OpcUaShim]` `NodeState` can *wrap* a `NodeData` + optional `INodeBehavior`, exposing the legacy `OnXxx` fields as forwarders. That lets old node managers keep compiling against `NodeState` while the graph underneath is already `NodeData` — the fork's bridge.

**Bottom line for the decision:** the re-cut is real and worth doing, but its *shape* is "extract behaviour + serialisation, keep the typed/behaviour inheritance," not "de-inherit the graph." That shape **favours the full rewrite** (greenfield data core, reusing the client model) and makes the fork **possible but not cheap** (per-node-manager hard cuts, with a mandatory up-front removal of the `lock(source)` contract). Neither option is blocked by client dependencies — `NodeState` is effectively server-only.

---

## Cross-cutting findings

### New finding: a second lock leak

`ServerRuntimeAnalysis.md` documented an `object`-typed diagnostics lock published on three
interfaces and taken in 88 `lock` statements. **`r2-nodestate` found a second, independent
lock leak** — and I verified it directly.

`NodeState.ReadAttributeAsync` and `WriteAttributeAsync` take `lock (this)`:

```csharp
// src/Opc.Ua.Types/State/NodeState.cs:3925-3927 and 4254-4256
#pragma warning disable CA2002, RCS1059 // weak-identity lock on `this` is intentional: external callers synchronise via lock(source)
lock (this)
#pragma warning restore CA2002, RCS1059
```

The suppression comment states the contract explicitly: *external callers synchronise via
`lock(source)`*. That is a **published synchronization contract carried in prose**, and it is
honoured across the framework. Verified count of `lock(source)`-style sites in `src`:

| File | Sites |
|---|---:|
| `AsyncCustomNodeManager.cs` | 8 |
| `BaseVariableState.cs` | 6 |
| `NodeState.cs` | 4 |
| `CustomNodeManager.cs` | 1 |
| **Total** | **19** |

Why this matters more than its size suggests:

- It is a **weak-identity lock on a public object**, so any consumer holding a `NodeState` can
  contend with the framework's critical sections — the same defect class as
  `Subscription.DiagnosticsLock => Diagnostics`.
- It **cannot cross a replica**, so it is a single-node assumption baked into the node type
  itself. Any HA design must remove it.
- `r2-nodestate` establishes it as a **prerequisite, not a consequence**: if new-style nodes
  drop `lock(this)`, framework code doing `lock(source)` on a node it did not author breaks.
  The 19 sites must be cut **before** any new node representation exists.

It is bounded, mechanical, and independent of which delivery option is chosen. Like the
diagnostics lock, it is worth doing regardless.

### Adjudicated disagreement: is NodeState client-coupled?

The two designers made **contradictory factual claims**, and the answer changes the fork's
viability. I verified it independently rather than take either on trust.

> **`r2-fork`:** *"Delete a hypothetical forked `NodeState` → complexity reappears massively:
> `NodeStateSerializer`, the client-side `INodeCache`, and every companion spec bind to
> `NodeState`, which lives in shared `Opc.Ua.Types`. A parallel node type would fork the type
> system itself."*

> **`r2-nodestate`:** *"`INodeCache` returns `Node`/`INode` … Client complex types use zero
> `NodeState`. `NodeState` leaks into the client only through the cold
> `CoreClientUtils.NodeSetExport.cs` path. **The re-cut is a server-side concern; the client
> does not constrain it.**"*

**Verification:**

- `src/Opc.Ua.Client/NodeCache/INodeCache.cs` — `FetchNodeAsync` returns `ValueTask<Node?>`
  and `FetchNodesAsync` returns `ValueTask<ArrayOf<Node?>>`. **`INodeCache` does not expose
  `NodeState` at all.** It is backed by the separate lightweight `Node`/`NodeTable`/
  `TypeTable` model in `Opc.Ua.Types/Nodes`.
- `NodeState` appears in the whole of `src/Opc.Ua.Client` in **exactly one file** —
  `CoreClientUtils.NodeSetExport.cs` (19 references), the cold NodeSet-export path.

**`r2-nodestate` is correct; `r2-fork`'s deletion-test verdict #4 rests on a false premise.**

This has a consequence `r2-fork` did not get to draw: because `NodeState` is effectively
server-only, the objection "forking it forks the type system" is much weaker than stated —
and, as `r2-nodestate` notes, **a data-oriented, NodeId-keyed node model already exists and
ships**: it is the one the client uses. A server-side re-cut can promote that model rather
than invent one.

The rest of `r2-fork`'s analysis — including its central finding about the fork line — is
unaffected and was independently verified (see below).

### What all three designers converged on

| Convergent conclusion | `r2-rewrite` | `r2-fork` | `r2-nodestate` |
|---|---|---|---|
| Do **not** de-inherit `NodeState` wholesale | defer it entirely | forking it fails the deletion test | inheritance is ~70% justified; re-cut the other 30% |
| The **data/behaviour split** is the right `NodeState` change | adopt as an internal seam | — | the core recommendation |
| `IStandardServer` stays as-is | unchanged | shared, untouched | — |
| The sync `INodeManager`/`2`/`3` chain can be deleted outright | delete | delete | — |
| Everything is already normalised to `IAsyncNodeManager` before dispatch | yes | yes — this is the fork line | — |

Round 1's independent convergence (a 6-9 member `IServerContext`, the `object` handle
removed, the `Processed` protocol eliminated, `IHistorianProvider` as the pattern) survives
round 2 unchallenged. **No designer in either round has argued against those four.**

Two further findings, each verified, that no round-1 designer had:

1. **The fork line falls at `IAsyncNodeManager`, and the runtime is shared.**
   `r2-fork` verified that `StandardServer.CreateMasterNodeManagerAsync` already builds both a
   sync and an async node-manager list and hands both to one `MasterNodeManager`, which
   normalises everything through the 812-line `AsyncNodeManagerAdapter`;
   `NodeManagerRoutingTable` is already `IReadOnlyList<IAsyncNodeManager>`. **Old and new
   node managers can therefore coexist in one address space with zero runtime changes**, and a
   "fork" duplicates only the ~15,000-line authoring surface (~10%), sharing the
   ~130,000-line runtime.

2. **The `[OpcUaShim]` pattern is structurally unavailable for the authoring surface.**
   `r2-rewrite` verified that every existing shim is an extension method or thin
   re-implementation *over the new API* — e.g.
   `ServerBaseObsolete.Start(this IServerBase) => StartAsync(...).GetAwaiter().GetResult()`.
   That works when the old API is a veneer over the new one. A node-manager author instead
   **inherits** an 8,028-line base and overrides virtuals whose bodies call
   `MasterNodeManager`/`NodeState`/`IServerInternal` internals. Shimming it requires shipping
   the old base classes *with their implementation* — *"which is precisely Option B relocated
   into a NuGet package."* **A base-class inheritance surface cannot be shimmed without
   retaining its implementation.**

Finding 2 is the reason the honest auto-fix estimate for the authoring slice is ~10%, not the
shipped 70%: the existing analyzers match `OperationKind.Invocation` — *"you called removed
symbol X"* — whereas this migration is *"your override body must be reshaped"*, which is not
an invocation pattern. The single precedent, `docs/migrate/2.0.x/node-states.md`, was
migrated **entirely by hand** with explicit *"⚠ Silent regression … No runtime exception is
thrown"* warnings.

---

## Comparison

| Axis | Option A (rewrite) | Option B (hard fork) |
|---|---|---|
| Implementations at the end | **1** | **2** (authoring layer only) |
| Duplicated code | none | ~15,000 lines (~10% of the assembly) |
| Shared runtime | n/a | ~130,000 lines (~90%) — publish pipeline, CTT semantics single-sourced |
| New bridge adapter | not needed | **required**, ~800 lines |
| Coexistence in one address space | n/a | **yes, zero runtime changes** |
| `object` handle / `Processed` defects | **deleted** | **inherited** by the bridge |
| Consumer migration | forced, ~55-60% manual | opt-in; 30+ subclasses keep compiling |
| Analyzer work | ~6 new rules, 2 partial fixes, no shim possible | 0-2 optional nudge rules |
| Test rewrite | **~19,720 lines** across 5 files, on the critical path | old suites stay |
| Coverage rule (must not decrease) | **hard stop** — new tests must land in the same train | not triggered |
| CTT | one engine, but the whole surface is new | ~1.3× (bridge + new runtime need a gate) |
| Trimming | improves | **strictly worse** — old bases un-trimmable |
| In-flight PRs | irreconcilable conflicts; needs a freeze | unaffected |
| Elapsed | multi-quarter, indivisible | incremental |

**The decisive asymmetry is not cost — it is who pays.** Option A concentrates a large,
schedulable, mostly-internal cost and eliminates a permanent recurring one. Option B avoids
the concentrated cost and accepts a permanent one, *conditional on an exit that step 2 of its
own analysis shows is most of a rewrite anyway*.

Because `r2-fork` established that a fork duplicates only the authoring layer, several of the
scariest fork costs do not materialise: bug fixes to the runtime are written once, and CTT is
mostly shared. Conversely, because `r2-rewrite` established that the shim is unavailable,
the rewrite's external-consumer cost is worse than the tooling premise implied.

**Both designers, independently, declined to recommend their own assignment as-is:**

- `r2-fork`: *"absent a hard requirement for a second node-serving engine, adopt the soft
  fork … That captures the entire measured win with one implementation to test, document,
  generate for, and trim."*
- `r2-rewrite`: *"if the near-term driver is the runtime debt, not the authoring model … all
  of these are fixable without deleting the node-manager surface at all. If that is the actual
  goal, a full rewrite is massive over-reach for it."*

---

## Recommendation — staged convergence

**Take neither pure option. Reach Option A's end state by Option B's path, with an explicit
gate that decides whether you finish.**

The two options are usually framed as alternatives. The evidence says they are the same
programme at different points in time. Three measured facts force this reading:

1. **Coexistence is nearly free** — the fork line is already there at `IAsyncNodeManager`,
   and the runtime already multiplexes heterogeneous node managers.
2. **The old base cannot freeze on its own** — `CoreNodeManager`, `DiagnosticsNodeManager`,
   `ConfigurationNodeManager`, `AliasNameNodeManager` and `FileSystemNodeManager` all derive
   from `AsyncCustomNodeManager`; **the server cannot start without them**. Migrating them is
   simultaneously the fork's exit condition *and* the bulk of the rewrite.
3. **The largest wins are not in either option** — the two lock leaks, the service locator,
   and the publish-protocol leak are all fixable in the shared runtime without deleting
   anything.

### The sequence

| # | Step | Scope | Gate |
|---|---|---|---|
| **0** | Remove the `lock(source)` contract | 19 sites, verified | Prerequisite for *any* new node representation |
| **1** | Owner-side diagnostics updates | 88 `lock` statements, 5 leaked members (#4183) | Independent; worth doing regardless |
| **2** | `IServerContext` + frozen bind phase | deletes 12 `Set*` mutators; 268 referencing files | Independent; worth doing regardless |
| **3** | Soft fork — `INodeSource` ladder as an `internal sealed` facade **over the shared engine** | new authoring surface, no second engine, no bridge | Delivers ~90% of the ergonomic win |
| **4** | Data/behaviour split inside the shared engine | `NodeData` + `INodeBehavior`; extract the ~1,500-line serialisation bucket | The HA enabler |
| **5** | Migrate the framework-internal node managers to the new surface | `CoreNodeManager`, `DiagnosticsNodeManager`, `ConfigurationNodeManager`, `AliasNameNodeManager`, `FileSystemNodeManager` | **THE GATE** |
| **6** | Delete `AsyncCustomNodeManager` / `CustomNodeManager2` / the old bases | — | Only reachable if 5 completed |

Steps 0-2 are pure debt paydown with no strategic commitment. Step 3 is the soft fork —
one engine, layered entry points, `internal sealed` so tests cannot fall through. Step 4 is
where HA correctness is won. **Step 5 is the decision point.**

### The gate that decides everything

Step 5 is the honest test of whether a rewrite is achievable, because it is the rewrite in
miniature: five real node managers, in-repo, fully controlled, with the complete test suite
available. If migrating them is tractable, the remaining consumers are tractable and step 6
follows. If it is not, you have learned that at the cost of five node managers rather than a
multi-quarter programme — and you stop at step 4 with a supported soft fork, which is a
legitimate end state rather than a failure.

This is what neither pure option offers: **a cheap, early, falsifiable test of the rewrite
hypothesis.** Option A only discovers the answer after committing; Option B never asks the
question.

### When to switch to a pure option

Take **Option A wholesale** if all five of `r2-rewrite`'s conditions hold — a decade-long
maintenance horizon with HA committed, a small or coordinated external node-manager
population, a release train that can freeze `MasterNodeManager`/`IServerInternal`/
`Subscription`, funding for the ~19,720-line test rewrite inside the same train, and the
`NodeState` re-cut kept out of scope. Note the last one: *both* the rewrite designer and the
`NodeState` designer independently insist the two programmes must not be combined.

Take **Option B's hard fork** only on `r2-fork`'s single surviving justification — a second,
structurally-incompatible node-serving engine for a genuinely remote address space, where the
shared runtime's synchronous `TryGetNode` fast path is *wrong* rather than inconvenient. Its
§1.4 bridge is exactly how to bolt that on later without a flag day, which is another reason
not to fork now.

### Honest caveats

- **Nothing here is implemented or benchmarked.** Every performance figure in all three
  proposals — including the ~400 bytes/node saving from dropping ~50 delegate slots — is a
  design estimate. Round 1's caveat stands.
- **Step 5 may fail, and the plan must survive that.** The recommendation is structured so
  failure at the gate leaves a coherent, supported architecture rather than a half-finished
  rewrite. If that property is not preserved in execution, the staged path loses its main
  advantage over Option A.
- **Steps 3-6 are still a large programme.** Staging reduces risk and makes it schedulable;
  it does not make it cheap. The un-shimmable external migration is deferred by staging, not
  avoided — it arrives in full at step 6.
- **Two designers' cost estimates are not independent of their assignments**, despite the
  instruction to price honestly. Both landed on "not my option as assigned", which is
  evidence of good faith, but the specific effort figures should be treated as informed
  guesses rather than estimates.
- **`docs/migrate/2.0.x/node-states.md` is the only precedent**, and it shows this surface has
  been broken before with compiler-guided breaks, recompilation and manual guidance — no
  analyzer, no shim. That is both encouraging (it has been survived) and sobering (it is the
  ceiling of what tooling will do here).
