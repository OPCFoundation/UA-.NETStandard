# Server Design Exploration — Design It Twice

> **Status: design exploration / proposal — not shipped API. Superseded in part by
> [ServerDesignExploration.md](ServerDesignExploration.md).**
> Four independent alternative designs for `src/Opc.Ua.Server` and its dependencies,
> produced in parallel by four designers each working to a different design constraint,
> followed by a comparison and a recommendation. Nothing here has been implemented.
>
> This is **round 1**, conducted under a hard constraint that was later lifted: backward
> compatibility with 1.5.378, additive changes only, `[Obsolete]` rather than remove. Round 2
> ([ServerDesignExploration.md](ServerDesignExploration.md)) re-runs the exercise without that
> constraint and addresses delivery strategy. Round 1 is retained because its four interface
> designs, and the convergence between them, still stand — round 2 builds on them rather than
> replacing them.
>
> This exploration deliberately includes the `NodeState` and node-manager surfaces,
> which were excluded from [ServerRuntimeAnalysis.md](ServerRuntimeAnalysis.md) as
> plugin API.

Produced against commit `e73e71184` on `master`, 5 Aug 2026.

## Related documents

| Document | Scope |
|---|---|
| [NodeManagerAnalysis.md](NodeManagerAnalysis.md) | Evidence: the node-manager plugin surface (93 virtuals, 112 override sites) |
| [ServerRuntimeAnalysis.md](ServerRuntimeAnalysis.md) | Evidence: the server runtime cluster, node managers excluded |
| [ServerDesignExploration.md](ServerDesignExploration.md) | Round 2: delivery strategy — full rewrite vs side-by-side fork |
| **This document** | Round 1 (alternative): four interface designs under the back-compat constraint |

## Table of contents

