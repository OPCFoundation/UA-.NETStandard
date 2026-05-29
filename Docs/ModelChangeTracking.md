# Model Change Tracking

OPC UA Part 5 §6.4.32 defines `GeneralModelChangeEventType` so a
server can notify clients that the address space has changed —
nodes added or deleted, references added or deleted, datatypes
changed. The `Opc.Ua.Client.ModelChange` namespace ships an
opt-in client-side tracker that consumes those events, drops the
affected entries from the client's `INodeCache`, and surfaces the
changes through a strongly-typed event so application code can
re-browse the impacted subtrees.

The server-side counterpart lives in `Opc.Ua.Server.Alarms`:
`ModelChangeAggregator` batches per-call changes and the
`CustomNodeManager` (and `AsyncCustomNodeManager`) emit
`GeneralModelChangeEvent` automatically from `CreateNode` /
`DeleteNode`. Opt out via a flag if you need manual control.

- [Quick reference](#quick-reference)
- [Server side: emitting model changes](#server-side-emitting-model-changes)
  - [Auto-emit from `CreateNode` and `DeleteNode`](#auto-emit-from-createnode-and-deletenode)
  - [Manual aggregation across a transaction](#manual-aggregation-across-a-transaction)
  - [Semantic change events](#semantic-change-events)
- [Client side: tracking model changes](#client-side-tracking-model-changes)
  - [Enabling tracking on a `ManagedSession`](#enabling-tracking-on-a-managedsession)
  - [Manual construction](#manual-construction)
  - [Reacting to changes](#reacting-to-changes)
  - [Per-node `NodeCache` invalidation](#per-node-nodecache-invalidation)
- [Security considerations](#security-considerations)
- [Reference](#reference)

## Quick reference

| Concern | Server entry point | Client entry point |
|---|---|---|
| Emit a model change | `CustomNodeManager.RaiseGeneralModelChangeEvent(...)` (auto from `CreateNode`/`DeleteNode`) | n/a |
| Aggregate before emit | `ModelChangeAggregator.RecordNodeAdded/Deleted/...` | n/a |
| Opt out of auto-emit | `node manager.ModelChangeEmissionEnabled = false` | n/a |
| Subscribe to changes | n/a | `ManagedSession.EnableModelChangeTrackingAsync()` |
| Inspect changes | n/a | `IModelChangeTracker.ModelChanged` event |
| Invalidate one node | n/a | `INodeCache.InvalidateNode(nodeId)` |
| Invalidate everything | n/a | `INodeCache.Clear()` |

## Server side: emitting model changes

### Auto-emit from `CreateNode` and `DeleteNode`

`AsyncCustomNodeManager.CreateNodeAsync(...)` and `DeleteNodeAsync(...)`
(and the sync-compat overloads on `CustomNodeManager`) automatically
record the change in a per-instance `ModelChangeAggregator` and emit a
`GeneralModelChangeEvent` at the end of the call.

```csharp
public class MyNodeManager : AsyncCustomNodeManager
{
    public async ValueTask AddDeviceTwinAsync(NodeId parent, string name, CancellationToken ct)
    {
        var device = new BaseObjectState(null);
        // CreateNodeAsync emits a GeneralModelChangeEvent with verbs
        //   NodeAdded(device.NodeId) + ReferenceAdded(parent)
        NodeId id = await CreateNodeAsync(SystemContext, parent,
            ReferenceTypeIds.HasComponent,
            new QualifiedName(name, NamespaceIndex),
            device,
            ct).ConfigureAwait(false);
    }
}
```

If your node manager mutates the address space without going through
`CreateNodeAsync` / `DeleteNodeAsync` (for example by editing an
in-memory `NodeStateCollection`), you can either:

1. **Opt out of auto-emit and drive it yourself:**

   ```csharp
   public class MyNodeManager : AsyncCustomNodeManager
   {
       public MyNodeManager(...)
       {
           ModelChangeEmissionEnabled = false;   // disable auto-emit
       }

       public void RewireGraph()
       {
           // ...mutate nodes...
           ModelChangeAggregator.RecordReferenceAdded(parent.NodeId);
           ModelChangeAggregator.RecordNodeDeleted(stale.NodeId, stale.TypeDefinitionId);
           EmitModelChange(SystemContext);       // single event covers the batch
       }
   }
   ```

2. **Leave auto-emit on and call the aggregator + `EmitModelChange`
   from custom mutation paths.** Every `CreateNode` / `DeleteNode`
   continues to emit its own event for the change it made.

`EmitModelChange` is a no-op when the aggregator has no pending
changes, so it is safe to call at the end of an arbitrary service
implementation.

### Manual aggregation across a transaction

`ModelChangeAggregator` is thread-safe and supports arbitrary batching:

```csharp
var aggregator = new ModelChangeAggregator();
aggregator.RecordNodeAdded(motor.NodeId, ObjectTypeIds.BaseObjectType);
aggregator.RecordReferenceAdded(parent.NodeId);
aggregator.RecordDataTypeChanged(speedVar.NodeId);
aggregator.RecordReferenceDeleted(oldParent.NodeId);

if (aggregator.HasPending)
{
    RaiseGeneralModelChangeEvent(context, aggregator.Drain());
}
```

`Drain` clears the pending list and returns the batch in insertion
order. The aggregator can be reused across many transactions.

The `Verb` field in `ModelChangeStructureDataType` is a bitmask (the
`ModelChangeVerbs` flags enum on the server side mirrors the same
spec values):

| Flag | Value | Meaning |
|---|---|---|
| `NodeAdded` | 1 | A new node was added. |
| `NodeDeleted` | 2 | An existing node was deleted. |
| `ReferenceAdded` | 4 | A reference was added. |
| `ReferenceDeleted` | 8 | A reference was deleted. |
| `DataTypeChanged` | 16 | The DataType attribute changed. |

### Semantic change events

`RaiseSemanticChangeEvent(...)` emits `SemanticChangeEventType` for
property semantics changes — for example when an EngineeringUnit,
EURange, or Description changes in a way that affects the meaning
of a value. This is independent of `GeneralModelChangeEvent`; both
are subtypes of `BaseModelChangeEventType` and both are observed by
the client-side tracker.

## Client side: tracking model changes

### Enabling tracking on a `ManagedSession`

The simplest path is to opt in via `ManagedSessionOptions` /
`ManagedSessionBuilder` — tracking auto-starts after connect:

```csharp
ManagedSession session = await new ManagedSessionBuilder(configuration, telemetry)
    .UseEndpoint(endpoint)
    .WithModelChangeTracking()      // opt in
    .ConnectAsync(ct);

session.ModelChange!.ModelChanged += (sender, args) =>
{
    foreach (ModelChange change in args.Changes)
    {
        Console.WriteLine($"{change.Verb}: {change.AffectedNode}");
    }
};
```

Tracking can also be enabled / disabled at runtime:

```csharp
await session.EnableModelChangeTrackingAsync(ct);
// ...
await session.DisableModelChangeTrackingAsync(ct);
```

Disposing the session disposes the tracker.

### Manual construction

If you need the tracker without a `ManagedSession` — for example with
a raw `Session` that has the V2 engine enabled — construct it
yourself:

```csharp
var streaming = new StreamingSubscription(session.SubscriptionManager);
var tracker = new ModelChangeTracker(
    streaming,
    nodeCache: session.NodeCache,
    logger: logger);

await tracker.StartTrackingAsync(ct);
```

The tracker takes an `IStreamingSubscription`, an optional `INodeCache`
to invalidate, and an optional `ILogger`. It subscribes to
`BaseModelChangeEventType` on the Server object's notifier
(`ObjectIds.Server`), which covers both `GeneralModelChangeEventType`
and `SemanticChangeEventType` payloads.

### Reacting to changes

The `ModelChanged` event fires once per server event with a structured
payload:

```csharp
public sealed class ModelChangedEventArgs
{
    public IReadOnlyList<ModelChange> Changes { get; }
    public bool RequiresFullCacheInvalidation { get; }
}

public readonly record struct ModelChange(
    ModelChangeVerb Verb,
    NodeId AffectedNode,
    NodeId? TypeDefinition);
```

`RequiresFullCacheInvalidation` is `true` when the server reports a
change without per-node detail (a Part 5 semantic change without a
populated payload). In that case the tracker calls
`INodeCache.Clear()` for you, but applications may also need to
re-browse interesting subtrees:

```csharp
session.ModelChange!.ModelChanged += async (sender, args) =>
{
    if (args.RequiresFullCacheInvalidation)
    {
        await RefreshUiTreeAsync();
        return;
    }

    foreach (ModelChange c in args.Changes)
    {
        if (c.Verb.HasFlag(ModelChangeVerb.NodeDeleted))
        {
            ui.RemoveNode(c.AffectedNode);
        }
        else if (c.Verb.HasFlag(ModelChangeVerb.NodeAdded))
        {
            await ui.BrowseAndAddAsync(c.AffectedNode);
        }
    }
};
```

### Per-node `NodeCache` invalidation

`INodeCache` exposes `InvalidateNode(NodeId)` for targeted eviction.
The `NodeCache` shipped with the stack drops the cached node, value,
and reference list for just that NodeId; alternative implementations
can fall back to `Clear()`.

The tracker calls `InvalidateNode` for every non-NoneVerb change, so
the cache stays in sync without application help. If you want to
intercept (e.g. to drop dependents):

```csharp
class MyInvalidatingCache : INodeCache
{
    private readonly NodeCache m_inner;
    public void InvalidateNode(NodeId id)
    {
        m_inner.InvalidateNode(id);
        // Also drop dependents tracked elsewhere
        m_dependentIndex.RemoveAllReferencing(id);
    }
    // ... delegate everything else to m_inner ...
}
```

## Security considerations

Model-change events are governed by the same per-event permission rules
as every other UA event, with one practical caveat that operators
should understand when deploying on shared address spaces.

### Per-event `ReceiveEvents` is enforced

OPC UA Part 3 §8.55 (`PermissionType`, bit 11 `ReceiveEvents`):

> *A Client only receives an Event if this bit is set on the Node
> identified by the EventTypeId field and on the Node identified by
> the SourceNode field.*

The stack honors this on every live event delivery — see
`MonitoredNode2.ProcessEventSnapshotAsync` →
`IAsyncNodeManager.ValidateEventRolePermissionsAsync` (which delegates
to `ValidateEventReceivePermissionsAsync` for the two
`ValidateRolePermissionsAsync(..., PermissionType.ReceiveEvents)`
checks). For `GeneralModelChangeEvent` the two nodes are
`GeneralModelChangeEventType` and (typically) the `Server` object — a
session lacking `ReceiveEvents` on either node will not receive the
event even when the session has a valid event monitor on the notifier.

The verdict is cached per `(MonitoredItemId, EventTypeId, SourceNodeId)`
so a busy notifier does not pay the two role-permission lookups on
every event. The cache is invalidated when:

- the namespace `DefaultRolePermissions` or `DefaultUserRolePermissions`
  change,
- the notifier's own `RolePermissions` change (signaled via
  `NodeStateChangeMasks.RolePermissions` to subscribed nodes),
- the receiving session's user identity changes
  (`InvalidatePermissionCacheForSession`),
- the monitored item is removed.

If you publish events via your own helper that pushes directly into a
list of `IEventMonitoredItem`, use
`EventManager.ReportEventAsync(event, nodeManager, items, ct)` — it
applies the same per-item permission gate before queueing. The legacy
`EventManager.ReportEvent(IFilterTarget, IList<IEventMonitoredItem>)`
overload is marked `[Obsolete]` because it bypasses the gate.

### The `Affected` array is NOT filtered

`GeneralModelChangeEvent.Changes[]` carries a
`ModelChangeStructureDataType` per address-space change with an
`Affected` and an `AffectedType` `NodeId`. These names are placed into
the event payload as-is — the stack does NOT redact entries the
receiving session cannot browse or read. The OPC UA specification does
not require such filtering, and applying it would force a per-item
metadata lookup for every affected node on every delivery.

When the affected subtree is sensitive (e.g. you do not want
unauthorized sessions to learn that a node with a given browse name
exists), choose one of the following:

1. **Emit on a private View.** Build a `ViewState` that contains only
   the public subset of the address space, give it a distinct
   `SourceNode`, and call `RaiseGeneralModelChangeEvent` with that
   view as the source. Subscribers without `ReceiveEvents` on the
   private view's source node will not receive the event at all.
2. **Split sensitive subtrees into a dedicated `AsyncCustomNodeManager`.**
   Set `ModelChangeEmissionEnabled = false` on that manager and emit
   model-change events on a notifier that is itself protected by
   `ReceiveEvents`. Public node managers continue to auto-emit on
   `Objects.Server` as normal.
3. **Drive emission manually with `ModelChangeAggregator`.** Inspect
   the pending entries before calling `RaiseGeneralModelChangeEvent`
   and drop entries that name sensitive nodes.

### The initial address-space build does not emit

`AsyncCustomNodeManager.CreateAddressSpaceAsync` walks the predefined
node set through `AddPredefinedNodeAsync` — that path does not feed
the `ModelChangeAggregator` and does not call `EmitModelChange`. Only
the dynamic `CreateNodeAsync` / `DeleteNodeAsync` (and any explicit
calls you add) raise `GeneralModelChangeEvent`. This is intentional:
no clients are subscribed during startup, and Part 5 §9.32.2 ties
`ModelChangeEvent` emission to nodes that carry a `NodeVersion`
property — predefined static nodes typically do not.

If your node manager mutates the address space late (e.g. installing
nodes after `StartAsync` returns) and uses `AddPredefinedNodeAsync`
rather than `CreateNodeAsync`, no event will fire. Either use
`CreateNodeAsync` or record the change with the aggregator and call
`EmitModelChange` explicitly. The
`AsyncCustomNodeManager.ModelChangeEmissionEnabled` flag opts the
entire manager out of auto-emit if you want full manual control.

## Reference

- Server source: `Libraries/Opc.Ua.Server/NodeManager/ModelChangeAggregator.cs`
- Server events: `CustomNodeManager.RaiseGeneralModelChangeEvent`,
  `RaiseSemanticChangeEvent` (and the async equivalents in
  `AsyncCustomNodeManager`)
- Client source: `Libraries/Opc.Ua.Client/ModelChange/`
- Cache: `Libraries/Opc.Ua.Client/NodeCache/NodeCache.cs` —
  `InvalidateNode`
- Sessions architecture: [Sessions.md](Sessions.md)
- Spec: [OPC UA Part 5 §6.4.32 — `BaseModelChangeEventType`](https://reference.opcfoundation.org/v105/Core/docs/Part5/6.4.32/)