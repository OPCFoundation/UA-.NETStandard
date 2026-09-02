# Node Manager Seam Analysis

> **Status: design analysis / proposal — not shipped API.**
> This document records an evidence-based assessment of the node-manager authoring
> interface in `Opc.Ua.Server`. It describes what the surface looks like *today* and
> proposes a narrower shape. Nothing here has been implemented. For the current,
> shipped node-manager documentation see [NodeManagers.md](../docs/NodeManagers.md).

Analysis performed against commit `e73e71184` on `master`.

> **2.0 transition note (September 2026):** generated application authoring now
> uses the source-only `[NodeSource]` interface documented in
> `docs/NodeManagers.md`. The legacy `[NodeManager]` generator remains only for
> specialized in-repository managers, notably `ReferenceNodeManager`, whose
> node-management, custom NodeId, history, and sampling capabilities cannot yet
> be represented by the small source interface without recreating the broad
> virtual seam. Keep that compatibility path out of public guidance and delete
> it before the final 2.0 release once those capabilities have narrower modules.

## Table of contents

- [Purpose](#purpose)
- [Vocabulary](#vocabulary)
- [Method](#method)
- [Finding 1: the assembly is bimodal](#finding-1-the-assembly-is-bimodal)
- [Finding 2: the node-manager seam is the outlier](#finding-2-the-node-manager-seam-is-the-outlier)
  - [Interface inventory](#interface-inventory)
  - [Base-class surface](#base-class-surface)
  - [What callers actually use](#what-callers-actually-use)
  - [Interface complexity outside the type signature](#interface-complexity-outside-the-type-signature)
- [Finding 3: the interface seam and the real seam are in different places](#finding-3-the-interface-seam-and-the-real-seam-are-in-different-places)
- [The deletion test](#the-deletion-test)
- [Full classification of the 93 virtual members](#full-classification-of-the-93-virtual-members)
  - [K — Keep at seam (7)](#k--keep-at-seam-7)
  - [O — Observer hooks (9)](#o--observer-hooks-9)
  - [C — Capability interface (26)](#c--capability-interface-26)
  - [D — Convert to dependency injection (2)](#d--convert-to-dependency-injection-2)
  - [H — Hide (36)](#h--hide-36)
- [Dependency category and testing strategy](#dependency-category-and-testing-strategy)
- [Proposed target shape](#proposed-target-shape)
- [Constraints and risks](#constraints-and-risks)
- [Open questions](#open-questions)
- [Reproducing the evidence](#reproducing-the-evidence)

## Purpose

`Opc.Ua.Server` is roughly 145,000 lines. Most of it is organised into modules with
small interfaces and large implementations. The node-manager authoring surface is not:
it presents 93 extension points to buy roughly 5 that callers use.

This document quantifies that gap so the decision to re-cut the seam can be made on
evidence rather than impression. It deliberately does **not** propose moving
implementation code. The implementation is good and earns its keep; only the
interface is mis-shaped.

## Vocabulary

The analysis uses deep-module design terms consistently:

| Term | Meaning here |
|---|---|
| **Module** | Anything with an interface and an implementation — a class, an interface, a package. |
| **Interface** | Everything a caller must know to use a module correctly: the type signature *plus* invariants, ordering constraints, error modes, and required cooperation protocols. |
| **Implementation** | The body of code inside the module. |
| **Depth** | Leverage at the interface — how much behaviour a caller gets per unit of interface they must learn. |
| **Seam** | The place where a module's interface lives; where behaviour can be altered without editing in that place. |
| **Adapter** | A concrete thing satisfying an interface at a seam. |

Two rules are applied throughout:

- **The deletion test.** Imagine deleting the module. If complexity vanishes, it was a
  pass-through. If complexity reappears across N callers, it was earning its keep.
- **One adapter means a hypothetical seam. Two adapters means a real one.**

## Method

Evidence was gathered mechanically over `src`, `samples`, and `tests`, excluding
`obj` and `bin`:

1. Every `public`/`protected` `virtual`/`abstract` declaration in
   `AsyncCustomNodeManager.cs` was enumerated.
2. Every `override` site in any class deriving from `AsyncCustomNodeManager`,
   `FluentNodeManagerBase`, or `CoreNodeManager` was counted and attributed to one of
   four consumer categories. The scan is textual, so its output is reviewed for
   synchronous-twin members and regex false positives before use.
3. Each virtual member was cross-referenced against the interface declarations in
   `INodeManager.cs` to separate *interface implementations* from *pure extension hooks*.

Consumer categories:

| Category | Meaning |
|---|---|
| `core` | Inside `src/Opc.Ua.Server` — framework-internal, an internal seam |
| `sib` | Other `src` libraries (GDS, PubSub, WotCon, XRegistry, Positioning) — first-party callers |
| `sample` | `samples/` — representative of external authors |
| `test` | `tests/` — may indicate testing past the interface |

Commands are listed under [Reproducing the evidence](#reproducing-the-evidence).

## Finding 1: the assembly is bimodal

`Opc.Ua.Server` already contains several genuinely deep modules. These are the
reference shape and should be left alone:

| Module | Interface size | Behind the seam |
|---|---|---|
| `IHistorianProvider` | **2 members** + narrow capability interfaces | ~5,900 lines; `HistorianDispatcher` (2,370 lines) routes |
| `ILocalAddressSpace` | 7 members, invariants documented in XML docs | node graph; two real adapters (`PredefinedNodes`, dictionary-backed test double) |
| `IMonitoredItemManager` | 7 members | sampling groups, queues, queue handlers |
| `IFileSystemProvider` | 11 members | 4,145 lines |

`IHistorianProvider` is worth reading as the model: an umbrella of two members
(`IsHistorizingAsync`, `GetCapabilitiesAsync`) with providers opting into Part 11
features through narrow capability interfaces (`IHistorianDataProvider`,
`IHistorianAtTimeProvider`, `IHistorianEventProvider`, and so on). See
[HistoricalAccess.md](../docs/HistoricalAccess.md).

## Finding 2: the node-manager seam is the outlier

### Interface inventory

`NodeManager/INodeManager.cs` is 1,158 lines and declares **22 public interfaces**:

| Interface | Members | Notes |
|---|---|---|
| `INodeManager` | 23 | Mandatory core |
| `INodeManager2` | +3 | Version-suffixed extension |
| `INodeManager3` | +4 | Version-suffixed extension |
| `IAsyncNodeManager` | ~20 | Parallel async family |
| `INodeManagerFactory` / `IAsyncNodeManagerFactory` | 2 each | Construction |
| 15 × `I*AsyncNodeManager` | 1–5 each | Opt-in capability interfaces — **the correct pattern** |

The seam has been widened twice by version suffix (`2`, `3`) rather than re-cut.

### Base-class surface

| Type | Lines | public | protected | virtual/abstract |
|---|---|---|---|---|
| `AsyncCustomNodeManager` | 8,028 | 59 | 92 | **93** |
| `CustomNodeManager2` | 6,302 | 52 | 86 | 93 |
| `MasterNodeManager` | 7,601 | 86 | 21 | 39 |

The 93 virtual/abstract declarations resolve to **80 distinct names** (the remainder
are overloads). That figure — 93 extension points — is the effective interface
presented to the 33 classes deriving from these bases.

### What callers actually use

Across `src`, `samples`, and `tests` there are **112 override sites**. The raw scan
reports 30 distinct names; five are discounted — `LoadPredefinedNodes`,
`OnSubscribeToEvents`, and `OnNodeRemoved` are the synchronous twins declared on
`CustomNodeManager2` rather than `AsyncCustomNodeManager`, and `if` plus
`CreateSessionServerSignature` are regex false positives. That leaves **25 distinct
names** of the 80. **55 of the 80 names are never overridden anywhere.**

Full override census, all consumer categories:

| Member | Total | core | sib | sample | test |
|---|---:|---:|---:|---:|---:|
| `CreateAddressSpaceAsync` | 21 | 5 | 9 | 5 | 2 |
| `Dispose` | 14 | 4 | 5 | 4 | 1 |
| `New` | 12 | 3 | 5 | 3 | 1 |
| `LoadPredefinedNodesAsync` | 11 | 1 | 8 | 1 | 1 |
| `DeleteAddressSpaceAsync` | 8 | 2 | 4 | 1 | 1 |
| `AddBehaviourToPredefinedNodeAsync` | 5 | 1 | 3 | 1 | 0 |
| `OnMonitoredItemCreated` | 4 | 2 | 1 | 1 | 0 |
| `OnMonitoredItemDeletedAsync` | 3 | 1 | 1 | 1 | 0 |
| `GetHistorianProvider` | 3 | 0 | 0 | 2 | 1 |
| `GetManagerHandleAsync` | 3 | 1 | 1 | 1 | 0 |
| `ValidateNodeAsync` | 3 | 1 | 1 | 1 | 0 |
| `OnMonitoringModeChangedAsync` | 3 | 1 | 1 | 1 | 0 |
| `OnSubscribeToEventsAsync` | 2 | 1 | 0 | 0 | 1 |
| `ConditionRefreshAsync` | 2 | 0 | 0 | 1 | 1 |
| `OnMonitoredItemModifiedAsync` | 2 | 0 | 1 | 1 | 0 |
| `OnNodeRemovedAsync` | 2 | 1 | 0 | 0 | 1 |
| `SubscribeToAllEventsAsync` | 1 | 0 | 0 | 0 | 1 |
| `SessionClosingAsync` | 1 | 1 | 0 | 0 | 0 |
| `SessionActivatedAsync` | 1 | 0 | 0 | 0 | 1 |
| `ReadAsync` | 1 | 0 | 0 | 0 | 1 |
| `AddPredefinedNodeAsync` | 1 | 1 | 0 | 0 | 0 |
| `AddReferencesAsync` | 1 | 1 | 0 | 0 | 0 |
| `DeleteReferenceAsync` | 1 | 1 | 0 | 0 | 0 |
| `CallAsync` | 1 | 0 | 0 | 1 | 0 |
| `ValidateViewDescription` | 1 | 1 | 0 | 0 | 0 |

Representative consumers:

| Consumer | Lines | Overrides |
|---|---:|---:|
| `ReferenceNodeManager` (sample) | 6,373 | 8 |
| `FluentNodeManagerBase` | 280 | 4 |

Six members account for the overwhelming majority of all override sites:
`CreateAddressSpaceAsync`, `Dispose`, `New`, `LoadPredefinedNodesAsync`,
`DeleteAddressSpaceAsync`, `AddBehaviourToPredefinedNodeAsync`.

This is the depth deficit. The *implementation* is genuinely deep — roughly 22,000
lines of browse, read, write, call, history, and monitored-item machinery across the
three base classes. The *interface* charges an author 93 extension points to buy the
handful they need. Leverage per unit of interface learned is poor.

### Interface complexity outside the type signature

Under the definition used here, the interface includes every fact a caller must know.
Several load-bearing contracts on this seam are enforced only by prose:

- **Opaque handles.** `object? GetManagerHandle(NodeId)` returns an untyped handle the
  caller must round-trip correctly. This also conflicts with the repository rule
  against `object` in public API.
- **The `Processed` flag protocol.** *"The node manager must ignore ReadValueId with the
  Processed flag set to true. The node manager must set the Processed flag for any
  ReadValueId that it processes."* This cooperative-multiplexing contract is restated
  six times in `INodeManager.cs` and is not expressible in the type system.
- **Index-aligned accumulators.** `Read`, `Write`, `Call`, `HistoryRead` and friends take
  parallel `IList<T>` value and error collections that the caller pre-sizes and the
  implementer must index-align.
- **Pre-populated outputs.** `Browse` takes `ref ContinuationPoint` and documents that
  *"the references parameter may already contain references when the method is called"*,
  which the implementer must account for when deciding whether to return a
  continuation point.

## Finding 3: the interface seam and the real seam are in different places

- Across the entire repository there is exactly **one** direct implementation of
  `INodeManager` outside the framework: `SampleNodeManager` in
  `samples/Quickstarts.Servers`. By the one-adapter rule, the `INodeManager` seam is
  hypothetical. All 33 real node managers derive from the base classes instead.
- `IAsyncNodeManager` exposes `SyncNodeManager : INodeManager` — the async interface
  publishes the sync adapter through itself, letting callers cross back over the seam.
- The two bridging adapters (`AsyncNodeManagerAdapter`, 915 lines;
  `SyncNodeManagerAdapter`) span a *technology* duality (sync versus async), not a
  domain one.
- Internally, `NodeManagerRoutingTable` is `IReadOnlyList<IAsyncNodeManager>`. Everything
  is already normalised to the async interface before dispatch.

## The deletion test

| Module | Delete it | Verdict |
|---|---|---|
| `AsyncCustomNodeManager` | Complexity reappears in all 33 subclasses | **Earns its keep.** The implementation is not the problem. |
| `INodeManager` / `2` / `3` | Little reappears — routing already runs on `IAsyncNodeManager` | Compatibility surface, not a load-bearing seam. |
| The 11 `History*Async` virtuals | Nothing reappears — they are handle loops delegating into `HistorianDispatcher.Dispatch*Async` | **Pure pass-through.** |
| `MasterNodeManager` | Complexity reappears, but much of it is *caused by* the seam below it (fan-out, `Processed` multiplexing, handle resolution) | Shrinks if the seam narrows; do not attack directly. |

## Full classification of the 93 virtual members

The 80 distinct names partition exactly into five buckets, verified with no gaps,
duplicates, or phantoms.

| Bucket | Count | Action |
|---|---:|---|
| K — Keep at seam | 7 | The authoring interface |
| O — Observer hooks | 9 | Collapse into one opt-in interface |
| C — Capability interface | 26 | Remove `virtual`; keep the existing opt-in interfaces |
| D — Convert to DI | 2 | Replace inheritance-based provider selection with injection |
| H — Hide | 36 | Make private or sealed |
| **Total** | **80** | |

### K — Keep at seam (7)

The members that actually carry authoring weight.

| Member | Overrides | Note |
|---|---:|---|
| `CreateAddressSpaceAsync` | 21 | Primary authoring entry point |
| `Dispose` | 14 | `IDisposable`; comes free, not part of the node-manager seam |
| `New` | 12 | NodeId factory (`INodeIdFactory`) |
| `LoadPredefinedNodesAsync` | 11 | NodeSet loading |
| `DeleteAddressSpaceAsync` | 8 | Teardown |
| `AddBehaviourToPredefinedNodeAsync` | 5 | Attach behaviour to loaded nodes |
| `NamespaceUris` | 0 | Set through the constructor; should be non-virtual |

Discounting `Dispose` (inherited from `IDisposable`) and `NamespaceUris` (constructor
state), **the genuine authoring seam is 5 members.**

### O — Observer hooks (9)

Every member here answers "tell me when X happened". Nine separate virtuals buy one
concept.

| Member | Overrides |
|---|---:|
| `OnMonitoredItemCreated` | 4 |
| `OnMonitoredItemDeletedAsync` | 3 |
| `OnMonitoringModeChangedAsync` | 3 |
| `ValidateNodeAsync` | 3 |
| `OnMonitoredItemModifiedAsync` | 2 |
| `OnSubscribeToEventsAsync` | 2 |
| `OnNodeRemovedAsync` | 2 |
| `SessionClosingAsync` | 1 |
| `SessionActivatedAsync` | 1 |

Proposal: one opt-in `INodeManagerObserver`, resolved through DI, keeping the mandatory
seam at 5 members.

### C — Capability interface (26)

Every member in this bucket is **already** declared on an `I*AsyncNodeManager`
capability interface. The base class then re-exposes each one as `virtual`, so an
author meets the same decision twice — once as "implement this interface", once as
"override this method". Only 7 of the 26 are ever overridden, mostly once.

`ReadAsync` · `WriteAsync` · `CallAsync` · `TranslateBrowsePathAsync` ·
`HistoryReadAsync` · `HistoryUpdateAsync` · `ConditionRefreshAsync` ·
`SetMonitoringModeAsync` · `CreateMonitoredItemsAsync` · `ModifyMonitoredItemsAsync` ·
`DeleteMonitoredItemsAsync` · `TransferMonitoredItemsAsync` ·
`RestoreMonitoredItemsAsync` · `AddNodeAsync` · `DeleteNodeAsync` ·
`AddReferenceAsync` · `AddReferencesAsync` · `DeleteReferenceAsync` ·
`AllowNodeManagement` · `GetManagerHandleAsync` · `SubscribeToEventsAsync` ·
`SubscribeToAllEventsAsync` · `ValidateRolePermissionsAsync` · `IsNodeInView` ·
`IsNodeInViewAsync` · `FindMethodStateAsync`

Proposal: drop the `virtual` modifier. The capability interfaces already provide the
opt-in route and are documented in [NodeManagement.md](../docs/NodeManagement.md) and
[AsyncServerSupport.md](../docs/AsyncServerSupport.md).

### D — Convert to dependency injection (2)

| Member | Overrides | Note |
|---|---:|---|
| `GetHistorianProvider` | 3 (2 sample, 1 test) | Returns `null` by default; composed as `HistorianDispatcher.ResolveProvider(Server, node, GetHistorianProvider(node))` |
| `HasHistorianProvider` | 0 | Derived from the above |

This is provider selection by inheritance. Samples override it because there is no
injection point at that layer. The repository convention is a provider model with
injectable providers, so this should become an injected resolver.

### H — Hide (36)

34 of these have zero overrides. Two (`AddPredefinedNodeAsync`,
`ValidateViewDescription`) are overridden only inside `Opc.Ua.Server` itself — internal
seams that should not surface at the authoring interface.

**History fan-out (11)** — verified to be handle loops delegating straight into
`HistorianDispatcher.Dispatch*Async`, with the real behaviour behind the 2-member
`IHistorianProvider`. Roughly 1,000 lines of pass-through published as 11 extension
points that nobody uses.

`HistoryReadRawModifiedAsync` · `HistoryReadAtTimeAsync` · `HistoryReadProcessedAsync` ·
`HistoryReadEventsAsync` · `HistoryUpdateDataAsync` · `HistoryUpdateEventsAsync` ·
`HistoryUpdateStructureDataAsync` · `HistoryDeleteRawModifiedAsync` ·
`HistoryDeleteAtTimeAsync` · `HistoryDeleteEventsAsync` ·
`HistoryReleaseContinuationPointsAsync`

**Monitored-item internals (13)** — `IMonitoredItemManager` (7 members) is already the
correct seam for these.

`CreateMonitoredItemAsync` · `ModifyMonitoredItemAsync` · `DeleteMonitoredItemAsync` ·
`RestoreMonitoredItem` · `ReadInitialValue` · `ValidateMonitoringFilterAsync` ·
`ReviseAggregateFilterAsync` · `ValidationComplete` · `OnCreateMonitoredItemsComplete` ·
`OnDeleteMonitoredItemsCompleteAsync` · `OnModifyMonitoredItemsCompleteAsync` ·
`OnSetMonitoringModeCompleteAsync` · `OnMonitoredItemsTransferredAsync`

Note the smell inside this group: the **batch** `On*Complete` family has zero overrides
while the **singular** `On*Created` / `On*Deleted` / `On*Modified` family (bucket O) has
nine. Two parallel hook families exist; only one is used.

**Namespace and view predicates (5)**

`IsHandleInNamespace` · `IsNodeIdInNamespace` · `IsReferenceInView` ·
`FindNodeInCacheAsync` · `ValidateViewDescription`

**Node-graph internals (7)**

`AddPredefinedNodeAsync` · `RemovePredefinedNodeAsync` · `AddReverseReferencesAsync` ·
`AddRootNotifierAsync` · `RemoveRootNotifierAsync` · `OnReportEvent` ·
`OnReportEventAsync`

## Dependency category and testing strategy

Classifying the dependencies determines how much work re-cutting the seam actually is.

| Dependency | Category | Consequence |
|---|---|---|
| Node graph (`NodeState`, in-memory dictionaries) | **In-process** | Deepenable directly; no port needed |
| Historian | Already behind `IHistorianProvider` | No new seam required |
| File system | Already behind `IFileSystemProvider` | No new seam required |
| Distributed address space | Already behind `ILocalAddressSpace` | No new seam required |

Because the core is in-process and every external dependency already sits behind its
own provider seam, **no new port is required**. The deepened module is tested directly
through its narrowed interface. This is the cheap case.

Testing should **replace, not layer**. Tests that drive 93 virtuals by subclassing are
testing past the interface and will not survive the change:

| Test file | Lines |
|---|---:|
| `NodeManagerLifecycleTests.cs` | 8,406 |
| `AsyncCustomNodeManagerTests.cs` | 5,821 |
| `MasterNodeManagerNodeManagementTests.cs` | 2,499 |
| `MasterNodeManagerDeterministicTests.cs` | 1,639 |
| `CustomNodeManagerDeterministicTests.cs` | 1,355 |

New tests should assert observable outcomes through the narrow interface so they
survive internal refactors.

## Proposed target shape

Nothing moves. The 8,028 lines of `AsyncCustomNodeManager` stay exactly where they are.
Only the interface is re-cut:

- **5 mandatory members** for authoring a node manager.
- **1 opt-in `INodeManagerObserver`** replacing the 9 scattered notification hooks.
- **The 15 existing `I*AsyncNodeManager` capability interfaces** remain the opt-in route
  for service-set behaviour, with the duplicate base-class virtuals removed.
- **2 provider hooks** converted to injection.
- **36 members** made private or sealed.

Net: **93 extension points → 5 mandatory members**, with the same behaviour behind them.

The strongest argument for the shape is that this assembly already contains the answer
twice. `IHistorianProvider` (2 members over ~5,900 lines) and `IMonitoredItemManager`
(7 members) demonstrate the target pattern, and they sit directly alongside the 24
virtuals that duplicate them.

## Constraints and risks

- **Backward compatibility.** The repository requires compatibility with 1.5.378
  (`master378`); replaced API must be marked `[Obsolete]` rather than removed. This makes
  the change additive. `INodeManager.cs` currently contains **zero** `[Obsolete]` markers,
  so the deprecation pass has not begun. The existing `SyncNodeManagerAdapter` and
  `AsyncNodeManagerAdapter` are the natural compatibility mechanism.
- **`FluentNodeManagerBase` layers rather than replaces.** It derives from
  `AsyncCustomNodeManager` and therefore inherits all 93 members, adding
  `EventSourceRegistry` and `SimulationRegistry` on top. It would need re-basing onto the
  narrow seam rather than stacking on the wide one.
- **Source generators emit against this surface.** The generated `NodeManagerBase` derives
  from `FluentNodeManagerBase`; generator templates must move in lockstep. See
  [NodeManagers.md](../docs/NodeManagers.md#source-generated-node-sources).
- **`SampleNodeManager` implements `INodeManager` directly** and is the one external
  adapter for that interface. It either migrates or stays as the demonstration of the
  obsolete path.

## Open questions

1. Should the 5-member authoring seam be an **interface** (`INodeSource`-style, favouring
   composition) or remain an **abstract base class** (favouring the existing 33
   subclasses)? This is the main design decision and is worth exploring more than once.
2. Should `GetManagerHandle`'s opaque `object` handle be replaced with a typed handle
   struct, and can that be done without breaking the `Processed`-flag multiplexing that
   `MasterNodeManager` depends on?
3. Can the `Processed` cooperative-multiplexing protocol be replaced by explicit
   partitioning in `MasterNodeManager`, removing the contract from the seam entirely?
4. Does the sync/async duality still need two adapters, or can `INodeManager` become a
   pure `[Obsolete]` compatibility shim over `IAsyncNodeManager`?

## Reproducing the evidence

Run from the repository root in PowerShell.

Enumerate the virtual/abstract surface:

```powershell
$lines = Get-Content src\Opc.Ua.Server\NodeManager\AsyncCustomNodeManager.cs
($lines | Select-String '^        (public|protected)(\s+internal)?\s+(virtual|abstract)\s+').Count
```

Count the override census by consumer category:

```powershell
$files = Get-ChildItem 'src','samples','tests' -Recurse -Filter *.cs -File |
    Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\' }
$rows = @()
foreach ($f in $files) {
    $t = Get-Content -LiteralPath $f.FullName -Raw
    if ($t -notmatch ':\s*(AsyncCustomNodeManager|FluentNodeManagerBase|CoreNodeManager)\b') { continue }
    $rel = $f.FullName.Replace("$PWD\", '')
    $cat = if ($rel -like 'src\Opc.Ua.Server\*') { 'core' }
           elseif ($rel -like 'src\*') { 'sib' }
           elseif ($rel -like 'samples\*') { 'sample' }
           else { 'test' }
    foreach ($m in [regex]::Matches($t, '\boverride\s+[\w\<\>\?\[\]\.,\s]{0,80}?(\w+)\s*[\(\{]')) {
        $rows += [PSCustomObject]@{ Name = $m.Groups[1].Value; Cat = $cat }
    }
}
$rows | Group-Object Name | Sort-Object Count -Descending
```

Count direct `INodeManager` implementations outside the framework:

```powershell
Get-ChildItem 'src','samples','tests' -Recurse -Filter *.cs -File |
    Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\' } |
    Select-String -Pattern '(class|record)\s+\w+\s*:[^{]*\bINodeManager\b'
```

## See also

- [Node Managers](../docs/NodeManagers.md) — the current, shipped node-manager guide.
- [Async Server Support](../docs/AsyncServerSupport.md) — the async capability-interface pattern.
- [NodeManagement Service Set](../docs/NodeManagement.md) — `INodeManagementAsyncNodeManager` opt-in.
- [Historical Access (Part 11)](../docs/HistoricalAccess.md) — the `IHistorianProvider` provider model
  used here as the reference shape.
- [Dependency Injection](../docs/DependencyInjection.md) — the injection surface the provider hooks
  would move to.
- [Developer Guide](../docs/DeveloperGuide.md) — coding standards referenced by this analysis.