- [Why this exercise](#why-this-exercise)
- [Method](#method)
- [The problem space as briefed](#the-problem-space-as-briefed)
  - [Measured surface](#measured-surface)
  - [Interface complexity outside the type signature](#interface-complexity-outside-the-type-signature)
  - [Dependency categories](#dependency-categories)
  - [Hard constraints given to every designer](#hard-constraints-given-to-every-designer)
- [Design 1 — One-Seam Address Space (minimise the interface)](#design-1--one-seam-address-space-minimise-the-interface)
- [Design 2 — Composition-First (maximise flexibility)](#design-2--composition-first-maximise-flexibility)
- [Design 3 — Progressive Ladder (optimise the common caller)](#design-3--progressive-ladder-optimise-the-common-caller)
- [Design 4 — State-Placement (ports and adapters)](#design-4--state-placement-ports-and-adapters)
- [Comparison](#comparison)
  - [Convergence — what all four agreed on](#convergence--what-all-four-agreed-on)
  - [Divergence — the real disagreements](#divergence--the-real-disagreements)
  - [Depth](#depth)
  - [Locality](#locality)
  - [Seam placement](#seam-placement)
  - [Four answers to one defect](#four-answers-to-one-defect)
- [Recommendation](#recommendation)
  - [The hybrid](#the-hybrid)
  - [What to reject and why](#what-to-reject-and-why)
  - [Sequencing](#sequencing)
  - [Honest caveats](#honest-caveats)
- [Appendix — verified facts each designer independently confirmed](#appendix--verified-facts-each-designer-independently-confirmed)

## Why this exercise

Per *Design It Twice* (Ousterhout): your first idea is unlikely to be the best. The two
evidence documents established **what** is wrong with the current interfaces. They did not
establish what should replace them, and the obvious replacement — "narrow the seam to the
6 members people actually override" — is only one point in a large design space.

Four designers were given the same evidence and the same constraints, but different design
constraints, and told to be radical. The value is in the disagreements, and in what they
converged on despite never seeing each other's work.

## Method

Each designer received an identical technical brief containing: the measured surface below,
the deep-module vocabulary (module, interface, implementation, depth, seam, adapter,
leverage, locality), the principles (deletion test, "the interface is the test surface",
one-adapter-means-hypothetical), the OPC UA domain vocabulary, the dependency-category
framework, the hard constraints, and the list of existing seams to reuse rather than
reinvent.

They differed only in one paragraph:

| Designer | Design constraint |
|---|---|
| `design-minimal` | Minimise the interface. 1–3 entry points maximum. Maximise leverage per entry point. |
| `design-flexible` | Maximise flexibility and composability — but through composition of small deep modules, **not** a wide inheritance surface. |
| `design-commoncase` | Optimise for the most common caller. Make the default trivial, then make the progression to advanced smooth and explicit. |
| `design-ports` | Design around ports and adapters, driven by the distributed / high-availability case. Be rigorous about the one-adapter rule. |

All four worked read-only; no repository files were modified. Each produced five sections:
interface, usage example, what the implementation hides, dependency strategy and adapters,
and trade-offs. Their designs are reproduced below **verbatim and in full**.

## The problem space as briefed

### Measured surface

| Module | Interface | Implementation |
|---|---:|---|
| `NodeState` | **155 public, 65 virtual, 18 public delegates**, 2 events | 5,824 lines (`State` folder: 22 files / 15,434 lines) |
| `AsyncCustomNodeManager` | **93 virtual/abstract** (80 distinct names) | 8,028 lines, 59 public + 92 protected |
| `CustomNodeManager2` | 93 virtual/abstract | 6,302 lines |
| `INodeManager.cs` | **22 interfaces in one file** | version chain `INodeManager`→`2`→`3` + async twin + 15 capability interfaces |
| `MasterNodeManager` | 36 public | 7,601 lines |
| `IServerInternal` | **57** (34 properties, 12 `Set*`) | `ServerInternalData` 1,355 lines, 200+ referencing files |
| `ISubscription` | 42 | 3,471 |
| `ISession` | 37 | 1,526 |
| `ISubscriptionManager` | 26 | 2,858 |
| `ISessionManager` | 18 (7 events) | 1,940 |
| `IStandardServer` | **9** | 5,575 — the one healthy seam |

Override census across `src` + `samples` + `tests`: **112 override sites, 25 distinct names
ever overridden, 55 of 80 never overridden anywhere.**

| Member | Sites | core | sibling libs | samples | tests |
|---|---:|---:|---:|---:|---:|
| `CreateAddressSpaceAsync` | 21 | 5 | 9 | 5 | 2 |
| `Dispose` | 14 | 4 | 5 | 4 | 1 |
| `New` (NodeId factory) | 12 | 3 | 5 | 3 | 1 |
| `LoadPredefinedNodesAsync` | 11 | 1 | 8 | 1 | 1 |
| `DeleteAddressSpaceAsync` | 8 | 2 | 4 | 1 | 1 |
| `AddBehaviourToPredefinedNodeAsync` | 5 | 1 | 3 | 1 | 0 |
| `OnMonitoredItemCreated` | 4 | 2 | 1 | 1 | 0 |
| `OnMonitoredItemDeletedAsync` | 3 | 1 | 1 | 1 | 0 |
| `GetHistorianProvider` | 3 | 0 | 0 | 2 | 1 |
| `GetManagerHandleAsync` | 3 | 1 | 1 | 1 | 0 |
| `ValidateNodeAsync` | 3 | 1 | 1 | 1 | 0 |
| `OnMonitoringModeChangedAsync` | 3 | 1 | 1 | 1 | 0 |

then a tail of `OnSubscribeToEventsAsync` (2), `ConditionRefreshAsync` (2),
`OnMonitoredItemModifiedAsync` (2), `OnNodeRemovedAsync` (2), and ten single-use names.

For scale: `ReferenceNodeManager` is a 6,373-line sample that overrides **8**.
`FluentNodeManagerBase` overrides **4**. Exactly **one** class implements `INodeManager`
directly outside the framework.

The 18 `NodeState` delegates, many in sync/async pairs:
`NodeStateChangedHandler` + `NodeStateChangedAsyncHandler`, `NodeStateReportEventHandler` +
`NodeStateReportEventAsyncHandler`, `NodeValueEventHandler` + `NodeValueEventHandlerAsync`,
`NodeValueSimpleEventHandler` + `NodeValueSimpleEventHandlerAsync`,
`NodeValueWriteEventHandlerAsync`, `NodeValueSimpleWriteEventHandlerAsync`,
`NodeStateValidateHandler`, `NodeStateReferenceAdded`, `NodeStateReferenceRemoved`,
`NodeStateConditionRefreshEventHandler`, `NodeStateCreateBrowserEventHandler`,
`NodeStatePopulateBrowserEventHandler`, `NodeAttributeEventHandler<T>`,
`NodeStateConstructDelegate`.

### Interface complexity outside the type signature

Under the definition used throughout — the interface is *everything a caller must know* —
these count, and they are enforced only by prose:

- **`object? GetManagerHandle(NodeId)`** — an opaque untyped handle the caller round-trips.
- **The `Processed` flag protocol** — *"must ignore ReadValueId with Processed set to true;
  must set Processed for any it processes."* Restated **6 times** in `INodeManager.cs`.
  `MasterNodeManager` broadcasts each request to every node manager and relies on each one
  honouring it.
- **Index-aligned parallel accumulators** — `Read(..., IList<DataValue> values,
  IList<ServiceResult> errors)`; the caller pre-sizes, the implementer must index-align.
- **`Browse(..., ref ContinuationPoint, IList<ReferenceDescription>)`** — documented that
  *"references may already contain references when the method is called."*
- **88 `lock` statements** on `object`-typed locks published across `IServerInternal`,
  `ISession` and `ISubscription`, including 2 in a shipped sample. `DiagnosticsWriteLock`'s
  getter calls `ForceDiagnosticsScan()` *outside* the lock it then returns.

### Dependency categories

| Category | What is here | Consequence |
|---|---|---|
| **1 — In-process** | node graph, diagnostics, publish pipeline, request routing | Deepenable directly, no port |
| **2 — Local-substitutable** | `IContinuationPointStore`, `ISubscriptionStore`, `IMonitoredItemQueueFactory` | Internal seam only |
| **3 — Remote but owned** | `ILocalAddressSpace`, HA/redundancy state stores | Ports already exist |
| **4 — True external** | historian, file system — user-supplied | `IHistorianProvider` (2 members) is the exemplar |

**No new port is needed.** Every external dependency already sits behind one. Designers were
told this explicitly and asked to reuse rather than invent.

Existing seams held up as exemplars: `IHistorianProvider` (2 members over ~5,900 lines with
opt-in capability interfaces routed by `HistorianDispatcher`), `ILocalAddressSpace`
(7 members, two real adapters), `IMonitoredItemManager` (7), `IFileSystemProvider` (11).

### Hard constraints given to every designer

1. **1.5.378 backward compatibility** — additive only, `[Obsolete]` rather than remove.
2. **NativeAOT and trimming compatible** — no reflection requiring suppression.
3. **Multi-TFM** — netstandard2.0/2.1, net48, net8.0/9.0/10.0.
4. **DI-injectable** with a direct-construct fallback.
5. **Type rules** — `ArrayOf<T>` over `IReadOnlyList<T>`/`T[]`; `ByteString` over `byte[]`;
   `Span<byte>`/`ReadOnlySpan<byte>`; **never `object` in public API** (use `Variant`);
   `INullable` types never wrapped in `System.Nullable<T>`.
6. **Never expose locks** — `System.Threading.Lock` only, never `object`.
7. **Async-only (TAP)** — no sync-over-async.
8. **Sealed by default**, provider model over inheritance.
9. **Source-generator-emittable.**
10. **Must still support** 33 existing node-manager subclasses, companion specs (DI, GDS,
    PubSub, Robotics, ISA95, WoT), NodeSet2 runtime loading, HA/distributed servers, and the
    full OPC UA service set.

---


## Design 1 — One-Seam Address Space (minimise the interface)

**Design constraint given:** *Minimise the interface. Aim for 1-3 entry points maximum for authoring a server-side address space. Maximise leverage per entry point. Be radical — find out how small this interface can possibly get while still supporting real OPC UA servers.*

> Reproduced verbatim as delivered by the designer. Section numbering is the designer's own.

### The One-Seam Address Space: a minimal authoring interface for OPC UA servers

#### Design thesis

A server author does exactly one thing: **contribute a partition of the address space**. Everything else the current 22 interfaces / 93 virtuals / 155 public members expose — routing, batching, the `Processed`-flag multiplex, continuation-point slicing, index-alignment, the publish pipeline, the service-locator — is framework plumbing that leaked across the seam. This design pushes all of it behind **one contributor interface (`INodeSource`, 2 members)**, **one builder the contributor is handed (`IAddressSpace`)**, and **one DI verb (`AddAddressSpace`)**. Virtual/external address spaces opt into behaviour through tiny capability interfaces inspected by a dispatcher — modelled bit-for-bit on `IHistorianProvider`, the best seam already in the tree.

Radical claim: **95% of authors write a single method body and never name a second framework type. The other 5% implement between 2 and 5 one-line-signature members.** No base class. No factory. No `IServerInternal`. No `NodeState` unless they ask for it.

---

#### 1. Interface

The entire mandatory authoring surface is two members:

```csharp
namespace Opc.Ua.Server.AddressSpace
{
    /// <summary>
    /// The single seam for contributing a partition of the server address space.
    /// A source owns one or more namespaces and is asked, exactly once, to bind
    /// its content.
    ///
    /// INVARIANTS
    ///  * <see cref="Namespaces"/> is stable for the lifetime of the source and
    ///    is read before <see cref="BindAsync"/>; the framework assigns indexes.
    ///  * <see cref="BindAsync"/> is called once per activation, after namespace
    ///    assignment, before the source serves any request. On an HA standby it
    ///    may be called again after a snapshot hydrate, so it MUST be idempotent.
    ///
    /// DEPTH (what the framework owns, not you): MasterNodeManager routing by
    /// namespace, request batching, the Processed-flag multiplex across sources,
    /// continuation-point slicing + persistence, index-range/data-encoding
    /// post-processing, the monitored-item/publish pipeline, subscription
    /// transfer. A source answers about ONE node at a time and never sees a
    /// ContinuationPoint, an index-aligned accumulator, or the Processed flag.
    /// </summary>
    public interface INodeSource
    {
        /// <summary>
        /// Namespace URIs owned by this source. Fixed for the source lifetime.
        /// </summary>
        ArrayOf<string> Namespaces { get; }

        /// <summary>
        /// Publishes this source's content into <paramref name="space"/>: either
        /// materialize nodes (Add*/Import) and/or declare on-demand content
        /// (space.Virtualize(this)). Errors abort server start.
        /// </summary>
        ValueTask BindAsync(IAddressSpace space, CancellationToken ct);
    }
}
```

`IAddressSpace` is the builder the contributor is handed. It is deliberately small — depth lives *below* it (the NodeState engine) and *beside* it (node sources), not *in* it:

```csharp
public interface IAddressSpace
{
    ISystemContext Context { get; }
    ushort NamespaceIndex { get; }                 // index of Namespaces[0]

    /// <summary>Purpose-built services for an address-space author: telemetry,
    /// namespace table, and registration of the base-service providers. This is
    /// NOT IServerInternal — no subsystem handles, no Set* mutators.</summary>
    IAddressSpaceServices Services { get; }

    // ---- Materialize (trivial + companion-spec authors) ----
    /// <summary>Load a NodeSet2 / companion-spec model. The generator emits a
    /// typed overload; the runtime overload takes a NodeSet2 stream. Returns a
    /// binder for wiring behaviour onto model nodes.</summary>
    IModelBinder Import(NodeModel model);

    IFolderHandle    AddFolder(QualifiedName browseName);                 // under Objects
    IFolderHandle    AddFolder(QualifiedName browseName, NodeId parent);  // NodeId.Null == Objects
    IVariableHandle<T> AddVariable<T>(QualifiedName browseName, NodeId parent);
    IMethodHandle    AddMethod(QualifiedName browseName, NodeId parent);

    // ---- Bind behaviour onto an already-present node ----
    INodeHandle        Node(NodeId nodeId);
    IVariableHandle<T> Variable<T>(NodeId nodeId);
    IMethodHandle      Method(NodeId nodeId);

    // ---- Virtualize (external / browse-on-demand authors) ----
    /// <summary>Declares an on-demand content provider. The framework routes
    /// Browse/Read (and opt-in Write/Call/Observe/topology) for this source's
    /// namespaces to <paramref name="provider"/>. Justified by two adapters: the
    /// built-in NodeState-backed source and any external-system source.</summary>
    void Virtualize(IVirtualNodes provider);

    // ---- Reuse existing seams; do not reinvent ----
    void UseHistorian(IHistorianProvider historian);   // Part 11 (existing umbrella seam)
}
```

Node handles carry the whole per-node vocabulary (bind + runtime push). `INullable` domain types are never wrapped in `Nullable<T>`; values are `Variant`/`DataValue`; there is no `object` anywhere:

```csharp
public interface INodeHandle
{
    NodeId NodeId { get; }
    INodeHandle OnEvent(out IEventEmitter emitter);            // push events
}

public interface IVariableHandle<T> : INodeHandle
{
    IVariableHandle<T> OnRead(Func<CancellationToken, ValueTask<T>> read);
    IVariableHandle<T> OnRead(Func<ISystemContext, CancellationToken, ValueTask<T>> read);
    IVariableHandle<T> OnWrite(Func<T, CancellationToken, ValueTask> write);
    IVariableHandle<T> Observe(out IValueUpdater<T> updater);  // reuse existing IValueUpdater<T>
    IVariableHandle<T> Historize();                            // opt this node into Part 11
}

public interface IMethodHandle : INodeHandle
{
    IMethodHandle OnCall(Func<CancellationToken, ValueTask> call);
    IMethodHandle OnCall<TOut>(Func<CancellationToken, ValueTask<TOut>> call);
    IMethodHandle OnCall<TIn, TOut>(Func<TIn, CancellationToken, ValueTask<TOut>> call);
    // further typed arities are source-generator-emitted from the method's Arguments
}
```

The **virtualization capability interfaces** — the only surface a browse-on-demand author touches — are historian-shaped: one mandatory content interface, plus opt-in siblings a dispatcher probes with `is`. Every value is `Variant`/`DataValue`; every collection is `ArrayOf<T>` or a lazy `IAsyncEnumerable<T>`; the browse cursor and Read batching are gone from the signature entirely:

```csharp
/// <summary>Read-only on-demand content. Mandatory when you Virtualize.</summary>
public interface IVirtualNodes
{
    /// <summary>Resolve a NodeId to its attribute metadata, or return
    /// <see cref="NodeSnapshot.Unknown"/> (IsUnknown == true) when not owned.
    /// Must not block on I/O beyond what the token permits; must not throw for
    /// unknown ids.</summary>
    ValueTask<NodeSnapshot> ResolveAsync(ISystemContext context, NodeId nodeId, CancellationToken ct);

    /// <summary>Lazily yield references from a node in a STABLE order. The
    /// framework applies the browse filter, enforces maxReferences, fills target
    /// attributes across sources, and slices the stream into continuation points
    /// persisted via IContinuationPointStore. You never construct or return a
    /// ContinuationPoint; you never see references from another source.</summary>
    IAsyncEnumerable<NodeReference> BrowseAsync(
        ISystemContext context, NodeId nodeId, BrowseFilter filter, CancellationToken ct);

    /// <summary>Read ONE attribute of ONE node. The framework owns batching, the
    /// Processed-flag multiplex, maxAge, index-range and data-encoding.</summary>
    ValueTask<DataValue> ReadAsync(ISystemContext context, ReadTarget target, CancellationToken ct);
}

public interface IWritableNodes            // opt-in
{
    ValueTask<ServiceResult> WriteAsync(
        ISystemContext context, WriteTarget target, DataValue value, CancellationToken ct);
}

public interface ICallableNodes            // opt-in
{
    ValueTask<CallOutcome> CallAsync(
        ISystemContext context, NodeId objectId, NodeId methodId,
        ArrayOf<Variant> inputs, CancellationToken ct);
}

public interface IObservableNodes          // opt-in: live values for monitored items
{
    /// <summary>Push DataValues for a monitored node. The framework owns the
    /// sampling interval, queue depth, deadband, publish pipeline and
    /// subscription transfer. The token is cancelled when the last monitored
    /// item on the node is removed. Return an empty sequence for pull-sampled
    /// nodes (the framework then polls ReadAsync at the sampling interval).</summary>
    IAsyncEnumerable<DataValue> ObserveAsync(
        ISystemContext context, MonitoredNodeRequest request, CancellationToken ct);
}

public interface IMutableTopology          // opt-in: AddNodes/DeleteNodes service set
{
    ValueTask<AddNodeOutcome>  AddNodeAsync(ISystemContext c, NewNode node, CancellationToken ct);
    ValueTask<ServiceResult>   DeleteNodeAsync(ISystemContext c, NodeId nodeId, bool deleteTargetRefs, CancellationToken ct);
    ValueTask<ServiceResult>   EditReferenceAsync(ISystemContext c, ReferenceEdit edit, CancellationToken ct);
}
```

Supporting value types are `readonly struct`s with no `object`, `INullable` sentinels rather than `T?`:

```csharp
public readonly struct NodeSnapshot          // INullable-style: has .IsUnknown + static Unknown
{
    public bool IsUnknown { get; }
    public static NodeSnapshot Unknown { get; }
    public static NodeSnapshot Variable(NodeId id, QualifiedName browseName, NodeId dataType,
        int valueRank = ValueRanks.Scalar, byte accessLevel = AccessLevels.CurrentRead,
        NodeId typeDefinition = default);     // typeDefinition default => VariableTypeIds.BaseDataVariableType
    public static NodeSnapshot Object(NodeId id, QualifiedName browseName, NodeId typeDefinition = default);
    public static NodeSnapshot Method(NodeId id, QualifiedName browseName, bool executable = true);
    // BrowseName/DisplayName/NodeClass/DataType/ValueRank/AccessLevel accessors …
}

public readonly struct NodeReference
{
    public static NodeReference Component(NodeId source, NodeId target, NodeId typeDefinition = default);
    public static NodeReference Organizes(NodeId source, NodeId target);
    public static NodeReference Of(NodeId referenceTypeId, bool isForward, ExpandedNodeId target);
    // optional inline target attributes to save a Resolve round-trip …
}

public readonly struct BrowseFilter { /* BrowseDirection, ReferenceTypeId, IncludeSubtypes, NodeClassMask */ }
public readonly struct ReadTarget   { public NodeId NodeId {get;} public uint AttributeId {get;}
                                      public NumericRange IndexRange {get;} public QualifiedName DataEncoding {get;} }
public readonly struct WriteTarget  { public NodeId NodeId {get;} public uint AttributeId {get;} public NumericRange IndexRange {get;} }
public readonly struct MonitoredNodeRequest { public NodeId NodeId {get;} public TimeSpan SamplingInterval {get;} public uint AttributeId {get;} }
public readonly struct CallOutcome  { public ServiceResult Result {get;} public ArrayOf<Variant> Outputs {get;} }
```

Two DI entry points, one verb:

```csharp
public static class AddressSpaceServiceCollectionExtensions
{
    /// <summary>Inline imperative source. The delegate is the ONE method you write.</summary>
    public static IOpcUaServerBuilder AddAddressSpace(
        this IOpcUaServerBuilder b, string namespaceUri,
        Func<IAddressSpace, CancellationToken, ValueTask> build);

    /// <summary>DI-activated source (hand-written or [AddressSpace]-generated).
    /// Constructor-injected; direct-construct fallback: new NodeSourceHost(source).</summary>
    public static IOpcUaServerBuilder AddAddressSpace<
        [DynamicallyAccessedMembers(PublicConstructors)] TSource>(this IOpcUaServerBuilder b)
        where TSource : class, INodeSource;
}
```

The generator path — one attribute, one partial method, same `IAddressSpace`:

```csharp
[AttributeUsage(AttributeTargets.Class)]
public sealed class AddressSpaceAttribute : Attribute   // supersedes [NodeManager]
{
    public string NamespaceUri { get; set; }
    public string Design { get; set; }
}
// Emitted per attributed class: an INodeSource whose BindAsync loads the NodeSet2
// predefined nodes then calls `partial void Build(IAddressSpace)` and the typed
// `partial void Build(I{Model}AddressSpace)`; plus the DI registration.
```

That is the complete surface: `INodeSource` (2), `IAddressSpace` (~11), the handles, `IVirtualNodes` (3) + four opt-in one-to-three-member capabilities, `[AddressSpace]`, and `AddAddressSpace`. Reused unchanged: `IValueUpdater<T>`, `IHistorianProvider`, `Variant`, `DataValue`, `NodeId`, `ArrayOf<T>`.

---

#### 2. Usage example

**(a) Trivial server — a handful of variables, one method body, zero framework types named beyond the builder:**

```csharp
HostApplicationBuilder app = Host.CreateApplicationBuilder(args);
app.Services.AddOpcUa()
   .AddServer(o => { o.ApplicationName = "Mini"; o.EndpointUrls.Add("opc.tcp://localhost:4840/mini"); })
   .AddAddressSpace("urn:example:mini", (space, ct) =>
   {
       NodeId plant = space.AddFolder("Plant").NodeId;

       space.AddVariable<double>("Temperature", plant)
            .OnRead(_ => new ValueTask<double>(Sensor.ReadCelsius()));

       space.AddVariable<bool>("PumpRunning", plant)
            .Observe(out IValueUpdater<bool> pump);          // push from anywhere later:
       PumpMonitor.OnChange = running => pump.SetValue(running);

       space.AddMethod("Reset", plant)
            .OnCall(_ => { Plant.Reset(); return default; });

       return default;                                       // ValueTask.CompletedTask
   });
await app.Build().RunAsync();
```

**(b) Non-trivial — an external SCADA gateway with millions of tags in a database, browse-on-demand, live subscriptions, writes.** The author implements `INodeSource` plus three opt-in capabilities. No node is materialized; the framework does browse-slicing, continuation persistence, the publish pipeline and routing:

```csharp
[AddressSpace(NamespaceUri = "urn:example:scada")]   // attribute optional; only needed for a typed model
public sealed class ScadaSource : INodeSource, IVirtualNodes, IWritableNodes, IObservableNodes
{
    private readonly ITagStore _tags;                 // true-external dependency, DI-injected
    public ScadaSource(ITagStore tags) => _tags = tags;

    public ArrayOf<string> Namespaces => ["urn:example:scada"];

    public ValueTask BindAsync(IAddressSpace space, CancellationToken ct)
    {
        space.AddFolder("Tags");        // one materialized anchor node
        space.Virtualize(this);         // everything beneath is answered on demand
        return default;
    }

    public async ValueTask<NodeSnapshot> ResolveAsync(ISystemContext c, NodeId id, CancellationToken ct)
    {
        if (!TagId.TryParse(id, out TagId tag)) return NodeSnapshot.Unknown;
        TagInfo? info = await _tags.FindAsync(tag, ct).ConfigureAwait(false);
        return info is null
            ? NodeSnapshot.Unknown
            : NodeSnapshot.Variable(id, info.Name, DataTypeIds.Double,
                                    accessLevel: AccessLevels.CurrentReadOrWrite);
    }

    public async IAsyncEnumerable<NodeReference> BrowseAsync(
        ISystemContext c, NodeId id, BrowseFilter filter, [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (TagInfo child in _tags.ChildrenOf(id, ct).ConfigureAwait(false))
        {
            yield return NodeReference.Component(id, child.NodeId, DataTypeIds.Double);
        }
        // Framework slices this into continuation points and enforces maxReferences.
    }

    public async ValueTask<DataValue> ReadAsync(ISystemContext c, ReadTarget t, CancellationToken ct)
    {
        double v = await _tags.ReadAsync(TagId.Parse(t.NodeId), ct).ConfigureAwait(false);
        return new DataValue(Variant.From(v), StatusCodes.Good, DateTimeUtc.Now);
    }

    public async ValueTask<ServiceResult> WriteAsync(
        ISystemContext c, WriteTarget t, DataValue value, CancellationToken ct)
    {
        if (!value.WrappedValue.TryGetValue(out double v)) return StatusCodes.BadTypeMismatch;
        await _tags.WriteAsync(TagId.Parse(t.NodeId), v, ct).ConfigureAwait(false);
        return ServiceResult.Good;
    }

    public async IAsyncEnumerable<DataValue> ObserveAsync(
        ISystemContext c, MonitoredNodeRequest r, [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (double v in _tags.SubscribeAsync(TagId.Parse(r.NodeId), r.SamplingInterval, ct)
                                        .ConfigureAwait(false))
        {
            yield return new DataValue(Variant.From(v), StatusCodes.Good, DateTimeUtc.Now);
        }
        // Framework owns queueing, deadband, publish and subscription transfer.
    }
}

// wiring:
app.Services.AddSingleton<ITagStore, SqlTagStore>();
app.Services.AddOpcUa().AddServer(/*…*/).AddAddressSpace<ScadaSource>();
```

**(c) Companion-spec model (Boiler)** — identical ergonomics to today's generated `Configure`, but through `IAddressSpace`. Author writes one typed partial method; the generated source does the NodeSet2 load:

```csharp
[AddressSpace(NamespaceUri = "http://opcfoundation.org/UA/Boiler/")]
public partial class BoilerSource
{
    partial void Build(IBoilerAddressSpace space)          // typed traversal, generated per model
    {
        space.Boilers.Boiler__1.LCX001.Measurement
             .OnRead(_ => new ValueTask<double>(_level.Read()))
             .Historize();

        space.Boilers.Boiler__1.Simulation.Halt
             .OnCall(ct => HaltAsync(ct));

        space.Boilers.Boiler__1.DrumX001
             .OnEvent(out IEventEmitter drum);             // drum.Emit(new BaseEventState(…))
        _heartbeat = drum;
    }
}
```

---

#### 3. What the implementation hides behind the seam

Everything below stays in the tree (backward compatibility) but moves *off the authoring interface*. The default in-memory source **is** today's `AsyncCustomNodeManager`; a single internal `NodeSourceManager : IAsyncNodeManager` adapts every `INodeSource` onto the existing routing table, so `MasterNodeManager` is untouched.

| Machinery | Lines / size | Was on interface as… | Now |
|---|---|---|---|
| `AsyncCustomNodeManager` | 8,028; 93 virtual/abstract, 80 distinct names | base class you inherit + override | private engine of the built-in source; author never derives it |
| `CustomNodeManager2` | 6,302; 93 virtual | base class you inherit | retained for the 33 legacy subclasses only |
| `MasterNodeManager` | 7,601; 36 public | routing you must satisfy (`Processed`, `GetManagerHandle`) | private; `INodeSource` adapted onto its routing table |
| `ISubscription` / `Subscription` | 42 members / 3,471 | 11-member publish-pipeline protocol | fully private; `IObservableNodes.ObserveAsync` is the whole author view |
| `IServerInternal` / `ServerInternalData` | 57 members / 1,355 | service locator every manager receives | replaced at the seam by `IAddressSpaceServices` (telemetry + namespaces + base-service registration) |
| continuation-point slicing | `ref ContinuationPoint` + IContinuationPointStore | `ref` cursor + "references may already contain references" | framework slices `IAsyncEnumerable<NodeReference>` |
| `NodeState` graph + 18 delegates | 5,824; 155 members | the object you hand-wire | optional; reachable via handles, not required |

**Deletion test, applied explicitly:**

1. **`object? GetManagerHandle(NodeId)` + `object sourceHandle` opaque handle.** Delete it from the interface: does complexity reappear across callers? No. It existed only to cache a NodeId parse and round-trip it back; every implementer invented a handle and every `MasterNodeManager` path treated it opaquely. Addressing by `NodeId` (a source parses/caches privately if it wants) makes the complexity vanish, not relocate. **It was a pass-through leak → deleted outright** (and with it two `object` violations of the type rules).

2. **The `Processed` flag protocol** (restated 6× in prose, enforced only by convention). Delete it from the interface: complexity *reappears* — but concentrated, not scattered. It exists because `MasterNodeManager` fans a batch to every manager. In a namespace-partitioned world (the 99% case) the framework already knows the owner by `NamespaceIndex`, so it hands a source only its own nodes and the flag is unnecessary. For the rare non-namespace partition it survives as ONE dispatcher concern. **It was earning its keep across N callers → relocated behind the seam into one place (locality), not deleted.**

3. **`ref ContinuationPoint` + "references may already contain references" on Browse.** Delete it from the interface: the cursor/partial-fill logic reappears — in exactly one framework component that slices `IAsyncEnumerable<NodeReference>` over the existing `IContinuationPointStore`. **Earning its keep → relocated, verified once, not re-implemented by every one of the 21 `CreateAddressSpace`/browse authors.**

4. **`IServerInternal` (34 subsystem getters + 12 `Set*` mutators, two-phase construction).** Delete it *from the author's view*: does address-space-author complexity reappear? No — an address-space author needed telemetry, the namespace table, and registration of a handful of base providers, all now on `IAddressSpaceServices`. The 20-subsystem locator was a pass-through for them. It stays internal to the runtime; it leaves the seam authors cross. (Bonus: the diagnostics `object`-lock leak on `IServerInternal`/`ISession`/`ISubscription` is never re-exposed — no interface here surfaces a lock, and `IValueUpdater<T>` serializes writes internally with `System.Threading.Lock`.)

---

#### 4. Dependency strategy and adapters

| Dependency | Category | Port at the seam? | Adapters / reuse |
|---|---|---|---|
| Node graph, diagnostics, publish pipeline, request routing, continuation slicing, index-alignment | **1 – In-process** | **No port.** Pure in-memory computation, merged into the framework and tested *through* `IAddressSpace` / `IVirtualNodes`. | n/a — these are implementation, exercised by the two examples above |
| Continuation-point store, subscription store, monitored-item queue factory | **2 – Local-substitutable** | Internal seam only; **not surfaced to authors.** | **Reuse** existing `IContinuationPointStore`, `ISubscriptionStore`, `IMonitoredItemQueueFactory` (in-memory default + persisted adapter) |
| Distributed / HA movable state | **3 – Remote-but-owned** | Port exists; **reused, not invented.** | **Reuse** `INodeStateStore` / `ISharedKeyValueStore` with two adapters — `InMemoryNodeStateStore` (local/test) and Redis/CRDT (prod). The built-in source already adapts its graph to `ILocalAddressSpace` (capture/apply), so `AddressSpaceSynchronizer` mirrors an `INodeSource` unchanged. A **virtual** `INodeSource` is *inherently* HA-friendly: it holds no authoritative state, so standbys serve identical content by construction. |
| Historian | **4 – True external** | **Reuse** `IHistorianProvider` (the exemplar umbrella + capability seam). | `space.UseHistorian(provider)`; `IVariableHandle.Historize()`. In-memory historian (default) + user historian (prod) |
| File system, secret store, certificate manager | **4 – True external** | **Reuse** `IFileSystemProvider`, secret/cert seams via `IAddressSpaceServices`. | existing providers; nothing new |
| The author's own external system (SCADA DB, robot, PLC gateway) | **4 – True external** | **`IVirtualNodes` (+ opt-in caps) — the one genuinely new port.** | Two real adapters justify it: the **built-in NodeState-backed source** and any **external-system source** (example (b)). Two adapters ⇒ a real seam, not a hypothetical one. In tests, mock `IVirtualNodes` directly — the test crosses the same seam the runtime does. |

No new remote-but-owned or true-external port was invented except `IVirtualNodes`, and that one clears the two-adapter bar. Everything else reuses a seam that already ships.

---

#### 5. Trade-offs

**Where leverage is high.** The trivial and companion-spec authors collapse from "derive an 8,028-line base class, override `CreateAddressSpaceAsync`/`Dispose`/`New`/`LoadPredefinedNodesAsync`, register an `INodeManagerFactory`, satisfy the `Processed`/continuation/index-align contracts" to **one method body**. The virtual author writes 2–5 members whose signatures fit on one line each and gets browse-slicing, continuation persistence, the publish pipeline, subscription transfer and routing for free. The interface *is* the test surface: you unit-test a source by calling `BindAsync`/`ReadAsync`/`BrowseAsync`, the exact methods the runtime calls — no need to stand up a `MasterNodeManager` to test past the seam.

**Where it is thin.** For a *fully materialized* model the `IVirtualNodes` port earns nothing — the built-in source is the only adapter that path ever sees, so for those authors it is a hypothetical seam kept alive only by the external-system author next door. That is the intended cost of one unified seam.

**What gets harder.**
- **Bespoke continuation-point semantics.** You no longer own slicing. Escape hatch: yield your own cursor state and let the framework persist it via `IContinuationPointStore`, or drop to the retained `AsyncCustomNodeManager`.
- **Non-namespace partitioning** (the current `GetManagerHandle` override). `INodeSource` partitions strictly by namespace. The rare custom-partition server keeps subclassing the retained `MasterNodeManager`.
- **Reaching for one of the 55 never-overridden virtuals.** Not on the new surface; those authors use the retained base class. Given the measured reality — 112 override sites, 25 distinct names, 6 names carrying nearly all use — this affects almost no one.

**What I gave up to hit the constraint.** The fine-grained *one-interface-per-service-call* substitutability of the current 15 async capability interfaces — collapsed to 1 mandatory + 4 opt-in, cut at behavioural seams (content / write / call / observe / topology) instead of at service names. And direct `NodeState` delegate wiring as the *primary* path — still reachable through handles and the retained `INodeManagerBuilder`, but no longer the front door.

**Migration path from the 93-virtual / 155-member world.**
- **Additive, `[Obsolete]` per the 1.5.378 rule.** `INodeManager`/`INodeManager2`/`INodeManager3` (the sync family) and their factory get `[Obsolete]` (the tree already normalizes to `IAsyncNodeManager` before dispatch, so this is labelling, not behaviour change). `IAsyncNodeManager` + the 15 capability interfaces and `AsyncCustomNodeManager`/`CustomNodeManager2` are **retained unchanged** so all 33 subclasses keep compiling.
- **One internal adapter** `NodeSourceManager : IAsyncNodeManager` bridges `INodeSource` onto the existing routing table — `MasterNodeManager`, `NodeManagerRoutingTable`, the publish pipeline and HA synchronizer are untouched.
- **`[NodeManager]` → `[AddressSpace]`** (superset); `[NodeManager]` becomes an `[Obsolete]` alias emitting the same code, so existing generated servers rebuild clean.
- **`INodeManagerBuilder`/`INodeBuilder`/`IVariableBuilder<T>`** (2.0-only, not in 1.5.378) are retained as the *advanced* surface; `IAddressSpace` is the minimal core, with two-way adapters (`space.AsNodeManagerBuilder()` / `builder.AsAddressSpace()`) so a manager can mix both during transition.
- **`IServerInternal`** stays (200+ references, 1.5.378) but is documented legacy; new authors receive `IAddressSpaceServices`.
- **Companion specs** (DI, GDS, PubSub, Robotics, ISA95, WoT) migrate file-by-file: swap the generated base from `CustomNodeManager2` to a generated `INodeSource`, keep the `Build`/`Configure` body verbatim. NodeSet2 runtime loading is unchanged (`space.Import(NodeModel.FromNodeSet2(stream))`). NativeAOT/trimming holds — the runtime path is reflection-free, handles are structs, and DI activation uses `[DynamicallyAccessedMembers(PublicConstructors)]` exactly as the current `AddNodeManager<TFactory>` does.

---

## Design 2 — Composition-First (maximise flexibility)

**Design constraint given:** *Maximise flexibility and composability — but achieve it through composition of small deep modules, NOT through a wide inheritance surface. A user should be able to replace or intercept ANY behaviour without subclassing an 8,000-line base class.*

> Reproduced verbatim as delivered by the designer. Section numbering is the designer's own.

### Composition-First Server Interface for OPC UA .NET Standard

**Thesis.** The codebase already contains the composition model it needs — it is just applied to *one* subsystem (history) and everything else is inheritance. `IHistorianProvider` is a 2-member umbrella over ~5,900 lines, with opt-in capability facets a dispatcher probes by `is`/`as`, and `IHistorianRegistryProvider` extends the server *without touching `IServerInternal`*. My design generalises exactly that shape to the node-manager surface, the server surface, node behaviour, interception, and authentication. No new port is invented; every seam already has (or trivially has) two adapters.

---

#### 1. Interface

##### 1.1 The address-space partition — one umbrella, opt-in facets

The umbrella replaces the ownership/lifecycle half of `INodeManager`/`INodeManager2`/`INodeManager3`. It is the *only* interface a partition must implement. Everything operational is an opt-in facet — the exact pattern of `IReadAsyncNodeManager` et al., but now attached to something you **compose**, not a base class you inherit.

```csharp
namespace Opc.Ua.Server.AddressSpace;

/// <summary>
/// A partition owns a set of NodeIds (usually one or more namespaces) and knows
/// how to load/unload them. It advertises operational capability by ALSO
/// implementing one or more service-set handler facets (IReadHandler, IBrowseHandler…).
/// Sealed by default; deep behaviour lives behind the implementation, never inherited.
/// Invariants:
///   • NamespaceUris is stable for the partition's lifetime and non-overlapping
///     with any other registered partition (the router enforces this at startup).
///   • TryGetOwnership must NOT block on I/O; it recognises the NodeId syntax only.
///   • LoadAsync runs exactly once before the partition serves any request.
/// </summary>
public interface IAddressSpacePartition
{
    ArrayOf<string> NamespaceUris { get; }

    ValueTask LoadAsync(IAddressSpaceEditor editor, CancellationToken ct = default);
    ValueTask UnloadAsync(CancellationToken ct = default);

    /// <summary>Ownership probe. Returns a value-typed handle — never `object`.</summary>
    bool TryGetOwnership(NodeId nodeId, out NodeOwnership ownership);
}

/// <summary>Replaces `object? GetManagerHandle(NodeId)`. Value type, no boxing, no round-trip.</summary>
public readonly struct NodeOwnership
{
    public bool IsOwned { get; init; }
    public NodeState? Node { get; init; }   // in-memory fast path (may be null for external partitions)
    public ulong Token { get; init; }       // partition-local token for browse-on-demand partitions
}
```

Service-set facets — each is the *interface a caller must learn to intercept that one operation*. These are re-cut from the existing 14 `I*AsyncNodeManager` facets, changing only the parameter shape (see §1.2). Old facets survive as `[Obsolete]` adapters (§5).

```csharp
public interface IReadHandler            { ValueTask ReadAsync(ReadBatch batch, CancellationToken ct = default); }
public interface IWriteHandler           { ValueTask WriteAsync(WriteBatch batch, CancellationToken ct = default); }
public interface IBrowseHandler          { ValueTask<ContinuationPoint?> BrowseAsync(BrowseRequest req, IReferenceSink sink, CancellationToken ct = default); }
public interface ICallHandler            { ValueTask CallAsync(CallBatch batch, CancellationToken ct = default); }
public interface IHistoryReadHandler     { ValueTask HistoryReadAsync(HistoryReadBatch batch, CancellationToken ct = default); }
public interface IHistoryUpdateHandler   { ValueTask HistoryUpdateAsync(HistoryUpdateBatch batch, CancellationToken ct = default); }
public interface ITranslateBrowsePathHandler { ValueTask TranslateAsync(TranslateRequest req, IBrowsePathSink sink, CancellationToken ct = default); }
public interface INodeManagementHandler  { bool AllowNodeManagement { get; } /* AddNode/DeleteNode/AddReference/DeleteReference */ }
public interface IEventNotifierHandler   { ValueTask<ServiceResult> SubscribeToEventsAsync(EventSubscriptionRequest req, CancellationToken ct = default); }

/// <summary>Monitoring lifecycle — the 5 monitored-item operations grouped (they always co-vary).</summary>
public interface IMonitoringHandler
{
    ValueTask CreateAsync(MonitoredItemCreateBatch batch, CancellationToken ct = default);
    ValueTask ModifyAsync(MonitoredItemModifyBatch batch, CancellationToken ct = default);
    ValueTask DeleteAsync(MonitoredItemDeleteBatch batch, CancellationToken ct = default);
    ValueTask SetModeAsync(MonitoringModeBatch batch, CancellationToken ct = default);
    ValueTask TransferAsync(MonitoredItemTransferBatch batch, CancellationToken ct = default);
}

/// <summary>Observe-only hooks (today: OnMonitoredItemCreated/Deleted/Modified, 4+3+2 override sites).</summary>
public interface IMonitoringObserver
{
    ValueTask OnCreatedAsync(NodeState source, ISampledDataChangeMonitoredItem item, CancellationToken ct = default);
    ValueTask OnDeletedAsync(NodeState source, IMonitoredItem item, CancellationToken ct = default);
    ValueTask OnModeChangedAsync(NodeState source, IMonitoredItem item, CancellationToken ct = default);
}
```

**Depth of the umbrella.** `IAddressSpacePartition` is 4 members; behind it sits the whole `NodeState` graph (5,824 lines), predefined-node loading, sampling groups, and reference bookkeeping — *for the in-memory adapter*. A browse-on-demand adapter puts a REST/SQL client behind the same 4 members. That is the leverage: one interface, radically different implementations.

##### 1.2 Deep operation objects — dissolving the `Processed` protocol and the index-aligned accumulators

This is the single biggest composability win. Today every facet carries three pieces of *prose-enforced* interface complexity: (a) the `Processed` cooperative-multiplex flag (restated 6×), (b) caller-pre-sized index-aligned parallel `values`/`errors` lists, (c) "references may already contain references". All three exist **because `MasterNodeManager` broadcasts each request to every node manager** (`foreach (IAsyncNodeManager nm in m_nodeManagers) await nm.ReadAsync(...)`, `MasterNodeManager.cs:4053`).

I move ownership resolution *ahead* of dispatch. The router splits the request by owning partition and hands each partition **only its own items, locally indexed**, with a result sink that owns the local→global mapping:

```csharp
/// <summary>
/// A partition's slice of a Read. Contains ONLY the items this partition owns.
/// There is no Processed flag (you were only handed your items) and no global
/// index alignment (Complete/Fail address local indices 0..Items.Count-1).
/// `ReadBatch` is a readonly struct over a pooled backing buffer — allocation-free in steady state.
/// </summary>
public readonly struct ReadBatch
{
    public OperationContext Context { get; }
    public IServerContext Server { get; }
    public double MaxAge { get; }
    public TimestampsToReturn TimestampsToReturn { get; }
    public ArrayOf<ReadValueId> Items { get; }              // this partition's items, contiguous
    public NodeOwnership OwnershipOf(int localIndex);        // resolved handle, no re-lookup
    public void Complete(int localIndex, in DataValue value);// by-ref in; DataValue implements INullable — never Nullable<DataValue>
    public void Fail(int localIndex, StatusCode status);
}
```

`BrowseRequest`/`IReferenceSink` remove the "already contains references" clause — the partition only ever *emits*:

```csharp
public readonly struct BrowseRequest
{
    public OperationContext Context { get; }
    public IServerContext Server { get; }
    public NodeOwnership Source { get; }
    public BrowseDescription Description { get; }
    public ContinuationPoint? Resume { get; }   // null on first page
}
public interface IReferenceSink
{
    /// <summary>The sink owns dedup, NodeClassMask filtering it can do, and continuation sizing.</summary>
    void Add(in ReferenceDescription reference);
    bool IsFull { get; }   // partition checks this to decide when to return a ContinuationPoint
}
```

##### 1.3 The interception pipeline — replace "override a virtual to intercept a service"

This is the composition answer to *"a user should be able to intercept ANY behaviour without subclassing."* Interceptors are capability-probed exactly like historian providers — an interceptor implements only the service facets it cares about; the pipeline builder inspects with `is`/`as` at startup and builds one ordered array per service set. The chain is walked by a **struct** cursor, so there is no per-operation closure or delegate allocation.

```csharp
public interface IReadInterceptor  { ValueTask InvokeAsync(ReadBatch batch, ReadPipeline next, CancellationToken ct); }
public interface IWriteInterceptor { ValueTask InvokeAsync(WriteBatch batch, WritePipeline next, CancellationToken ct); }
public interface ICallInterceptor  { ValueTask InvokeAsync(CallBatch batch, CallPipeline next, CancellationToken ct); }
// … one thin interceptor facet per service set; an interceptor implements only what it intercepts.

/// <summary>Struct cursor: holds (interceptor[] chain, int index, terminal handler). No allocation to advance.</summary>
public readonly struct ReadPipeline
{
    public ValueTask InvokeAsync(ReadBatch batch, CancellationToken ct);  // advances to next interceptor, or the router terminal
}
```

Interception operates at **batch granularity** — the stack grows O(interceptor count) *per service call*, never per node. A server with 10k monitored items and 3 interceptors sees 3 frames, not 30 000.

##### 1.4 The node-behaviour seam — collapse 18 delegates to two providers

The 18 `NodeState` delegates (9 sync/async pairs) are a real composition mechanism, but they are shallow — 18 interface elements for what is conceptually "intercept a value read" and "intercept a value write". The async ones already return record structs (`AttributeReadResult`) because `ref` cannot cross `await`. I keep that insight and collapse the surface:

```csharp
/// <summary>Per-node (or per-type) value behaviour. Async-only. Replaces the 9 read/write delegates.</summary>
public interface INodeValueBehavior
{
    ValueTask<AttributeReadResult>  ReadAsync(NodeValueReadContext ctx, CancellationToken ct);
    ValueTask<AttributeWriteResult> WriteAsync(NodeValueWriteContext ctx, CancellationToken ct);
}
public interface INodeEventBehavior   // replaces ReportEvent/ConditionRefresh/CreateBrowser delegates
{
    ValueTask ReportAsync(NodeEventContext ctx, CancellationToken ct);
    ValueTask ConditionRefreshAsync(NodeConditionRefreshContext ctx, CancellationToken ct);
}
```

A node gets behaviour by *attachment*, not subclass: `node.Attach(behavior)`. The existing delegates remain as a `DelegateNodeBehavior` adapter (§4) so nothing breaks and the source generator can emit either.

##### 1.5 The server context — replace the 57-member service locator

`IServerInternal` (57 members, 200+ referencing files, `object DiagnosticsLock`/`DiagnosticsWriteLock`, 12 `Set*` mutators, two-phase `SetNodeManager` that reaches into its argument) is a service locator with zero depth. I split it into (a) a small ambient context every partition genuinely needs, and (b) **capability-probe interfaces** for the ~20 subsystems — reusing `IHistorianRegistryProvider` verbatim as the template.

```csharp
/// <summary>The ambient a partition always needs. ~8 members. No locks, no Set* mutators, no subsystems.</summary>
public interface IServerContext
{
    ITelemetryContext        Telemetry { get; }
    NamespaceTable           NamespaceUris { get; }
    StringTable              ServerUris { get; }
    IServiceMessageContext   MessageContext { get; }
    ServerSystemContext      DefaultSystemContext { get; }
    IEncodeableFactory       Factory { get; }
    ServerState              State { get; }
    ValueTask ReportEventAsync(IFilterTarget e, CancellationToken ct = default);
}

// Subsystems are opt-in probes — a partition that never touches subscriptions never learns this exists.
public interface ISubscriptionHost      { ISubscriptionCoordinator Subscriptions { get; } }
public interface ISessionHost           { ISessionRegistry Sessions { get; } }
public interface IDiagnosticsHost       { IDiagnosticsRecorder Diagnostics { get; } }  // owns its own System.Threading.Lock internally
public interface IHistorianRegistryProvider { IHistorianProviderRegistry HistorianRegistry { get; } } // REUSED, unchanged
```

Usage inside a partition: `if (Server is ISubscriptionHost host) { … host.Subscriptions … }`. `ServerContext` (production) implements all of them; the test double implements only what a given test exercises.

Crucially, **no lock ever appears**. `IDiagnosticsRecorder` is a deep module that takes `DataValue`s and computes diagnostics behind its own `private readonly System.Threading.Lock m_lock = new();`. The 88 `lock` statements over the published `object` locks collapse into it.

##### 1.6 The authentication pipeline — replace extension-by-mutable-event-args

`ISessionManager.ImpersonateUser` decides auth by mutating `ImpersonateEventArgs.Identity`/`.EffectiveIdentity`/`.IdentityValidationError` across ordered handlers with no failure channel (`ISessionManager.cs:279–289`). The repo already gestures at the replacement in the `[Obsolete]` message ("Replaced by IUserTokenAuthenticator + IServerIdentityRegistry"). I formalise it as an ordered chain returning a value with a real result channel:

```csharp
public interface IUserTokenAuthenticator
{
    ValueTask<AuthenticationResult> AuthenticateAsync(AuthenticationRequest request, CancellationToken ct = default);
}
public readonly record struct AuthenticationResult(
    AuthenticationDecision Decision,
    IUserIdentity? Identity,
    IUserIdentity? EffectiveIdentity,
    ServiceResult Error);
public enum AuthenticationDecision { Continue, Grant, Reject }  // first Grant/Reject wins; Continue defers to the next
```

No mutation, no invisible ordering dependency, and `Reject` carries a `ServiceResult` — the missing failure channel.

##### 1.7 The composition root (DI, with direct-construct fallback)

```csharp
public interface IOpcUaServerBuilder   // extends today's builder
{
    IServiceCollection Services { get; }
    IOpcUaServerBuilder AddPartition<[DynamicallyAccessedMembers(PublicConstructors)] T>()      where T : class, IAddressSpacePartition;
    IOpcUaServerBuilder AddPartition(string namespaceUri, Action<INodeManagerBuilder> configure);// fluent — NO base class
    IOpcUaServerBuilder AddReadInterceptor<[DynamicallyAccessedMembers(PublicConstructors)] T>() where T : class, IReadInterceptor;
    IOpcUaServerBuilder AddAuthenticator<[DynamicallyAccessedMembers(PublicConstructors)] T>()   where T : class, IUserTokenAuthenticator;
    // Back-compat bridges:
    IOpcUaServerBuilder AddNodeManager<T>()     where T : class, IAsyncNodeManagerFactory; // [Obsolete] path, wrapped by LegacyPartitionAdapter
}
```

`is`/`as` probing and `ActivatorUtilities` construction are AOT-safe (the `[DynamicallyAccessedMembers(PublicConstructors)]` annotation is already used by today's `AddNodeManager<TFactory>`). No open-generic registration, no reflection over methods. Direct-construct fallback: `new ServerComposition(context, partitions, interceptors, authenticators)` — every piece is constructor-injectable without a container.

---

#### 2. Usage example

##### (a) Trivial server — a handful of variables, zero inheritance

```csharp
builder.Services.AddOpcUa().AddServer(o => o.ApplicationName = "Demo")
    .AddPartition("urn:demo", ns =>
    {
        ns.Variable<double>("Temperature").OnRead(_ => ReadSensor());
        ns.Variable<bool>("Enabled").OnWrite((_, v) => { _enabled = v; return ServiceResult.Good; });
    });
```

The fluent `INodeManagerBuilder` is unchanged from today — but it now configures a composed `NodeStatePartition`, **not** a `FluentNodeManagerBase : AsyncCustomNodeManager`. The user inherits nothing.

##### (b) Demanding case — browse-on-demand over an external system, no `NodeState` graph

A partition that projects a live SQL catalogue as OPC UA nodes. It implements the umbrella plus exactly the three facets it needs — ~a few hundred lines, no 8,028-line base class, and it never materialises a node graph.

```csharp
public sealed class SqlCatalogPartition : IAddressSpacePartition, IBrowseHandler, IReadHandler
{
    private readonly ISqlCatalog m_db;
    private readonly ushort m_ns;
    public SqlCatalogPartition(ISqlCatalog db) => m_db = db;

    public ArrayOf<string> NamespaceUris => new[] { "urn:acme:sql-catalog" };

    public ValueTask LoadAsync(IAddressSpaceEditor editor, CancellationToken ct) => default; // nothing to preload

    public bool TryGetOwnership(NodeId nodeId, out NodeOwnership o)
    {
        // recognise "tbl:<id>" syntax without touching the DB (interface says: no I/O here)
        if (nodeId.NamespaceIndex == m_ns && nodeId.Identifier is string s && s.StartsWith("tbl:"))
        {
            o = new NodeOwnership { IsOwned = true, Token = ParseId(s) };
            return true;
        }
        o = default; return false;
    }

    public async ValueTask<ContinuationPoint?> BrowseAsync(BrowseRequest req, IReferenceSink sink, CancellationToken ct)
    {
        await foreach (Column col in m_db.EnumerateColumnsAsync(req.Source.Token, ct))
        {
            sink.Add(new ReferenceDescription { NodeId = ColumnNodeId(col), BrowseName = new QualifiedName(col.Name, m_ns), NodeClass = NodeClass.Variable });
            if (sink.IsFull) return ContinuationFrom(col);   // sink owns the sizing decision
        }
        return null;
    }

    public async ValueTask ReadAsync(ReadBatch batch, CancellationToken ct)
    {
        for (int i = 0; i < batch.Items.Count; i++)   // only MY items, locally indexed — no Processed flag
        {
            DataValue v = await m_db.ReadCellAsync(batch.OwnershipOf(i).Token, ct).ConfigureAwait(false);
            batch.Complete(i, v);
        }
    }
    public ValueTask UnloadAsync(CancellationToken ct) => default;
}
```

Registration: `.AddPartition<SqlCatalogPartition>()`. This is the demanding scenario the brief targets — **custom transport of nodes from an external system, no inheritance, no `Processed` bookkeeping**.

##### (c) Replacing a behaviour that today requires overriding one of the 93 virtuals

Today, to audit and role-check every `Read`/`Write` you override `CustomNodeManager2.Read` and `Write` (or `ValidateRolePermissions`, one of the tail virtuals). With composition it is an interceptor that touches nothing:

```csharp
public sealed class AuditInterceptor : IReadInterceptor, IWriteInterceptor, ICallInterceptor
{
    private readonly IAuditSink m_audit;
    public AuditInterceptor(IAuditSink audit) => m_audit = audit;

    public async ValueTask InvokeAsync(WriteBatch batch, WritePipeline next, CancellationToken ct)
    {
        await next.InvokeAsync(batch, ct).ConfigureAwait(false);       // run the write
        for (int i = 0; i < batch.Items.Count; i++)
            await m_audit.RecordWriteAsync(batch.Context, batch.Items[i], batch.ResultOf(i), ct).ConfigureAwait(false);
    }
    public ValueTask InvokeAsync(ReadBatch b, ReadPipeline next, CancellationToken ct) => next.InvokeAsync(b, ct);
    public ValueTask InvokeAsync(CallBatch b, CallPipeline next, CancellationToken ct) => AuditCallAsync(b, next, ct);
}
```

Registration: `.AddReadInterceptor<AuditInterceptor>()` (the builder probes it and also wires the Write/Call facets it implements). It composes across **every** partition, including the SQL one above — something an inheritance override can never do, because it would live on one base class.

Mapping of the 25 ever-overridden virtuals to their composition seam:

| Overridden virtual (sites) | Replacement seam |
|---|---|
| `CreateAddressSpaceAsync` (21) | `IAddressSpacePartition.LoadAsync` (it *is* the partition body) |
| `Dispose` (14) | partition `IAsyncDisposable`; registries own their own teardown |
| `New` NodeId factory (12) | `INodeIdFactory` provider (already exists) injected |
| `LoadPredefinedNodesAsync` (11) | `INodeSetSource` provider passed to `LoadAsync` |
| `DeleteAddressSpaceAsync` (8) | `IAddressSpacePartition.UnloadAsync` |
| `AddBehaviourToPredefinedNodeAsync` (5) | `INodeValueBehavior`/`INodeEventBehavior` attach |
| `OnMonitoredItem{Created,Deleted,Modified}` (4/3/2) | `IMonitoringObserver` facet |
| `GetHistorianProvider` (3) | `IHistorianRegistryProvider` (already exists) |
| `GetManagerHandleAsync` (3) | `IAddressSpacePartition.TryGetOwnership` |
| `ValidateNodeAsync` (3) | `IReadInterceptor`/`INodeValidator` provider |
| `OnMonitoringModeChangedAsync` (3) | `IMonitoringObserver.OnModeChangedAsync` |
| `ConditionRefreshAsync`/`OnSubscribeToEventsAsync` (2/2) | `IEventNotifierHandler` / `INodeEventBehavior` |
| `OnNodeRemovedAsync` (2) | `IAddressSpaceEditor` events (`ILocalAddressSpace.NodeRemoved`, reused) |

---

#### 3. What the implementation hides behind the seam (deletion tests)

**Moved from interface to implementation** (stays working, stops being something a caller must learn):
- The entire `NodeState` graph (5,824 lines) + the `src/Opc.Ua.Types/State` folder (15,434 lines) become the private implementation of one adapter — `NodeStatePartition`. Visible surface for that adapter: `IAddressSpacePartition` + facets. A browse-on-demand partition hides a DB client instead.
- `AsyncCustomNodeManager` (8,028 lines, 93 virtual/abstract) and `CustomNodeManager2` (6,302 lines) become the *body* of `NodeStatePartition`, exposed through facets — not 93 override points.
- `MasterNodeManager` routing (7,601 lines) becomes an internal `AddressSpaceRouter`; the `Processed` fan-out loop (`MasterNodeManager.cs:4053`, 6 restatements) is deleted.

**Deletion test #1 — the `Processed` flag protocol.** Delete it and the 6 prose restatements and the `foreach nodeManager` broadcast. Does complexity reappear across callers? *No.* It reappears in exactly **one** place — the router's ownership-split — which is complexity that must exist once anyway. The flag was pure pass-through overhead created by broadcast dispatch. It fails the test → remove it. Net: −6 duplicated contracts, −N partitions each re-implementing "check/set Processed".

**Deletion test #2 — `IServerInternal.DiagnosticsLock` / `DiagnosticsWriteLock` (`object`).** Delete them from the interface. Complexity reappears? Only inside `IDiagnosticsRecorder`, which needs *one* `System.Threading.Lock`. The 88 `lock` statements across 7 files (incl. a sample) that today take a *published* lock collapse to recorder-internal calls. The public lock was negative-value interface (it enabled the `DiagnosticsWriteLock`-scans-outside-its-own-lock bug). Fails the test spectacularly → remove.

**Deletion test #3 — `FluentNodeManagerBase : AsyncCustomNodeManager`.** Delete the inheritance. Complexity reappears? The fluent registries (`EventSourceRegistry`, `SimulationRegistry`) simply become constructor dependencies of `NodeStatePartition` — the `CreateFluentBuilder`/`AttachToBuilder`/`Configure`/`Seal` quadruple already exists and doesn't need the base class. The 93 inherited virtuals on the fluent surface vanish. The inheritance was a delivery mechanism for `PredefinedNodes` access, obtainable by composition. Fails the test → remove.

**Deletion test #4 — `IAsyncNodeManager.SyncNodeManager`.** This member exists purely to let callers cross *back* over the async seam. Delete it. Complexity reappears only in the one obsolete `INodeManager` bridge adapter. The single external `INodeManager` implementer (`SampleNodeManager`) confirms the sync seam is *hypothetical* (one adapter). Collapse it to an `[Obsolete]` adapter, not a first-class seam.

**Stays visible (correctly):** `NodeId`, `DataValue`, `ReferenceDescription`, `ContinuationPoint`, `OperationContext`, `ServiceResult`, `Variant`, `ArrayOf<T>` — the OPC UA domain vocabulary. `NodeState` stays visible *as a fast-path handle* on `NodeOwnership.Node` for in-memory partitions, because that genuinely varies (external partitions leave it null).

---

#### 4. Dependency strategy and adapters

Classifying every dependency and naming **two adapters per seam** (the anti-hypothetical-seam discipline the brief demands):

| Seam | Category | Adapter #1 | Adapter #2 | Port or internal |
|---|---|---|---|---|
| `IAddressSpacePartition` | 1 In-process / 3 Remote-owned | `NodeStatePartition` (in-memory graph) | `SqlCatalogPartition` / distributed-HA partition over movable state | External interface, no separate port (partition *is* the seam) |
| `IReadHandler`/`IBrowseHandler`/… facets | 1 In-process | NodeState default read/browse | `DiNodeManager` Call, browse-on-demand Browse (both exist today as overrides) | Internal to partition; probed by router |
| `IReadInterceptor` (+ Write/Call…) | 1 In-process | `AuditInterceptor` (repo has audit APIs) | `RolePermissionInterceptor` (Part 18, today the `ValidateRolePermissions` virtual) | Public seam; ordered array built at startup |
| `INodeValueBehavior` | 1 In-process | `DelegateNodeBehavior` (wraps the existing 18 delegates) | source-generated typed behavior / external value fetch | Internal; attach per-node |
| `IServerContext` + subsystem probes | 1/2 | `ServerContext` (production, over the real subsystems) | in-memory test context (partition unit tests) | Ambient; probes reuse `IHistorianRegistryProvider` |
| `IUserTokenAuthenticator` | 1/4 | `X509IdentityAuthenticator` | `UserNameIdentityAuthenticator` (+ anonymous) | Public seam; ordered chain |
| `IHistorianProvider` | 4 True-external | `InMemoryHistorianProvider` (exists) | user SQL/PI historian | **Reused unchanged** |
| `ILocalAddressSpace` | 2 Local-substitutable | node-manager `PredefinedNodes` | dictionary-backed test double | **Reused unchanged** |

**Reused, not invented:** `IHistorianProvider`(+capability facets)/`IHistorianProviderRegistry`, `ILocalAddressSpace`, `IMonitoredItemManager`, `IContinuationPointStore`, `ISubscriptionStore`, `IMonitoredItemQueueFactory`, `IFileSystemProvider`, `INodeIdFactory`, `IConformanceContributor`, and the `IHistorianRegistryProvider` capability-probe *pattern*. Every external dependency already sits behind a port — I add none.

**Kept internal (one adapter → hypothetical seam → NOT a public seam):**
- `AddressSpaceRouter` — the routing table (`NodeManagerRoutingTable` is already `IReadOnlyList<IAsyncNodeManager>` internally). One production implementation. If someone later needs a distributed router, promote it then.
- The pipeline-cursor structs (`ReadPipeline` etc.) — implementation detail of the interceptor mechanism.

---

#### 5. Trade-offs

**Where leverage is highest.** The umbrella + facet + interceptor triad. A consumer learns `IAddressSpacePartition` (4 members) + the one or two facets they need, and can implement *any* partition — in-memory, external, HA. Interceptors give cross-cutting reach (audit, auth, redaction, rate-limit — the repo already has a `RateLimiting` folder) across all partitions from a single registration, which inheritance fundamentally cannot. The 93-virtual/80-name surface becomes: 4 umbrella members + ~10 facet interfaces you pick from + ~8 interceptor facets. A companion spec (DI/GDS/PubSub/Robotics/ISA95/WoT) implements the umbrella, `INodeIdFactory`, `IConformanceContributor`, and overrides *nothing*.

**Where leverage is thin (honest).** `IServerContext` is close to a data holder — its depth is low (it mostly hands out ambient values). It earns its place only because it (a) deletes the 12 `Set*` mutators and the two `object` locks, and (b) shrinks the mandatory surface from 57 to ~8. I would *not* claim it is a "deep" module; it is a **narrowing** module. Similarly `NodeOwnership` is a thin value type — but it removes `object` from the hottest interface, which is worth a shallow struct.

**What gets harder.**
- **Ordering across interceptors** becomes explicit configuration (registration order) rather than implicit call order — more visible, but users must now *think* about interceptor order. Mitigation: `AddReadInterceptor` documents "outermost-first"; provide `Order` metadata for the rare conflict.
- **Cross-facet invariants** that a single base class enforced by construction (e.g. Create/Modify/Delete monitored-item state consistency) now span `IMonitoringHandler`'s 5 methods — which is *why* I grouped them into one interface rather than five, so a partition can't implement half.
- **Debugging a request** now steps through router → interceptor chain → facet rather than one virtual. Batch-granularity keeps the stack shallow, but it is more indirection than a single overridden method.

**Allocation / performance.** The design is explicitly built for the thousands-of-items hot path:
- Batches (`ReadBatch`, `WriteBatch`, …) are **readonly structs over pooled backing buffers**; the per-partition `ArrayOf<ReadValueId>` slices are rented from a pool. Steady-state Read/Write/Publish allocate **zero** per operation.
- Interceptor chains are **pre-built ordered arrays**; the `ReadPipeline` cursor is a struct — advancing the chain allocates nothing. Stack depth is O(interceptor count) **per service call**, not per node — the brief's explicit fear ("deep call stacks per node") is designed out.
- `is`/`as` capability probing happens **once at startup** when building the router's facet table and the interceptor arrays — never on the request path. AOT/trim-safe: no reflection, no `MakeGenericType`, no open-generic DI.
- The one cost: an ownership pre-split pass (O(items)) before dispatch. But `MasterNodeManager` already does an O(items) validation pass (`MasterNodeManager.cs:4023`), and the split *replaces* the O(items × partitions) broadcast — it is strictly cheaper on multi-partition servers.

**What I gave up.** I did not try to make `NodeState` itself composable-not-inherited — source generators emit `NodeState` subclasses and that is a 15k-line load-bearing contract; re-cutting it is out of scope and higher-risk than it is worth. I kept it whole behind the `NodeStatePartition` adapter and only collapsed its *delegate* surface (§1.4).

**Migration path (additive, `[Obsolete]`, 1.5.378-compatible).**
1. Ship `IAddressSpacePartition` + facets + router + interceptors + `IServerContext` alongside the existing interfaces. Nothing is removed.
2. `LegacyPartitionAdapter : IAddressSpacePartition` wraps any existing `IAsyncNodeManager` (delegating `TryGetOwnership`→`GetManagerHandleAsync`, facets→existing methods) so **all 33 existing subclasses run unchanged** under the new router. Conversely `PartitionAsNodeManager : IAsyncNodeManager` exposes a new partition to code still calling the old surface (mirrors today's `SyncNodeManagerAdapter`).
3. `ServerContext` implements `IServerInternal` too during the transition; the `object` `DiagnosticsLock`/`DiagnosticsWriteLock` getters are marked `[Obsolete]` and forward to `IDiagnosticsRecorder`.
4. Mark `INodeManager`/`INodeManager2`/`INodeManager3`, the `I*AsyncNodeManager` facets, `IAsyncNodeManager.SyncNodeManager`, `ImpersonateUser`/`ImpersonateEventArgs`, and the 18 `NodeState` delegates `[Obsolete]` with `see cref` to their composition replacement. (The `ImpersonateUser` obsoletion already exists and already points at `IUserTokenAuthenticator` — I am completing a migration the repo started.)
5. Re-point the source generator to emit `sealed partial class FooPartition : IAddressSpacePartition, IReadHandler, …` composing a `NodeStatePartition` core, instead of `: FluentNodeManagerBase`. Generated code targets the same facets a hand-written partition uses — which is the proof that the design is generator-emittable.

The end state: the *same* flexibility that 93 virtuals delivered, re-expressed as ~4 required members + a menu of opt-in facets + a cross-cutting interceptor chain — every one of which already has two real adapters in this repository.

---

## Design 3 — Progressive Ladder (optimise the common caller)

**Design constraint given:** *Optimise for the most common caller. Make the default case trivial — then make the progression to advanced cases smooth and explicit. Nobody should pay for a capability they do not use, and nobody should hit a cliff when they need one.*

> Reproduced verbatim as delivered by the designer. Section numbering is the designer's own.

### A Progressive Node-Source Interface for the OPC UA Server Stack

**Design axis: optimise for the most common caller.** The measured data says the common caller creates a set of nodes, backs some with a device or database, and overrides nothing else — 55 of 80 virtuals are never touched anywhere, and the entire common case reduces to six overrides that are *all boilerplate the source generator already writes*. So the level‑1 **interface** must be "declare nodes, back their values," and every rung above it must be reachable by *learning one more verb or injecting one more provider* — never by inheriting an 8,028‑line base class.

This design **builds on** three assets I verified in the tree rather than replacing them: the fluent builder (`src/Opc.Ua.Server/Fluent`, 35 files / 8,814 lines), the source generator (`BoilerNodeManager.NodeManager.g.cs` and the typed `Configure(IBoilerNodeManagerBuilder)` traversal), and the two reference‑quality seams `IStandardServer` (9 members over 5,575 lines) and `IHistorianProvider` (2 members + opt‑in capability interfaces). It removes the one structural flaw that turns the fluent layer into a veneer: `FluentNodeManagerBase : AsyncCustomNodeManager` (confirmed at `Fluent/FluentNodeManagerBase.cs:57`), which means a user who starts fluent and needs one more thing falls straight through to the 93‑virtual surface.

---

#### 1. Interface

##### The ladder at a glance — 4 rungs; you climb by adding a call or injecting a provider, never by subclassing

| Rung | Seam the author learns | Size of interface | What triggers the climb |
|---|---|---|---|
| **1** | `INodeSourceBuilder` — create + back nodes | ~6 verbs | "I have nodes, some static, some device/DB‑backed" |
| **2** | Capability verbs on the *same* builder + generated typed traversal | +1 verb per capability | history, alarms, a method, events, or a large authored NodeSet2 model |
| **3** | Inject `INodeProvider<THandle>` (+ opt‑in `IBrowse/IValue/ICallProvider`) | 2 core members + 1 per service | browse‑on‑demand over a huge/external space, custom partitioning, HA‑backed nodes |
| **4** | The existing `IAsyncNodeManager` / `AsyncCustomNodeManager` | full surface (unchanged) | anything off rungs 1–3 — a **documented step down**, not a silent fall |

The crucial property that distinguishes this design from the current one: **rungs 1–2 produce a `sealed` module.** You cannot subclass the produced manager, so there is no second surface to fall through to. To go past rung 2 you *deliberately* implement a provider (rung 3) or the full node‑manager interface (rung 4). The seam is a wall with a labelled door, not a slope.

##### Rung 1 — `INodeSourceBuilder`

This is the whole interface a common‑case author must know. It **extends** today's `INodeManagerBuilder` (`Fluent/INodeManagerBuilder.cs`, 306 lines) with the missing *creational* verbs. Today every `Variable<T>(...)` overload only *resolves* an existing node and throws `ServiceResultException` if absent (verified in the interface doc-comments: *"Thrown if the path does not resolve"*). That is why the no‑class `AddNodeManager("ns", b => …)` path cannot stand up a device server without first authoring a NodeSet2 — the builder can wire callbacks but cannot mint a plain variable. Closing that gap is the single most important ergonomic fix.

```csharp
namespace Opc.Ua.Server;

/// <summary>
/// Everything a common-case node author must know: create nodes and bind
/// what backs them. No NodeState, no NodeId factory, no IServerInternal,
/// no lock, no object.
/// </summary>
public interface INodeSourceBuilder
{
    /// <summary>
    /// Minimal context: the ~5 things authors actually used out of
    /// IServerInternal's 57 members.
    /// </summary>
    INodeSourceContext Context { get; }

    // --- create nodes (the fix: these MINT nodes; they do not resolve) ---
    IFolderNode      Folder(string browseName);
    IObjectNode      Object(string browseName, NodeId typeDefinition = default);
    IVariableNode<T> Variable<T>(string browseName);   // typed, minted, returns typed builder
    IMethodNode      Method(string browseName);

    // --- resolve nodes a model already created (NodeSet2 / companion spec) ---
    INode            Node(string browsePath);
    INode            Node(NodeId nodeId);
    IVariableNode<T> Bind<T>(NodeId nodeId);           // resolve + type-narrow

    // --- the one common-6 override that is genuinely author policy ---
    INodeSourceBuilder UseNodeIdScheme(INodeIdScheme scheme); // retires the `New` virtual (12 sites)
}
```

The typed variable node — note the **three read shapes**, which resolve the async‑ergonomics tension explicitly rather than forcing every getter to become async:

```csharp
public interface IVariableNode<T> : INode
{
    // (a) pure static — ZERO delegates, ZERO async. The value sits in the node
    //     and the built-in read path serves it. This is the overwhelming majority.
    IVariableNode<T> Value(T initialValue);

    // (b) synchronous getter — a GENUINE synchronous path
    //     (BaseVariableState.OnReadValue, verified at line 559), invoked directly
    //     on the sampling thread. This is NOT sync-over-async.
    IVariableNode<T> OnRead(Func<T> read);
    IVariableNode<T> OnRead(Func<ISystemContext, T> read);

    // (c) async getter — for I/O-backed values. Runs WITHOUT lock(this)
    //     (BaseVariableState.OnReadValueAsync, verified at line 578), so it may
    //     await a device/DB freely without tying up a thread-pool thread.
    IVariableNode<T> OnRead(Func<CancellationToken, ValueTask<T>> read);
    IVariableNode<T> OnRead(Func<ISystemContext, CancellationToken, ValueTask<T>> read);

    IVariableNode<T> OnWrite(Action<T> write);
    IVariableNode<T> OnWrite(Func<T, CancellationToken, ValueTask> write);

    IVariableNode<T> Writable(bool writable = true);
    IVariableNode<T> Units(string symbol, double low, double high);   // EU + EURange, folded in
}
```

`IObjectNode`, `IFolderNode`, `IMethodNode` are the same shape, narrowing to the callbacks that make sense for the node class (an `IObjectNode` has no `OnRead`; an `IMethodNode` has `OnCall` — see rung 2). Each returns itself so calls chain, and each exposes `Child(...)`/`Variable<T>(...)` to build a subtree without re‑resolving from the root.

**`INodeSourceContext` — deleting the service locator from the authoring surface:**

```csharp
public interface INodeSourceContext
{
    NamespaceTable      NamespaceUris { get; }
    ITelemetryContext   Telemetry     { get; }  // create ILogger via the source-gen [LoggerMessage] conventions
    TimeProvider        TimeProvider  { get; }
    ISystemContext      SystemContext { get; }
    // typed access to base-service ports the author is ALLOWED to use — each already a seam:
    IHistorianRegistry  Historians    { get; }
    IFileSystemProvider FileSystem    { get; }
    ISecretStore        Secrets       { get; }
}
```

**Invariants, ordering constraints and error modes that are part of this interface** (today these live as prose restated six times across `INodeManager.cs`; here they are structural and stated once):

- **Build runs once, at activation, single‑threaded.** After the delegate returns, the runtime *seals* the source; the dispatch dictionaries are populated once and read‑only thereafter, so **no synchronization primitive is ever exposed** and no lock appears on the surface (satisfies "never expose locks"). This mirrors `NodeManagerBuilder`'s existing `Seal()` semantics.
- **Ownership is by namespace** — the default `MasterNodeManager` partitioning. Cross‑namespace/id‑pattern ownership is a rung‑3/4 concern, not expressible here (stated so the author knows the boundary up front).
- **Wiring is last‑writer‑wins per node per category; double‑wiring the same category on the same node throws at build time**, surfacing author errors at startup rather than at first request (already the fluent contract per `INodeBuilder` remarks: *"Wiring the same node twice with the same callback category is an error and throws."*).
- **Node lookups resolve eagerly against the address space while the delegate runs**; a failed `Node(...)`/`Bind(...)` throws `ServiceResultException` (`BadNodeIdUnknown`, `BadBrowseNameDuplicated`, or `BadTypeMismatch`) immediately. The runtime path stays reflection‑free and AOT/trim safe.
- **The `Processed` flag, index‑aligned parallel accumulators, and `ref ContinuationPoint` append semantics do not appear.** The runtime owns request fan‑out; the author never sees them.

**What the level‑1 caller learns:** `Folder / Object / Variable<T> / Method` to create, `Value / OnRead / OnWrite` to back, `Writable / Units` to shape, `UseNodeIdScheme` if they mint runtime instances. That is the entire vocabulary for the common case.

##### Rung 2 — the same builder, one verb per capability

No new type to learn; you keep chaining on the node builders. These already exist as builder extension methods in `src/Opc.Ua.Server/Fluent` — I keep them and add the two the data shows are common (`OnCall` and monitored‑item lifecycle). All are AOT/trim safe (no reflection) and follow the return‑the‑same‑builder contract.

```csharp
// history (Part 11) — HistorianFluentExtensions
IVariableNode<T> Historize(byte historyAccessLevel = AccessLevels.HistoryRead | AccessLevels.HistoryWrite,
                           IHistorianProvider? provider = null, bool autoCapture = true);
IVariableNode<T> WithHistorian(IHistorianProvider provider);   // per-node provider binding

// alarms & conditions — AlarmBuilderExtensions
IAlarmBuilder<NonExclusiveLimitAlarmState> CreateLimitAlarm(string browseName);
IAlarmBuilder<ExclusiveLimitAlarmState>    CreateExclusiveLimitAlarm(string browseName);
IAlarmBuilder<OffNormalAlarmState>         CreateOffNormalAlarm(string browseName);
INodeBuilder Done<TState>();                                    // terminates the alarm sub-chain

// method dispatch — typed, replacing the (ISystemContext, MethodState, NodeId,
// ArrayOf<Variant> inputs, IList<Variant> outputs, ...) positional shape
IMethodNode OnCall(Func<CallRequest, CancellationToken, ValueTask<CallResult>> handler);
IMethodNode OnCall<TArgs, TResult>(Func<TArgs, CancellationToken, ValueTask<TResult>> handler); // generated

// event sources — typed Publish<TEvent> on notifier wrappers, EventSourceRegistry
INode Publish(Func<BaseObjectState, ISystemContext, CancellationToken, IAsyncEnumerable<BaseEventState>> src);

// push sampling — INodeBuilder.OnMonitoredItemCreated
IVariableNode<T> OnMonitoredItemCreated(MonitoredItemCreatedHandler handler);

// engineering units, simulation timers, dynamic child creation — existing extensions kept
```

Plus the **generated typed traversal** — `Configure(IBoilerNodeManagerBuilder builder)` — which the source generator already emits per model (`BoilerNodeManager.NodeManager.g.cs` calls both the untyped `Configure(INodeManagerBuilder)` and the typed `Configure(IBoilerNodeManagerBuilder)`). This is the strongest ergonomic asset in the current stack and I preserve it verbatim: each browse segment is a generated property returning the typed wrapper for the next node, so `builder.Boilers.Boiler__1.LCX001.Measurement.OnRead(...)` is fully type‑checked, IntelliSense surfaces every legal child, and a typo is a compile error rather than a startup `ServiceResultException`.

**What level 2 adds:** one verb per capability, or a NodeSet2 design plus the generated traversal for a large static model. **Climb trigger:** you need history, an alarm, a method, an event source, push sampling, or a large authored model. You never change base classes; you add a call.

##### Rung 3 — inject a provider (modelled exactly on `IHistorianProvider`)

This eliminates the current *cliff*. Today, browse‑on‑demand over a large external address space means subclassing `AsyncCustomNodeManager` (8,028 lines, 93 virtuals) and overriding:

- `GetManagerHandleAsync` — returns `ValueTask<object>` (the `object` leak, verified at `INodeManager.cs:902`);
- `BrowseAsync(OperationContext, ContinuationPoint, IList<ReferenceDescription> references, …)` — where *"The references parameter may already contain references when the method is called. The implementer must include these references when calculating whether a continuation point must be returned"* (verified at `INodeManager.cs:588`); and
- index‑aligned `Read`, honouring the `Processed` flag on every element.

I re‑cut the 15 opt‑in capability interfaces (`IReadAsyncNodeManager`, `IWriteAsyncNodeManager`, `ICallAsyncNodeManager`, `IBrowseAsyncNodeManager`, `IHistoryReadAsyncNodeManager`, … verified in `INodeManager.cs`) from *interfaces you implement on a subclassed base* into *providers you inject, with a typed handle*:

```csharp
namespace Opc.Ua.Server;

/// <summary>
/// Core: recognize an id (no I/O) and describe it. Two members — the same
/// progressive-disclosure shape as IHistorianProvider.
/// </summary>
public interface INodeProvider<THandle> where THandle : notnull
{
    ArrayOf<string> NamespaceUris { get; }

    /// <summary>
    /// Recognize the syntax of an id without blocking on the underlying system.
    /// Returns a typed handle — no object round-trip, no Processed flag.
    /// </summary>
    bool TryResolve(NodeId nodeId, out THandle handle);

    ValueTask<NodeMetadata?> DescribeAsync(THandle handle, CancellationToken ct);
}

// opt-in per service you actually support; unsupported services fall back automatically.
public interface IBrowseProvider<THandle>
{
    /// <summary>
    /// Return one page of children. The runtime owns paging: you receive a token
    /// and return the next token. You never append into a shared, pre-populated list.
    /// </summary>
    ValueTask<BrowsePage> BrowseAsync(
        THandle handle, in BrowseFilter filter, ContinuationToken token, CancellationToken ct);
}

public interface IValueProvider<THandle>
{
    ValueTask<DataValue>     ReadAsync(THandle handle, in ReadFilter filter, CancellationToken ct);
    ValueTask<ServiceResult> WriteAsync(THandle handle, DataValue value, CancellationToken ct);
}

public interface ICallProvider<THandle>
{
    ValueTask<CallResult> CallAsync(THandle handle, in CallRequest request, CancellationToken ct);
}
```

**Invariants and error modes here, made structural:**

- **Dispatch is first‑match‑wins.** The runtime router calls `TryResolve` on each provider; the first that returns `true` owns the node. That single fact *replaces the entire prose‑enforced `Processed` protocol* — the invariant is now carried by the type system (a resolved `THandle`), not by "each of N node managers must set a flag while `MasterNodeManager` fans out and relies on each honouring it."
- **A returned `BrowsePage` replaces the append accumulator.** You build and return your page; you never mutate a caller‑owned `IList<ReferenceDescription>` that may already hold references.
- **Capability is advertised by which interfaces you implement.** A provider that implements `IValueProvider` but not `ICallProvider` automatically yields `BadNotSupported` for Call — the exact "implement the umbrella, add a capability interface per feature" model that `IHistorianProvider` + `IHistorianDataProvider`/`IHistorianEventProvider`/… already prove in `src/Opc.Ua.Server/Historian`.
- **Per‑session behaviour is expressible** because `BrowseFilter`/`ReadFilter`/`CallRequest` carry the `OperationContext` (identity, session), so role‑based node visibility lives in the provider without any extra hook.

**What level 3 adds:** a 2‑member core plus one capability interface per service set you support. **Climb trigger:** your address space is too big to materialise, lives in an external system, is partitioned by something other than namespace, or is backed by replicated/HA state.

##### Rung 4 — the existing full surface, unchanged and un‑obsoleted

`IAsyncNodeManager`, `AsyncCustomNodeManager`, `CustomNodeManager2`, and the `INodeManager`/`INodeManager2`/`INodeManager3` chain remain **first‑class and are not marked `[Obsolete]`.** They are rung 4. You register one via the existing `IStandardServer.AddNodeManager(IAsyncNodeManagerFactory)` / `AddNodeManager(INodeManagerFactory)`. This is the labelled door: the docs describe it as "you are leaving the ergonomic surface on purpose," and nodes authored at rungs 1–3 in other sources continue to work alongside it. **What level 4 is:** the complete, untouched power surface, entered deliberately.

##### Registration — DI‑injectable with a direct‑construct fallback (both required)

```csharp
// DI — the no-class trivial form already exists (AddNodeManager(string, Action<INodeManagerBuilder>),
// verified at OpcUaServerBuilderExtensions.cs:863); I make its builder able to CREATE nodes:
services.AddOpcUa()
    .AddServer(o => { /* app name, uri, endpoints */ })
    .AddNodeSource("urn:acme:line1", b => { /* rung 1–2 */ });   // Action<INodeSourceBuilder>

// class form when the source has DI dependencies or state:
services.AddOpcUa().AddServer(o => …).AddNodeSource<TankSource>();      // TankSource : INodeSource

// rung 3 provider, injectable (its own ctor deps resolved from the container):
services.AddOpcUa().AddServer(o => …).AddNodeProvider<PlantNodeProvider>();

// Direct-construct fallback (no Generic Host):
IAsyncNodeManagerFactory factory = NodeSource.Factory("urn:acme:line1", b => { … });
server.AddNodeManager(factory);   // the existing IStandardServer entry point
```

`INodeSource` is the class form of rung 1:

```csharp
public interface INodeSource
{
    ArrayOf<string> NamespaceUris { get; }
    ValueTask ConfigureAsync(INodeSourceBuilder builder, CancellationToken ct = default);
}
```

---

#### 2. Usage example

##### (a) The trivial case — a device‑backed server, single file, no NodeSet2, no class

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Opc.Ua;
using Opc.Ua.Server;

var modbus = new ModbusClient("10.0.0.7");                       // the author's device

HostApplicationBuilder host = Host.CreateApplicationBuilder(args);
host.Services.AddOpcUa()
    .AddServer(o =>
    {
        o.ApplicationName = "Line1";
        o.ApplicationUri  = "urn:localhost:acme:Line1";
        o.ProductUri      = "uri:opcfoundation.org:Line1";
        o.AutoAcceptUntrustedCertificates = true;               // sample convenience only
        o.EndpointUrls.Add("opc.tcp://localhost:62541/Line1");
    })
    .AddNodeSource("urn:acme:line1", b =>
    {
        IObjectNode tank = b.Folder("Plant").Object("Tank1");

        tank.Variable<double>("Level")
            .OnRead(ct => modbus.ReadHoldingAsync(40001, ct))   // async, lock-free, device-backed
            .Units("m", 0, 5);

        tank.Variable<string>("Model")
            .Value("ACME-9000");                                // static: no delegate, no async at all

        tank.Variable<bool>("PumpOn")
            .OnRead(() => modbus.Coil(1))                       // synchronous getter — genuine sync path
            .OnWrite((v, ct) => modbus.WriteCoilAsync(1, v, ct))
            .Writable();
    });

await host.Build().RunAsync();
```

Everything a newcomer learns is on that page: `Folder / Object / Variable<T>`, `OnRead / OnWrite / Value`, `Units / Writable`. There is no `NodeState`, no `NodeId` factory, no `CreateAddressSpace`, no `IServerInternal`, no `lock`, and no `object`. The static `Model` variable pays **zero** async ceremony; the sync `PumpOn` getter runs on a genuine synchronous read path; only the genuinely I/O‑backed `Level` opts into the awaitable shape.

##### (b) One rung up — history + an alarm + a method (verbs added, nothing subclassed)

```csharp
.AddNodeSource("urn:acme:line1", b =>
{
    IObjectNode tank = b.Folder("Plant").Object("Tank1");

    IVariableNode<double> level = tank.Variable<double>("Level")
        .OnRead(ct => modbus.ReadHoldingAsync(40001, ct))
        .Units("m", 0, 5)
        .Historize();                          // Part 11 — in-memory historian auto-installed on first use

    tank.CreateLimitAlarm("LevelHigh")         // Alarms & Conditions — one verb
        .OfSource(level)
        .HighLimit(4.5)
        .Severity(700)
        .Done();

    tank.Method("Drain")
        .OnCall((in CallRequest r, CancellationToken ct) => modbus.DrainAsync(ct));  // typed dispatch
});
```

For a *large* static model the climb is instead: author `Tank.NodeSet2.xml`, mark the partial class `[NodeManager(NamespaceUri = "urn:acme:line1")]`, let the generator emit the typed traversal, and wire only the device‑backed leaves:

```csharp
[NodeManager(NamespaceUri = "urn:acme:line1")]
public partial class TankNodeManager
{
    partial void Configure(ITankNodeManagerBuilder b)
    {
        b.Plant.Tank1.Level.OnRead(ct => m_modbus.ReadHoldingAsync(40001, ct)).Historize();
        b.Plant.Tank1.Drain.OnCall(DrainAsync);
    }
}
```

Same verbs, type‑checked paths, no strings or `NodeId`s at the call site.

##### (c) The demanding case — browse‑on‑demand over a large external address space (rung 3)

```csharp
// 200k tags in a plant historian; never materialised into NodeState. Inject a provider.
public sealed class PlantNodeProvider :
    INodeProvider<TagRef>, IBrowseProvider<TagRef>, IValueProvider<TagRef>
{
    private readonly IPlantGateway m_plant;
    public PlantNodeProvider(IPlantGateway plant) => m_plant = plant;   // DI-resolved

    public ArrayOf<string> NamespaceUris => ["urn:acme:plant"];

    public bool TryResolve(NodeId nodeId, out TagRef handle)            // cheap, no I/O
        => TagRef.TryParse(nodeId, out handle);

    public ValueTask<NodeMetadata?> DescribeAsync(TagRef h, CancellationToken ct)
        => m_plant.DescribeAsync(h, ct);

    public async ValueTask<BrowsePage> BrowseAsync(
        TagRef h, in BrowseFilter f, ContinuationToken token, CancellationToken ct)
    {
        PlantPage page = await m_plant.ListChildrenAsync(h, token.Offset, pageSize: 500, ct);
        return BrowsePage.From(
            page.References,
            next: page.HasMore ? token.Advance(500) : ContinuationToken.None);
    }

    public ValueTask<DataValue> ReadAsync(TagRef h, in ReadFilter f, CancellationToken ct)
        => m_plant.ReadAsync(h, ct);

    public ValueTask<ServiceResult> WriteAsync(TagRef h, DataValue v, CancellationToken ct)
        => m_plant.WriteAsync(h, v, ct);
}

// registration:
services.AddOpcUa().AddServer(o => …).AddNodeProvider<PlantNodeProvider>();
```

Note what is **absent**: no continuation‑point struct threaded through `ref` parameters, no `Processed` flag, no index‑aligned `IList<DataValue>` / `IList<ServiceResult>` the caller pre‑sized, no `object` handle. `TagRef` is the author's own typed handle. The provider implements `IValueProvider` but *not* `ICallProvider`, so Call automatically returns `BadNotSupported` — the same "advertise only what you support" model as `IHistorianProvider`'s capability interfaces. Per‑session ACLs, if needed, read `f.OperationContext.Session` inside `BrowseAsync`/`ReadAsync`.

##### (d) The ergonomic cliff test — the user who needs something off‑ladder

**Scenario 1 — a cross‑cutting concern (audit/transform *every* Read across all namespaces).** Rungs 1–3 are per‑node; there is no per‑node verb for "every request." The author *would* hit a wall — but the ladder **redirects them to the correct seam instead of dropping them into 93 virtuals.** This is a server concern, and the healthy 9‑member `IStandardServer` already exposes the right hook (`OnRequestValidatedAsync` is one of its clean lifecycle overrides):

```csharp
public sealed class AuditingServer : StandardServer
{
    protected override ValueTask OnRequestValidatedAsync(RequestContext ctx, CancellationToken ct)
    {
        m_audit.Record(ctx);
        return base.OnRequestValidatedAsync(ctx, ct);
    }
}

services.AddOpcUa().AddServer<AuditingServer>(o => …);   // AddServer<TServer> sits next to AddServer(...)
```

That is a **step**: one override on the reference‑shape server interface, discoverable because `AddServer<TServer>()` is right beside `AddServer(...)`. A good ladder also tells you when you are standing at the wrong ladder.

**Scenario 2 — a genuine rung‑4 drop.** The author needs a manager that owns nodes by an **id pattern spanning namespaces** *and* wants to hook the publish pipeline's sampling‑group scheduling. Neither is expressible on rungs 1–3, so they descend, deliberately, to the full surface:

```csharp
public sealed class LegacyManager : AsyncCustomNodeManager   // full surface, on purpose
{
    public override IEnumerable<string> NamespaceUris => null;        // custom partitioning
    // override the id-resolution + sampling hooks you need; the router already
    // supports NamespaceUris == null via MasterNodeManager.GetManagerHandle fan-out.
}

server.AddNodeManager(new LegacyManagerFactory());
```

This is a **step, not a fall**, for three reasons: (i) it is a deliberate `new` + registration, not silent inheritance; (ii) the docs name it as rung 4 with its trade‑offs; (iii) rung‑1/2 nodes in *other* sources keep working next to it. The difference from today is that you arrive here **by choice for a real reason**, not because you needed one extra thing the veneer didn't cover and got the whole 93‑virtual surface by inheritance.

---

#### 3. What the implementation hides behind the seam

The runtime keeps a single **`internal sealed`** implementation — call it `NodeSourceRuntime : IAsyncNodeManager` — that the builder (rungs 1–2) and the providers (rung 3) feed. It is the deep module: a small authored interface sits in front of the entire existing node‑manager engine.

**Machinery that moves from interface to implementation (with line counts):**

- **`AsyncCustomNodeManager` — 8,028 lines, 59 public + 92 protected members, 93 virtual/abstract resolving to 80 distinct names.** Authors never see it. `NodeSourceRuntime` *contains and drives* it; the 93 virtuals become private policy.
- **`CustomNodeManager2` — 6,302 lines, 93 virtual/abstract.** Internal engine, not an authored base.
- **`MasterNodeManager` — 7,601 lines, 36 public methods.** Its request fan‑out and the `Processed`‑flag arbitration are hidden; the router now owns dispatch via `TryResolve`, so the flag protocol is deleted from every authored surface.
- **`IServerInternal` — 57 members (34 properties handing out 20+ subsystems, 12 `Set*` mutators), referenced across 200+ files; `ServerInternalData` is 1,355 lines.** Stays internal to the runtime; the authored surface sees the 6‑member `INodeSourceContext`.
- **`NodeState` — 5,824 lines, 155 public members, 65 virtual/abstract, 2 events, 18 public delegates (the sync/async duplicated pairs).** Reduced, for the common case, to `Value / OnRead / OnWrite / OnCall / Publish` on the typed node builders. The full delegate surface remains available to rung‑4 authors.
- **The 15 opt‑in capability node‑manager interfaces** (`IReadAsyncNodeManager`, `IBrowseAsyncNodeManager`, …) collapse into the injectable `INodeProvider`/`IBrowseProvider`/`IValueProvider`/`ICallProvider` family.

**Machinery that stays visible (correctly):** `NodeId`, `Variant`, `DataValue`, `QualifiedName`, `ArrayOf<T>`, `ByteString`, `ServiceResult`/`StatusCode`, `OperationContext`, `NodeMetadata` — the domain vocabulary, unavoidable and shared with the rest of the stack. Hiding it would be dishonest depth (you cannot serve OPC UA without `NodeId` and `Variant`).

**The deletion test, applied explicitly to three removals:**

1. **`FluentNodeManagerBase` as a public base class** (`Fluent/FluentNodeManagerBase.cs:57`, `public abstract class FluentNodeManagerBase : AsyncCustomNodeManager`). Delete it as an *extension point*. Complexity does **not** reappear across callers: the 90% used only `CreateFluentBuilder` + `Configure` + the two registries (`EventSourceRegistry`, `SimulationRegistry`), which the runtime now performs for them. From the author's vantage the class was a **pass‑through** onto `AsyncCustomNodeManager` — it forwarded lifecycle to the base and attached two registries. A pass‑through whose deletion makes complexity vanish was a shallow module; removing it from the surface removes the cliff without removing any capability.
2. **`object? GetManagerHandle` / `GetManagerHandleAsync` (`INodeManager.cs:902`) plus the `Processed`‑flag protocol** (restated six times in `INodeManager.cs`). Delete both from the authored interface. Complexity does **not** reappear for the few rung‑3 providers, because the router now arbitrates with a typed `THandle` and first‑match‑wins. This was complexity earning *negative* keep — a footgun replicated at every implementer and enforced only by prose; concentrating it in one router is a pure **locality** win (all id arbitration in one place) and a pure **leverage** win (providers get correct dispatch for free).
3. **`IServerInternal` on the authoring surface.** Delete it from what a node source sees. Complexity does **not** reappear: callers referenced 3–4 of its 57 members (namespace table, telemetry, system context, occasionally a base‑service port). Those become the 6‑member `INodeSourceContext`. The locator stays internal to the runtime; its 200+ references are all framework‑internal and unaffected. This is the textbook shallow service locator — deleting it from the interface removes the newcomer's worst first encounter with zero loss of capability.

**A non‑deletion, to show the test cuts both ways (earns its keep):** `MasterNodeManager` routing. Delete it and *every* rung‑3 provider re‑implements fan‑out, id arbitration, and cross‑manager reference stitching — complexity reappears once per caller (×N). It is not a pass‑through; it is deep behaviour behind a small seam. Keep it and *deepen* it (own the dispatch, stop leaking the protocol).

**Why the simple surface is a deep module and not a shallow facade — and how tests stay honest.** The failure mode the brief names is a facade people test *past*. Two structural guarantees prevent that here:

- `NodeSourceRuntime` is **`internal sealed`.** There is no public base to subclass, so neither an author nor a test can reach the 93 virtuals *through* a rung‑1/2 source. The only routes past rung 2 are the **front‑door** seams — implement a provider (rung 3) or a node manager (rung 4). Contrast the current design, where a test of a fluent manager can reach every virtual because `FluentNodeManagerBase : AsyncCustomNodeManager`.
- **Caller surface == test surface.** A rung‑1/2 test mounts the `INodeSource` into an in‑memory server harness and drives **real Read/Browse/Call service‑set calls** at the same seam production uses — it never constructs an `IServerInternal`, never touches a virtual. A rung‑3 test constructs the provider and drives it through `TryResolve`/`BrowseAsync`/`ReadAsync` directly, or through the router with an `InMemoryNodeProvider` peer. Because the thing a test exercises is exactly the thing the router calls in production, the urge to "test past" the interface never arises — which is the operational definition of the module being the right shape. **Leverage** is high (the whole node‑manager engine behind ~6 verbs); **locality** is high (dispatch, id arbitration and paging all concentrate in the runtime, so a bug in any of them has exactly one home).

---

#### 4. Dependency strategy and adapters

Classifying every dependency this design touches by the four categories, stating which seams get ports vs stay internal, and naming two adapters per port:

**Category 1 — In‑process (pure computation / in‑memory state; no I/O; always deepenable; no port).**
The node graph (`NodeState`), the builder, request routing/dispatch, the publish pipeline, and diagnostics. These have **internal** seams only (private, used by their own tests). No adapter at the external interface. The builder and the router are the deep in‑process modules of this design, and they are exactly where the removed complexity now lives.

**Category 2 — Local‑substitutable (a local test stand‑in exists; the seam is internal; no port at the external interface).**
Continuation‑point store, subscription store, monitored‑item queue factory — already `IContinuationPointStore`, `ISubscriptionStore`, `IMonitoredItemQueueFactory`, each with an in‑memory and a durable implementation. **Reuse as‑is; invent nothing.** Rung‑3 paging (`ContinuationToken` → `BrowsePage`) is served by the existing continuation‑point store *behind* the router, so the provider author never sees it. These stay internal precisely because a single process can always substitute a local stand‑in — no port is warranted.

**Category 3 — Remote‑but‑owned (define a port; in‑memory adapter for tests, network adapter for production).**
Distributed / HA address‑space state. The port already exists: **`ILocalAddressSpace`** (7 members, `NodeManager/ILocalAddressSpace.cs`). Its two adapters justify the seam:
- **Adapter A (test / local):** the dictionary‑backed implementation the interface doc‑comment explicitly calls out (*"tests use a dictionary-backed implementation"*).
- **Adapter B (production):** the redundancy/network synchroniser (`AddOrUpdateRangeAsync` bulk‑hydration path for a standby, `NodeAdded`/`NodeRemoved` for outbound capture).

**Reuse this seam.** A rung‑3 `INodeProvider` can be backed by an HA store transparently: the author writes the same provider whether the state is local or replicated, because the runtime owns the sync seam. Two real adapters ⇒ a real seam, not a hypothetical one.

**Category 4 — True external (inject a port; mock in tests).**
- **Historian** — `IHistorianProvider` (umbrella, 2 members) + the opt‑in `IHistorianDataProvider` / `IHistorianModifiedProvider` / `IHistorianAtTimeProvider` / `IHistorianProcessedProvider` / `IHistorianAnnotationProvider` / `IHistorianEventProvider` capabilities, routed by `HistorianDispatcher`. Two adapters: **`InMemoryHistorianProvider`** (`Historian/InMemory`) for tests/quickstart and the **user's time‑series adapter** for production. `.Historize()` and `.WithHistorian(...)` route here. **Reuse.**
- **File system / secrets** — `IFileSystemProvider` (11 members), `ISecretStore`. Exposed to authors through `INodeSourceContext`. **Reuse.**
- **The node/browse source** — the **one seam I re‑shape.** It is *not a new port*: it is the existing browse/read/call decomposition (the 15 capability interfaces), moved from "opt‑in interfaces on a subclassed base" to "providers you inject," with a typed `THandle` replacing `object`. It clears the two‑adapter bar (one adapter would make it merely hypothetical): **Adapter A** = `InMemoryNodeProvider` (tests and small dynamic sets), **Adapter B** = a real external adapter such as `PlantNodeProvider` over a gateway/SQL/OPC‑client. Two adapters ⇒ a real seam.

**Net:** every external dependency already sits behind a port, so I reuse `IHistorianProvider`, `ILocalAddressSpace`, `IFileSystemProvider`, `ISecretStore`, `IContinuationPointStore`, and `ISubscriptionStore` unchanged. The only *reshaped* seam (`INodeProvider` + capabilities) is a re‑cut of an existing one (the 15 capability node‑manager interfaces), held to the two‑adapter bar. I invent no brand‑new external port.

---

#### 5. Trade‑offs

**Where leverage is high.** The trivial and one‑rung cases: ~6 verbs stand in front of the 8,028‑line manager, the 5,824‑line `NodeState`, and the 57‑member locator. The measured common‑6 overrides map cleanly onto builder knobs, which is the proof the ladder targets the *actual* common case rather than a guessed one:

| Common‑6 override (override sites) | New home on the ladder |
|---|---|
| `CreateAddressSpaceAsync` (21) | the builder delegate **is** this |
| `Dispose` (14) | runtime owns dispose; `IAsyncDisposable` backings disposed for you |
| `New` / NodeId factory (12) | `builder.UseNodeIdScheme(...)` (rung 1) |
| `LoadPredefinedNodesAsync` (11) | generator / `AddRuntimeNodeSet` / builder node creation |
| `DeleteAddressSpaceAsync` (8) | runtime owns |
| `AddBehaviourToPredefinedNodeAsync` (5) | `.OnRead / .OnWrite / .OnCall` wiring |

Every member the data shows is *actually* overridden by the common caller becomes a rung‑1/2 knob. That is the whole thesis, demonstrated against the measurements.

**Where leverage is thin (honest).** A rung‑3 provider author still learns OPC UA browse and continuation *concepts* — `BrowsePage`, filters, capability advertisement. The interface is far smaller and typed, but the domain is not eliminated. That is depth applied correctly: I hid the *mechanism* (paging state, `Processed` arbitration, `object` handles), not the *domain* (browse still means browse). Likewise, a static value truly needs nothing (`Value(T)`), but a historized, alarmed, method‑bearing object legitimately requires several verbs — the surface grows with genuine capability, not with accidental ceremony.

**What gets harder / what a power user loses.** A power user who *liked* subclassing `CustomNodeManager2` and overriding an arbitrary virtual mid‑pipeline must now either (a) express the need as a provider capability, (b) use a server‑level hook on `IStandardServer`, or (c) drop to rung 4 explicitly. Rung 4 preserves 100% of today's power, but the *ambient* extensibility of "override anything, anywhere" is deliberately gone from rungs 1–3 — and that ambient power was precisely the 93‑virtual cliff, so its removal from the simple surface is the point, not a regret. A narrower loss: cross‑node atomic operations and bespoke sampling‑group scheduling have no rung‑1–3 verb and require rung 4.

**The async‑ergonomics tension, addressed head‑on.** TAP‑only would ordinarily force a "return this value" callback to become `Func<CancellationToken, ValueTask<T>>`. The design defuses this with the **three read shapes**: `Value(T)` (no delegate, no async at all — served by the built‑in read path), `OnRead(Func<T>)` (a *genuine* synchronous path via `BaseVariableState.OnReadValue`, verified at line 559, invoked directly on the sampling thread), and `OnRead(Func<CancellationToken, ValueTask<T>>)` (lock‑free async via `OnReadValueAsync`, verified at line 578). The overwhelming majority pay zero async ceremony; only genuinely I/O‑backed reads opt into the awaitable shape. There is no `GetAwaiter().GetResult()`, `.Wait()`, or `.Result` anywhere on the path — the sync overload is a real synchronous read, not sync‑over‑async, which is why both handler families already coexist on `BaseVariableState`.

**One residual risk, flagged.** `INodeProvider<THandle>` is generic; the router holds heterogeneous providers behind an internal, non‑generic erased adapter. That erasure boxes `THandle` at the router boundary only (once per resolve, not per value read) — a deliberate, measured cost to keep the *authored* interface `object`‑free and AOT‑clean. If profiling ever shows that boundary hot, the erased adapter can cache a typed dispatcher per provider without touching the authored interface — which is itself evidence the seam is in the right place.

**Migration path — additive; `[Obsolete]` marks only the *replaced*, and removes nothing.**

- **The full node‑manager surface stays.** `INodeManager` / `INodeManager2` / `INodeManager3`, `IAsyncNodeManager`, `AsyncCustomNodeManager`, `CustomNodeManager2` are **unchanged and not obsoleted.** They are rung 4. All **33 existing subclasses** across `src` / `samples` / `tests` compile and run untouched, as does the one class that implements `INodeManager` directly (`SampleNodeManager`). The full OPC UA service set, companion specs (DI, GDS, PubSub, Robotics, ISA95, WoT), NodeSet2 runtime loading, and HA/distributed servers all continue to work because none of their seams are removed.
- **The Fluent layer is preserved, not deleted.** `NodeManagerBuilder`, `INodeManagerBuilder`, `IVariableBuilder<T>`, `INodeBuilder`, `EventSourceRegistry`, `SimulationRegistry`, and all the `*BuilderExtensions` become the *implementation* of rungs 1–2 — I extend `INodeManagerBuilder` additively with the creational verbs (`Variable<T>` that mints, `Folder`, `Object`, `Method`, `UseNodeIdScheme`) and keep every existing extension. Only one thing changes role: **`FluentNodeManagerBase`'s advertisement as a public base to derive from** gets `[Obsolete("Author an INodeSource or inject an INodeProvider; see NodeManagers.md")]`. It survives as the *internal* implementation vehicle, so existing derivers keep compiling while new code is steered to the front door.
- **The source generator is re‑pointed, keeping its best output.** It currently emits `BoilerNodeManager : FluentNodeManagerBase` + `BoilerNodeManagerFactory : IAsyncNodeManagerFactory` + the typed `Configure(IBoilerNodeManagerBuilder)` traversal. It re‑points to emit an `INodeSource` (or an `internal sealed` runtime‑backed manager) plus the **same typed‑traversal partial**, which is the strongest ergonomic asset and is preserved verbatim. The `[NodeManager]` attribute opt‑in and the generated identifier tables are unchanged.
- **Mechanical migration for the common case.** A subclass that overrides only the common‑6 becomes an `INodeSource` whose `ConfigureAsync` drives the builder: `CreateAddressSpaceAsync` → the delegate body, `New` → `UseNodeIdScheme`, `LoadPredefinedNodesAsync` → generator or `AddRuntimeNodeSet`, `Dispose`/`DeleteAddressSpaceAsync` → runtime‑owned, `AddBehaviourToPredefinedNodeAsync` → `.OnRead/.OnWrite/.OnCall`. The version‑suffixed `INodeManager2/3` chain and the 15 capability interfaces remain for rung‑4 authors but are documented as superseded by the provider model for new work.
- **The diagnostics lock leak stays gone.** No `object`‑typed sync‑root is exposed anywhere on the new surface. The build‑once‑then‑seal model means the authored interface never hands out a lock, and the redesigned surface gives samples no reason to `lock(server…)` — retiring the pattern the `ConsoleReferenceServer` sample currently teaches.

---

## Design 4 — State-Placement (ports and adapters)

**Design constraint given:** *Design around ports and adapters, driven by the distributed / high-availability case. Identify precisely which state is local-ephemeral, which must transfer, which must replicate, which must persist. Be rigorous about the one-adapter rule.*

> Reproduced verbatim as delivered by the designer. Section numbering is the designer's own.

### Ports & Adapters for the OPC UA Server — a State‑Placement Design

**Thesis.** This codebase has *already grown* most of the ports it needs (`ISharedKeyValueStore`, `ILeaderElection`, `IRecordProtector`, `ILocalAddressSpace`, `INodeStateStore`, `ISubscriptionStore`, `IContinuationPointStore`, `ISharedSessionStore`). But they are **bolted onto the side**: the `Opc.Ua.Redundancy.Server` package reaches *into* the core through `IServerStartupTask` + `IServerInternal.Set*` + duck‑typed `ILocalAddressSpaceSource` sniffing, because the core still centres on a **mutable service‑locator** (`IServerInternal`, 57 members) and a **mutable data+behaviour graph** (`NodeState`, 5,824 lines). My design does not invent new ports. It **promotes the existing seams to first‑class, DI‑injected state ports keyed by a four‑category state taxonomy, deletes the service locator and the `Set*` two‑phase construction, and splits `NodeState`'s data from its behaviour** so replication has a clean, reflection‑free payload. The differentiator is *where each byte of state lives* and *seam discipline*: I keep only the seams that have two real adapters, and I say plainly which "stores" are deep modules over `ISharedKeyValueStore` rather than substitution ports.

---

#### 1. Interface

##### 1.1 The state taxonomy (the organizing principle)

Every port below is justified by exactly one row of this taxonomy. The taxonomy *is* the design.

| Category | Meaning | Consistency | Dep. category | Port? |
|---|---|---|---|---|
| **A — Local & ephemeral** | Per‑request/per‑channel; regenerated on restart; may legitimately differ per replica | node‑local | 1 (in‑process) | **No port** |
| **B — Transferable** | Session/subscription state a client needs to survive failover | eventual mirror (except nonce = linearizable) | 3 (remote‑but‑owned) | **Port** |
| **C — Replicable** | Address‑space topology + values; identical NodeIds across the set | eventual (single‑writer or CRDT) | 3 | **Port** |
| **D — Durable** | Survives full‑set restart: durable subs, trust lists, secrets, roles, users | mixed (local file → linearizable) | 3/4 | **Port** |
| **Foundation** | The substrate B/C/D write through | pluggable | 3/4 | **The real seams** |

##### 1.2 Replacing `IServerInternal` — split the locator; freeze the graph

`IServerInternal` conflates three things: (i) immutable local facts, (ii) 20+ subsystem managers, (iii) 12 `Set*` mutators enabling two‑phase construction. I split them.

**(i) `IServerContext` — Category A only. Read‑only, immutable after bind, constructor‑injected everywhere a component needs ambient facts. No `Set*`. No `object`. No exposed locks.**

```csharp
namespace Opc.Ua.Server;

/// <summary>
/// Immutable, read-only ambient server facts (Category A). Replaces the read side
/// of IServerInternal. Frozen after the single startup bind phase.
/// </summary>
public interface IServerContext
{
    IServiceMessageContext MessageContext { get; }
    NamespaceTable NamespaceUris { get; }
    StringTable ServerUris { get; }
    IEncodeableFactory Factory { get; }
    ITelemetryContext Telemetry { get; }
    ServerSystemContext DefaultSystemContext { get; }

    /// <summary>Read-only type tree; populated during bind, frozen afterwards.</summary>
    ITypeTable TypeTree { get; }

    ServerState CurrentState { get; }

    /// <summary>
    /// The ONLY state mutation. Linearizable and audited. Replaces the mutable
    /// <c>CurrentState { set; }</c> and the obsolete <c>Status</c> property.
    /// </summary>
    ValueTask<ServerState> TransitionStateAsync(
        ServerState target, LocalizedText reason, CancellationToken ct = default);

    /// <summary>
    /// Replaces <c>object DiagnosticsLock</c> / <c>DiagnosticsWriteLock</c>. Returns a
    /// consistent snapshot; the lock is a private <see cref="System.Threading.Lock"/>.
    /// </summary>
    ValueTask<ServerDiagnosticsSummaryDataType> GetServerDiagnosticsAsync(CancellationToken ct = default);
}
```

**(ii) Subsystem managers** (`ISessionManager`, `ISubscriptionManager`, `IMasterNodeManager`, `EventManager`, …) are **resolved from DI by the components that need them**, not handed out by a locator. There is deliberately **no `IServerStatePorts` god‑bundle** — that would just rename the locator. A component's interface declares its dependencies:

```csharp
public sealed class SessionManager(
    IServerContext context,
    ISharedSessionStore sessions,        // Category B
    ISingleUseNonceRegistry nonces,      // Category B (linearizable)
    IContinuationPointStore continuations // Category B
) : ISessionManager { /* ... */ }
```

**(iii) The 12 `Set*` mutators are deleted.** They are replaced by a **single ordered bind phase** — the existing `IServerStartupTask` pipeline — after which the object graph is **frozen**. The bind hook itself stops leaking the locator:

```csharp
// Before: OnServerStartedAsync(IServerInternal server, ...)  // hands out the whole locator
// After:
public interface IServerStartupTask
{
    ValueTask OnServerStartedAsync(
        IServerContext context, IServiceProvider services, CancellationToken ct = default);
}
```

Ordering/error contract: tasks run in DI registration order; a task may `services.GetRequiredService<T>()` any port; **any attempt to bind a port after `ServerState.Running` throws `BadInvalidState`** (replaces silent re‑entrant `SetSessionManager`).

##### 1.3 The address‑space data/behaviour split (the crux)

`NodeState` mixes **data** (topology, references, attribute values — serializable) with **18 behaviour delegates** (interception, browsers, validators, method handlers — *code, unserialisable*). Replication must carry only data. The split already exists implicitly in `NodeStateSerializer` (4‑byte `NodeClass` + `SaveAsBinary`, behaviour "re‑attached by the owning node manager"). I make it a **first‑class seam**.

```csharp
namespace Opc.Ua.Server;

/// <summary>
/// DATA seam (Category C). The replicable node graph. Already exists (7 members,
/// two adapters). Reads are SYNCHRONOUS (local serving cache); writes are async
/// (write-behind replication). Unchanged from today's ILocalAddressSpace.
/// </summary>
public interface ILocalAddressSpace { /* TryGetNode (sync) + AddOrUpdate/RemoveAsync + events */ }

/// <summary>
/// BEHAVIOUR seam (Category A, node-local, NEVER replicated). The 18 NodeState
/// delegates collapse to one small typed provider that the owning node manager
/// re-attaches locally after hydration. Values, not code, cross the wire.
/// </summary>
public interface INodeBehavior
{
    ValueTask<AttributeReadResult>  OnReadValueAsync (ISystemContext ctx, in ReadValueId  id,    CancellationToken ct = default);
    ValueTask<AttributeWriteResult> OnWriteValueAsync(ISystemContext ctx, in WriteValue   value, CancellationToken ct = default);
    ValueTask<CallMethodResult>     OnCallAsync      (ISystemContext ctx, in CallMethodRequest r, CancellationToken ct = default);
    NodeBrowser CreateBrowser(ISystemContext ctx, ContinuationPoint? cp);
}

/// <summary>
/// Implemented by a node manager (and by the source generator's output). After a
/// standby hydrates the DATA graph from INodeStateStore, the server calls this to
/// re-bind local behaviour by NodeId. Behaviour is deterministic from the model +
/// the manager's code, so every replica reconstructs identical behaviour.
/// </summary>
public interface INodeBehaviorSource
{
    bool TryGetBehavior(NodeId nodeId, [NotNullWhen(true)] out INodeBehavior? behavior);
}
```

**Invariant:** the serialized `IStoredNode.Payload` (Category C) contains **no delegate, no `object`, no `System.Threading.Lock`**. Behaviour is a pure function of `(model, manager code)` reconstructed on each replica — so a value written on the writer, mirrored as a `DataValue`, and re‑served on a standby is identical, while the *validation/interception* that produced it runs locally on whichever replica is the writer.

##### 1.4 Killing the `object` leaks and the `Processed` protocol

**Opaque handle → portable value.** `GetManagerHandleAsync : ValueTask<object>` cannot survive a failover. Replace with a value:

```csharp
public readonly record struct NodeManagerHandle
{
    public NodeId NodeId { get; init; }
    public int OwningNamespaceIndex { get; init; }   // the partition, portable across replicas
    // NO in-process pointer. Valid on any replica hosting the same namespace partition.
}
ValueTask<NodeManagerHandle?> GetManagerHandleAsync(NodeId nodeId, CancellationToken ct = default);
```

**This also deletes the `Processed`‑flag / index‑aligned‑accumulator protocol.** Today `MasterNodeManager` fans a shared mutable `IList<DataValue>`/`IList<ServiceResult>` out to *every* manager, each cooperatively skipping items whose `Processed` bit is set — cooperative multiplexing that only works because all managers share one in‑process array. With portable handles, `MasterNodeManager` resolves each `ReadValueId` to its single owning `NodeManagerHandle` (the routing table is already `NodeManagerRoutingTable : IReadOnlyList<IAsyncNodeManager>`) and dispatches a **partition** to exactly that manager, which returns its **own** `ArrayOf<DataValue>`. No shared array, no cooperative flag — and the partition composes across a network.

**Untyped continuation → typed, AOT‑serialisable.** `object? RestoreHistoryContinuationPoint` / `SaveHistoryContinuationPoint(Guid, object)` become:

```csharp
public interface IHistoryContinuationState : IEncodeable { }   // AOT-safe via source-gen encoders

ValueTask<IHistoryContinuationState?> RestoreHistoryContinuationPointAsync(ByteString id, CancellationToken ct = default);
ValueTask SaveHistoryContinuationPointAsync(Guid id, IHistoryContinuationState state, CancellationToken ct = default);
```

A node manager that can serialise its continuation now transfers it through `IContinuationPointStore`; generic managers keep the existing best‑effort `ContinuationPointEnvelope` (client re‑issues on `BadContinuationPointInvalid`, permitted by Part 4 §6.6.2.2).

**Diagnostics locks → gone.** `object DiagnosticsLock` / `DiagnosticsWriteLock` are **removed from `IServerInternal`, `ISession`, `ISubscription`** (the 88 `lock` sites become private `System.Threading.Lock`). Consumers use `GetServerDiagnosticsAsync` / `GetSessionDiagnosticsAsync` (snapshot). An exposed in‑process lock is a single‑node assumption baked into the interface; it cannot cross a replica and must not exist at the seam.

##### 1.5 Consistency contract (part of the interface)

| Operation | Guarantee | Backing |
|---|---|---|
| `ILocalAddressSpace.TryGetNode`, local value read/write | **node‑local**, synchronous, zero‑alloc | in‑process cache |
| `INodeStateStore` topology/value mirror; `ISharedSessionStore`; subscription/MI/retransmission mirror | **eventually consistent**, write‑behind | `ISharedKeyValueStore` / CRDT |
| `ISingleUseNonceRegistry.ConsumeAsync`; writer/leader election; durable‑subscription ownership | **linearizable** (CAS) | Raft strong keyspace |
| Continuation‑point envelope | **best‑effort** (may be lost) | mirror |

**Not every state port is async.** The **local serving read path stays synchronous** (`TryGetNode`, node‑manager `Read`), so a node read remains a dictionary lookup with no `await` and no allocation. Only **writes, hydration, mirroring, and nonce CAS** are async, and mirroring runs off the publish path via coalesced background drains (as `SharedKeyValueMonitoredItemQueueFactory` already does).

---

#### 2. Usage example

##### (a) Single‑node — in‑memory adapters

```csharp
services.AddOpcUa()
    .AddServer(server => server.ApplicationUri = "urn:plant:line-a")
    .AddNodeManager<BoilerNodeManagerFactory>();   // authoring code — see below
// No Use* calls. DI supplies the in-memory adapters as the direct-construct fallback:
//   ISharedKeyValueStore   -> InMemorySharedKeyValueStore
//   ILeaderElection        -> StaticLeaderElection (always writer)
//   ISubscriptionStore     -> in-memory (no durable persistence)
//   ILocalAddressSpace     -> PredefinedNodesAddressSpace (per node manager)
```

##### (b) Clustered — distributed adapters, **identical authoring code**

```csharp
services.AddOpcUa()
    .AddServer(server => server.ApplicationUri = "urn:plant:line-a")
    .AddNodeManager<BoilerNodeManagerFactory>()          // <-- byte-for-byte identical to (a)
    .UseRedundancyConsistency(c => c.Mode = ConsistencyMode.Strong)  // Raft strong keyspaces
    .UseDistributedAddressSpace(o => o.NodeId = Environment.MachineName)  // Category C
    .UseDistributedSessions(o => o.EnableFastReconnect = true)           // Category B
    .UseDistributedSubscriptionMirroring()                              // Category B
    .AddServerRedundancy(o => o.Mode = RedundancySupport.HotAndMirrored)
    .AddServerServiceLevel(new LeaderServiceLevelProvider(election, RedundancySupport.HotAndMirrored));
```

The **authoring surface never changes** — `BoilerNodeManagerFactory` and its node manager are written once:

```csharp
public sealed class BoilerNodeManager : ManagedNodeManager, INodeBehaviorSource
{
    protected override async ValueTask CreateAddressSpaceAsync(CancellationToken ct)
    {
        // DATA: create nodes as always. Replication captures these via ILocalAddressSpace.
        _pressure = CreateVariable(Objects.Boiler, "Pressure", DataTypeIds.Double);
    }

    // BEHAVIOUR (Category A) — re-attached locally on every replica; never serialized.
    public bool TryGetBehavior(NodeId nodeId, out INodeBehavior? behavior)
    {
        if (nodeId == _pressure.NodeId) { behavior = new PressureBehavior(_sensor); return true; }
        behavior = null; return false;
    }
}
```

The single‑node build re‑attaches behaviour on the one process; the clustered build re‑attaches the **same** behaviour on each standby after it hydrates the data graph. Same code, two adapter sets.

##### (c) Failover — what state moves, through which port

Replica 1 (writer, active session S) crashes. Replica 2 (standby, already hydrated) is promoted:

| State | Category | Port it travels through | Mechanism on promotion |
|---|---|---|---|
| Address space topology + values | C | `INodeStateStore` (+`INodeStateSnapshotStore`) via `IAddressSpaceSynchronizer` | Already resident (snapshot + delta‑log hydration); R2 flips writer role via `ILeaderElection` |
| Node **behaviour** | A | *(none)* | `INodeBehaviorSource.TryGetBehavior` re‑binds locally — never crossed the wire |
| Session S (token, nonce, identity, cert chain) | B | `ISharedSessionStore` / `SharedSessionEntry` | Client re‑`ActivateSession` with same `AuthenticationToken`; **`ISingleUseNonceRegistry.ConsumeAsync` (linearizable)** guarantees the mirrored nonce is spent once |
| Subscriptions + monitored‑item queues + retransmission | B | `ISubscriptionStore` / `ISubscriptionRetransmissionStore` / `IMonitoredItemQueueFactory` | Definitions + queued‑but‑unpublished notifications restored; `Republish` continues |
| Browse/HistoryRead continuation | B | `IContinuationPointStore` (best‑effort) | Restored if typed; else client re‑issues on `BadContinuationPointInvalid` |
| Durable subscriptions, trust lists, roles | D | `ISubscriptionStore`(durable) / `SharedKeyValueCertificateStore` / `IRoleManager` | Read from shared/durable store |

The client (`ManagedSession` with `WithServerRedundancy()`) sees a transparent reconnect. No authoring code participated in failover.

---

#### 3. What the implementation hides behind the seam

##### State inventory (every category of server state)

| State | Dep. cat. | Port | Why (one‑adapter check) |
|---|---|---|---|
| `OperationContext`, `SecureChannel`, transport listeners | 1 | none | Per‑request; regenerated. Pass‑through. |
| `RequestManager`, `ResourceManager`, `AggregateManager`, `ModellingRulesManager`, `EventManager`, `ConformanceUnitsManager` | 1 | none | Pure in‑process compute/state. Merge as deep modules. |
| In‑process `NodeState` serving cache | 1 | none (behind `ILocalAddressSpace` for capture) | Local materialised view; not itself a port. |
| **Node behaviour** (18 delegates, method handlers) | 1 | `INodeBehavior`/`INodeBehaviorSource` (internal seam) | Node‑local, deterministic; two adapters = hand‑written + source‑generated. |
| Per‑replica diagnostics counters | 1 | none | Spec permits divergence; snapshot via `GetServerDiagnosticsAsync`. |
| Address‑space topology + values | 3 | `INodeStateStore`(+snapshot) / `IAddressSpaceSynchronizer` / `ILocalAddressSpace` | See §4 — real seams at synchronizer + KV. |
| Session context + nonce | 3 | `ISharedSessionStore`, **`ISingleUseNonceRegistry`** | Transfer; nonce linearizable. |
| Subscriptions / MI queues / retransmission | 3 | `ISubscriptionStore`, `ISubscriptionRetransmissionStore`, `IMonitoredItemQueueFactory` | Two real adapters each. |
| Continuation points | 3 | `IContinuationPointStore` | Best‑effort; one adapter (see §4). |
| Durable subscriptions | 3 | `ISubscriptionStore` (durable) | File + shared‑KV adapters. |
| Trust lists / CRLs | 4 | `ICertificateStore` / `SharedKeyValueCertificateStore` | Directory + shared‑KV adapters. |
| Secrets / record‑protection keys | 4 | secret manager + `IRecordProtector` | Null + AES‑CBC‑HMAC + key‑ring. |
| Role assignments (Part 18) | 3/4 | `IRoleManager` | One prod adapter (see §4). |
| User management (Part 18) | 3/4 | `IUserManagement` | Injected, nullable. |
| **Substrate** | 3/4 | **`ISharedKeyValueStore`, `ILeaderElection`, `IRecordProtector`** | The real swap seams. |

##### Machinery that moves from interface to implementation

- **`AsyncCustomNodeManager` — 8,028 lines, 93 virtual/abstract (80 distinct), of which 55 never overridden** and only 25 distinct names ever overridden. These 55 non‑extension‑point virtuals move *behind* `ManagedNodeManager`; the interface exposes the ~6 real extension points (`CreateAddressSpaceAsync`, `LoadPredefinedNodesAsync`, `AddBehaviourToPredefinedNodeAsync`, `New`, `DeleteAddressSpaceAsync`, `Dispose`).
- **`NodeState`'s 18 delegates + 2 events** move behind `INodeBehavior`; the serialized payload (`SaveAsBinary`, line 1048) stays, the delegates leave the graph.
- **`IServerInternal`'s 12 `Set*` mutators + `ServerInternalData` two‑phase wiring (1,355 lines)** move behind the frozen bind phase.
- **88 `lock` statements / 7 files + `Subscription.DiagnosticsLock => Diagnostics`** move behind `System.Threading.Lock`; snapshots surface via async getters.

##### Deletion test (applied)

1. **Delete `IServerInternal.Set*` (12 methods) + two‑phase construction.** Complexity **vanishes**: single‑node wires once through DI; the Redundancy library stops calling `SetSubscriptionStore`/`SetSessionManager`; the "`SetNodeManager` silently populates three more properties" and "`SetSessionManager` unhooks prior handlers so it can be called twice" hazards disappear. Pure indirection removed → **delete**.
2. **Delete `object DiagnosticsLock`/`DiagnosticsWriteLock` (2 members).** Complexity **vanishes** at the interface: no caller needs the lock object; the `DiagnosticsWriteLock` getter that calls `ForceDiagnosticsScan()` *outside* the lock it returns is a latent bug that cannot be expressed once the lock is private. **Delete.**
3. **Delete `INodeStateStore` *as a substitution port*** — keep it as a *module*. If deleted entirely, its 867 lines of framing/sequencing/snapshot logic **reappear** inside `AddressSpaceSynchronizer` → it earns its keep as a **deep module** (8 members over 867 lines). But as a *swappable port* it has **one** production adapter (`InMemoryNodeStateStore`) because the documented "Redis/CRDT backend" never materialised — real substitution happens at `ISharedKeyValueStore` beneath it. **Verdict: keep the module, do not market it as a substitution seam.**
4. **Delete the `Processed` flag + index‑aligned accumulators.** Complexity **reappears elsewhere but better‑placed**: routing by `NodeManagerHandle` replaces cooperative multiplexing. Not pure indirection — but the *prose‑enforced* protocol (restated 6×) is deleted in favour of a typed partition dispatch. **Delete the protocol, keep the routing.**

---

#### 4. Dependency strategy and adapters (two adapters or bust)

**Reused seams (I invent nothing):** `ISharedKeyValueStore`, `ILeaderElection`, `IRecordProtector`, `ILocalAddressSpace`/`ILocalAddressSpaceSource`, `IAddressSpaceSynchronizer`, `INodeStateStore`/`INodeStateSnapshotStore`/`IStoredNode`/`NodeStateChange`/`NodeStateSerializer`, `ISubscriptionStore`/`IStoredSubscription`/`IStoredMonitoredItem`/`ISubscriptionRetransmissionStore`, `IMonitoredItemQueueFactory`, `IContinuationPointStore`/`ContinuationPointEnvelope`, `ISharedSessionStore`/`SharedSessionEntry`, `ISingleUseNonceRegistry`, `IEventIdProvider`, `IServerStartupTask`.

| Port | Adapter 1 (single‑node/test) | Adapter 2 (distributed/prod) | Verdict |
|---|---|---|---|
| **`ISharedKeyValueStore`** | `InMemorySharedKeyValueStore` | `RaftSharedKeyValueStore`, `ReplicatedSharedKeyValueStore` (CRDT), `HybridSharedKeyValueStore` | **Real (4).** The foundation. |
| **`ILeaderElection`** | `StaticLeaderElection` | `RaftLeaderElection`, `KubernetesLeaseLeaderElection`, `SharedStoreLeaseElection` | **Real (4).** |
| **`IRecordProtector`** | `NullRecordProtector` | `AesCbcHmacRecordProtector`, `KeyRingRecordProtector` | **Real (3).** Category 4. |
| **`ILocalAddressSpace`** | `DictionaryAddressSpace` | `PredefinedNodesAddressSpace` | **Real (2).** |
| **`IAddressSpaceSynchronizer`** | `AddressSpaceSynchronizer` (single‑writer, 788 ln) | `ReplicatedAddressSpaceSynchronizer` (CRDT, 801 ln) | **Real (2).** Two genuinely different strategies. |
| **`ISubscriptionStore`** | `SubscriptionStore` (file durable) | `SharedKeyValueSubscriptionStore` (mirror) | **Real (2).** |
| **`IMonitoredItemQueueFactory`** | `MonitoredItemQueueFactory` | `DurableMonitoredItemQueueFactory`, `SharedKeyValueMonitoredItemQueueFactory` | **Real (3).** |
| **`IEventIdProvider`** | default random‑GUID (implicit) | `DeterministicEventIdProvider` | **Real (2).** |
| `INodeStateStore` | `InMemoryNodeStateStore` | — (`StallingNodeStateStore`, `NonSnapshotStoreView` are test‑only) | **One prod adapter.** Keep as **deep module**; substitution is at `ISharedKeyValueStore`. Do **not** sell as a swap seam. |
| `ISharedSessionStore` | `SharedKeyValueSessionStore` | — | **One prod adapter** over KV. Keep as module; the swap is KV/CRDT (`SharedKeyValue` vs `Replicated` session store share this via the store beneath). |
| `IContinuationPointStore` | `SharedKeyValueSubscriptionStore` (implements it) | — | **One adapter, best‑effort.** Keep the **envelope type** (it *is* the interface value); the transfer swap is KV. If no node manager ever serialises typed continuations, this is the weakest port — **justified only** by the typed‑continuation opt‑in in §1.4; otherwise a candidate to fold into `ISubscriptionStore`. |
| `IRoleManager` | `RoleManager` (in‑memory) | — (LDAP/DB hypothetical) | **One adapter.** Category D port justified by the durable‑backing use case + test double as second adapter. Flag honestly: today a hypothetical seam. |
| `IServerIdentityRegistry` | `ServerIdentityRegistry` | — (`RecordingRegistry` test only) | **One adapter.** Same call as `IRoleManager`. |

**The honest conclusion:** the *true* substitution surface is the **foundation trio** plus `ILocalAddressSpace`, `IAddressSpaceSynchronizer`, `ISubscriptionStore`, `IMonitoredItemQueueFactory`, `IEventIdProvider`. The KV‑layered "stores" (`INodeStateStore`, `ISharedSessionStore`, `IContinuationPointStore`) are **deep modules with one production adapter each** — I keep them for their logic and as the synchronizers' test surface, but I do not pretend they are swap seams, and I would **reject** a proposal to add a fourth KV‑layered "store" that just forwards to `ISharedKeyValueStore`.

**AOT & serialization (constraint 2).** No reflection anywhere on the replication path: `NodeStateSerializer` frames `NodeClass` + `NodeState.SaveAsBinary`; `SharedSessionEntry` and `IHistoryContinuationState : IEncodeable` use the source‑generated binary/JSON encoders. `IStoredNode.Payload` is a `ByteString`. `IRecordProtector` operates on `ReadOnlySpan<byte>`. **Constraint 5 types throughout:** `ArrayOf<T>`, `ByteString`, `in DataValue`, `NodeId`; no `object` in any port; `Variant` where a value is polymorphic.

---

#### 5. Trade‑offs

**Where leverage is high.** Deleting the `IServerInternal` locator + two‑phase construction pays across **200+ files**: each stops depending on 57 members to reach 1–2, and the Redundancy library stops reaching in through `Set*`. The `NodeState` data/behaviour split is the single change that makes HA *correct by construction* — behaviour can never accidentally be serialised, so a replica can never fail over into a half‑reconstructed graph. Portable `NodeManagerHandle` retires an entire prose‑enforced protocol (`Processed`, restated 6×) *and* the `object` handle in one move.

**Where leverage is thin (kept honest).** `INodeStateStore`, `ISharedSessionStore`, `IContinuationPointStore` are single‑production‑adapter. They survive as deep modules, not as swap seams. `IRoleManager`/`IServerIdentityRegistry` are single‑adapter Category‑D ports whose "second adapter" is currently only a test double — I flag them rather than dress them up.

**Latency/allocation cost of state behind ports.** The design's central defence is the **synchronous local fast path**: `ILocalAddressSpace.TryGetNode` and node‑manager `Read` stay sync and zero‑alloc, so the 2,000‑session/hot‑publish path is untouched in single‑node mode. The cost lands only on **writes** (write‑behind mirror, coalesced per monitored item — "latest state wins", so mirror cost scales with the *unpublished tail*, not every sampled value) and on **hydration** (snapshot + delta‑log, a handful of large reads instead of one per node). The genuine new tax is the **linearizable nonce CAS** on failover reconnect — one Raft round‑trip per re‑activation — which I accept because a replayable nonce is a security defect, not a perf knob.

**What gets harder for the single‑node user who does not want HA.** Two things. (1) A node manager that used to hang a closure on `NodeState.OnReadValue` now implements `INodeBehaviorSource.TryGetBehavior` — a few more lines even when they will never cluster. Mitigation: `ManagedNodeManager` keeps a convenience overload that adapts a delegate to `INodeBehavior`, and the source generator emits the `TryGetBehavior` switch, so authored code rarely writes it by hand. (2) `IServerContext` being frozen after bind removes the "just call `SetSessionManager` again" escape hatch some tests used; they move to DI registration.

**What I gave up.** On‑demand node fault‑in (materialising a node only on first browse) — hydration still fully materialises the graph (tracked as #3938). True active/active linearizable address‑space writes — the CRDT path is eventual by construction; strong consistency stays on the Raft keyspace for the exactly‑once cases only. And I deliberately did **not** unify the KV‑layered stores into one generic `IStateStore<T>` — that would be depth‑by‑genericity that erases the domain invariants (a subscription store's restore semantics differ from a node store's snapshot semantics).

**Migration from the 93‑virtual / 155‑member / 57‑locator world (all additive; nothing removed — constraint 1).**
1. **Ship `IServerContext` + individual port injection alongside `IServerInternal`.** `ServerInternalData` implements `IServerContext`; the 12 `Set*` and `object` locks are marked `[Obsolete("Bind through IServerStartupTask; frozen after Running")]` and forward into the bind phase (throwing only after `Running`).
2. **`ManagedNodeManager` gains `INodeBehaviorSource`;** `CustomNodeManager2`/`AsyncCustomNodeManager` keep every virtual, but the 55 never‑overridden ones become non‑virtual‑by‑default in the new base while the old bases stay for the 33 existing subclasses.
3. **Retype the `object` leaks additively:** add `GetManagerHandleAsync : ValueTask<NodeManagerHandle?>`, `RestoreHistoryContinuationPointAsync : IHistoryContinuationState?`, and `GetServerDiagnosticsAsync`; mark the `object`/lock members `[Obsolete]`.
4. **The Redundancy library rebinds to the new `IServerStartupTask(IServerContext, IServiceProvider)` overload** and stops calling `Set*` — no behaviour change, just a cleaner seam.
5. Update `docs/HighAvailability.md` + `migrationguide.md`; the `samples/RedundantServer` "no data loss on strong store" test is the acceptance gate.

---

## Comparison

### Convergence — what all four agreed on

Four designers working independently, none seeing another's output, arrived at the same
five moves. When independent designs converge like this, the moves are not design opinions —
they are the shape of the problem, and they are the most actionable output of the exercise.

| Convergent move | minimal | flexible | commoncase | ports |
|---|---|---|---|---|
| `IServerInternal` 57 members → small read-only context | `IAddressSpaceServices` | `IServerContext` (8) | `INodeSourceContext` (6-7) | `IServerContext` (9) |
| The `object` handle is removed | deleted outright | `NodeOwnership` struct | typed `THandle` | portable `NodeManagerHandle` |
| The `Processed` protocol is eliminated or relocated to one place | relocated to dispatcher | dissolved by owner pre-split | dissolved by first-match-wins | dissolved by portable-handle routing |
| `IHistorianProvider` is the pattern to copy | explicit | explicit | explicit | explicit |
| Additive `[Obsolete]` migration; 33 subclasses keep compiling | yes | yes | yes | yes |

All four also refused to expose a lock anywhere, and all four kept the OPC UA domain
vocabulary (`NodeId`, `Variant`, `DataValue`, `ArrayOf<T>`) visible — correctly identifying
that hiding the domain would be dishonest depth.

### Divergence — the real disagreements

**1. Batch versus per-node on the read path.** The sharpest technical split.

| Design | Read shape | Consequence |
|---|---|---|
| `minimal` | `ReadAsync(ctx, ReadTarget, ct)` — one node per call | Easiest to implement correctly; one `await` per node |
| `commoncase` | `ReadAsync(THandle, in ReadFilter, ct)` — per node, but with sync/static shapes available | Most reads never become async at all |
| `flexible` | `ReadBatch` readonly struct over pooled buffers | Zero steady-state allocation; author does local-index bookkeeping |
| `ports` | Synchronous local serving read; async only for writes/mirror/hydrate | Hot path untouched in single-node mode |

This matters because a server may serve thousands of monitored items. `minimal`'s per-node
`await` is central to its design, not peripheral.

**2. Whether the full surface stays first-class.** `commoncase` alone keeps
`IAsyncNodeManager`/`AsyncCustomNodeManager` **un-obsoleted** as rung 4 — a documented
destination rather than a legacy path. `minimal` and `flexible` mark the old surface
`[Obsolete]`. Given 33 subclasses and external consumers, `commoncase`'s position is the more
realistic reading of what this API actually is.

**3. Whether async is mandatory.** `commoncase` and `ports` independently verified that
`BaseVariableState` already carries **both** a synchronous handler family (`OnReadValue`,
line 559) and an asynchronous one (`OnReadValueAsync`, line 578) — and that the async family
runs *without* `lock(this)`. Both therefore preserve a genuine synchronous read path with no
sync-over-async. `minimal` and `flexible` make every read awaitable.

**4. What the node-manager handle is for.** Only `ports` asked whether the handle can cross a
machine boundary. That question is decisive for the stated HA goal and none of the other three
raised it.

### Depth

`minimal` wins on raw interface size — 2 mandatory members. But it buys that number with the
per-node read above, and it is candid that `IVirtualNodes` is a hypothetical seam for
fully-materialised authors, kept alive only by the external-system author next door.

`flexible` has the widest *learnable* surface (umbrella + ~10 facets + interceptor facets),
but each individual piece is small and you only learn the ones you use. Its depth claim is
strongest at the implementation boundary: 4 members in front of the whole `NodeState` engine
for one adapter, in front of a REST/SQL client for another.

`commoncase` has the most honest depth. Leverage is highest exactly where the measurements say
callers actually live, and it proves it by mapping the measured common-6 one-to-one onto
rung-1/2 knobs. It also declines to claim depth it does not have — noting that a rung-3 author
still learns browse and continuation *concepts*, because the mechanism was hidden but the
domain was not.

`ports` optimises depth for state rather than authoring. Its `IServerContext` is explicitly a
*narrowing* module, not a deep one — a distinction `flexible` also drew about its own
equivalent, and both were right to draw it.

### Locality

`flexible` is unique. An interceptor registered once applies across **every** partition,
including external ones — something inheritance structurally cannot do, because an override
lives on one base class. The repository already has audit APIs, a `RateLimiting` folder, and
Part 18 role permissions; all three are per-request cross-cutting concerns.
`commoncase`'s answer to the same need — subclass `StandardServer` and override
`OnRequestValidatedAsync` — is a weaker form, though it is at least the *right ladder*.

`ports` gives the best locality for state: every category of server state has exactly one home,
and behaviour can never accidentally be serialised because it is no longer in the replicated
graph at all.

`minimal` and `commoncase` both concentrate dispatch, id arbitration and paging in one runtime
component, which is a large locality win over the status quo where 21+ browse authors each
re-implement continuation handling.

### Seam placement

This separated them most sharply, because it is where a designer can quietly cheat.

`ports` was the only one to **refuse seams it was invited to sell**:

> "`INodeStateStore`, `ISharedSessionStore`, `IContinuationPointStore` are deep modules with
> **one production adapter** each — I keep them for their logic and as the synchronizers' test
> surface, but I do not pretend they are swap seams, and I would **reject** a proposal to add a
> fourth KV-layered store that just forwards to `ISharedKeyValueStore`."

It then flagged `IRoleManager` and `IServerIdentityRegistry` as "today a hypothetical seam"
rather than dressing them up, and relocated the real substitution surface to
`ISharedKeyValueStore` (**4 adapters**), `ILeaderElection` (4) and `IRecordProtector` (3).
That is the one-adapter rule applied against its own brief's incentive.

`flexible` was also disciplined — it explicitly declines to promote its router and pipeline
cursor structs to public seams because each would have one adapter. `minimal` admits its one
weak seam. `commoncase` is weakest here: its provider seam is justified partly by a test
double, which is the thinnest of the four justifications.

### Four answers to one defect

The clearest evidence the exercise did its job. One defect — `object? GetManagerHandle(NodeId)` —
produced four genuinely different resolutions:

| Design | Resolution | Trade-off |
|---|---|---|
| `minimal` | **Delete it.** Address by `NodeId`; a source caches privately if it wants | Simplest; loses the caching the handle existed to provide |
| `flexible` | `NodeOwnership` **readonly struct** with an in-process `NodeState` fast path | No boxing; carries a pointer, so cannot cross a replica |
| `commoncase` | **Typed** `THandle` via `INodeProvider<THandle>` | Type-safe; admits boxing at the erased router boundary |
| `ports` | **Portable** `NodeManagerHandle` (`NodeId` + owning namespace index, no pointer) | Survives failover; retires the `Processed` protocol as a side effect |

Only `ports` asked whether the handle can survive a failover — the question that matters for the
project's stated first architectural goal.

---

## Recommendation

**Take `design-commoncase` as the authoring surface. Take `design-ports` for state. Take the
interceptor chain from `design-flexible`. Reject `design-minimal`'s per-node read.**

### The hybrid

| Concern | Take from | What |
|---|---|---|
| Authoring surface | **commoncase** | The 4-rung ladder; creational verbs on the builder; `internal sealed` runtime |
| Read ergonomics | **commoncase** | Three read shapes — `Value(T)`, sync `OnRead(Func<T>)`, async `OnRead(Func<CT, ValueTask<T>>)` |
| Node handle | **ports** | Portable `NodeManagerHandle` — beats commoncase's boxing `THandle` and flexible's pointer-carrying struct |
| Node behaviour | **ports** | `INodeBehavior` / `INodeBehaviorSource` data/behaviour split |
| Service locator | **ports** | `IServerContext` + frozen bind phase replacing the 12 `Set*` mutators |
| Cross-cutting concerns | **flexible** | Interceptor chain at batch granularity, struct cursor |
| Hot path | **flexible** + **ports** | Pooled batch structs where batching is needed; synchronous local serving reads |
| Browse paging | **commoncase** or **minimal** | `BrowsePage`/`ContinuationToken`, or `IAsyncEnumerable<NodeReference>` — both let the framework own slicing |

**Why commoncase leads.** It is the only design grounded in the measured override
distribution rather than an aesthetic, and it demonstrates the fit rather than asserting it.
It found a real, verifiable defect none of the others did: *today's `Variable<T>` only
resolves an existing node and throws if absent, which is why the no-class
`AddNodeManager("ns", b => …)` path cannot stand up a device server without first authoring a
NodeSet2.* That single gap explains why the existing ergonomic story does not land, and it is
independently checkable.

Its `internal sealed` runtime is the right kind of answer to the shallow-facade risk — enforced
by the compiler, not by documentation. Neither an author nor a test can reach the 93 virtuals
through a rung-1/2 source, which is precisely the failure mode the current
`FluentNodeManagerBase : AsyncCustomNodeManager` inheritance creates.

And keeping rung 4 un-obsoleted respects what this API actually is: plugin surface with 33
subclasses and unknown external consumers.

**Why it must absorb ports.** `commoncase` does not address high availability at all — the
project's stated *first* architectural goal. The data/behaviour split is the one change that
makes replication correct by construction: delegates are code, cannot be serialised, and
therefore must not live in the replicated graph. A replica can then never fail over into a
half-reconstructed address space. `ports` also notes the split is already latent in the
existing AOT-safe `NodeStateSerializer`, so this is formalising something the codebase has
half-built rather than inventing.

**Why the interceptor chain.** Audit, rate limiting and Part 18 role permissions are all
per-request and all already present in the repository. They are the cases inheritance serves
worst and composition serves best.

### What to reject and why

- **`minimal`'s per-node `ReadAsync`.** An `await` per node at thousands of monitored items is
  a regression this codebase cannot absorb — a single node already tops out near 2,000
  sessions. The rest of the design is elegant, and its `IAsyncEnumerable<NodeReference>` browse
  is arguably the cleanest of the four, but the read path is central rather than peripheral.
- **Any fifth KV-layered "store" port.** `ports` argued this pre-emptively and correctly:
  a store that just forwards to `ISharedKeyValueStore` is indirection, not a seam.
- **Obsoleting the full node-manager surface now.** `minimal` and `flexible` both propose it.
  With 33 subclasses and external consumers, `commoncase`'s un-obsoleted rung 4 is the
  defensible position.

### Sequencing

| # | Step | Depends on | Notes |
|---|---|---|---|
| 1 | **Diagnostics lock** — owner-side update methods over `System.Threading.Lock` | nothing | 88 `lock` statements; `UpdateServerStatus(Action<T>)` already shows the shape. Independent of everything below. |
| 2 | **`IServerContext` + frozen bind phase** | 1 | All four designs agree; `ports`' version is the most rigorous. Deletes the 12 `Set*` mutators. |
| 3 | **Creational verbs on the builder + `internal sealed` runtime** | 2 | Closes the ergonomic cliff; unblocks the trivial case. |
| 4 | **Portable `NodeManagerHandle`** | 3 | Retires the `Processed` protocol as a side effect. |
| 5 | **Data/behaviour split** (`INodeBehavior`/`INodeBehaviorSource`) | 4 | The HA enabler. Largest and last. |

Steps 1 and 2 are worth doing regardless of whether the rest is ever taken.

### Honest caveats

- **Steps 3-5 are a major undertaking against a live plugin API.** The evidence supports the
  direction, but the cost is real and should not be understated by the neatness of the designs
  above.
- **The interceptor chain adds an ordering concern** that does not exist today. Registration
  order becomes semantically meaningful, which is more visible than implicit call order but is
  a new thing for users to reason about.
- **The data/behaviour split costs the single-node user.** A node manager that hangs a closure
  on `NodeState.OnReadValue` today would implement `INodeBehaviorSource.TryGetBehavior` —
  more lines even for someone who will never cluster. Both the source generator and a delegate-
  adapting convenience overload mitigate it, but it is a real tax.
- **No design was tested.** These are proposals grounded in read-only investigation. Every
  performance claim (pooled batches, zero-allocation steady state, sync fast path) is a design
  intention, not a measurement.

---

## Appendix — verified facts each designer independently confirmed

Facts the designers checked against the tree during their investigation, useful because several
are load-bearing for the recommendation and were verified more than once independently:

- `BaseVariableState` carries **both** `OnReadValue` (sync, line 559) and `OnReadValueAsync`
  (async, line 578); the async family runs **without** `lock(this)`. Confirmed by `commoncase`
  and `ports` separately. This is what makes a genuine sync read path possible.
- Today's fluent `Variable<T>(...)` overloads **only resolve** an existing node and throw
  `ServiceResultException` if absent — they cannot mint a node. Confirmed by `commoncase` from
  the interface doc comments.
- `NodeManagerRoutingTable` is already `IReadOnlyList<IAsyncNodeManager>` — everything is
  normalised to the async interface before dispatch. Confirmed by `minimal` and `flexible`.
- `MasterNodeManager` broadcasts to every node manager in a `foreach` loop
  (`MasterNodeManager.cs:4053`) and already performs an O(items) validation pass
  (`MasterNodeManager.cs:4023`). Confirmed by `flexible`.
- `NodeStateSerializer` frames `NodeClass` + `NodeState.SaveAsBinary` and is AOT-safe;
  behaviour is already documented as "re-attached by the owning node manager". Confirmed by
  `ports` — this is the latent data/behaviour split.
- The `ImpersonateUser` obsoletion message **already** points at
  `IUserTokenAuthenticator` + `IServerIdentityRegistry`. Confirmed by `flexible`, which noted
  it was completing a migration the repository had already started.
- `IHistorianRegistryProvider` already extends the server *without* touching `IServerInternal` —
  an existing capability-probe precedent. Confirmed by `flexible`.
- `AddNodeManager(string, Action<INodeManagerBuilder>)` already exists at
  `OpcUaServerBuilderExtensions.cs:863` — the no-class registration path is present but cannot
  create nodes. Confirmed by `commoncase`.
