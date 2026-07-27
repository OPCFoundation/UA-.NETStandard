# Registering NodeManagers

A NodeManager owns a part of the server address space. This guide explains the three points at
which a NodeManager can be registered with a server, and what the server guarantees when
registrations change while the server is running.

For how to author a NodeManager, see [source-generated NodeManagers](SourceGeneratedNodeManagers.md),
[runtime NodeSets](RuntimeNodeSets.md), and
[CoreNodeManager vs CustomNodeManager2](CoreNodeManagerVsCustomNodeManager2.md).

## Where NodeManagers come from

| Registration point | API | When the address space is built |
| --- | --- | --- |
| Compile time | A source-generated or hand-written `AsyncCustomNodeManager` / `CustomNodeManager2` type | When the server creates its address space |
| Startup | `IOpcUaServerBuilder.AddNodeManager(...)`, `IOpcUaServerBuilder.AddRuntimeNodeSet(...)` | During `CreateAddressSpaceAsync`, before the server accepts connections |
| Runtime | `INodeManagerLifecycle.AddAsync` / `ReloadAsync` / `RemoveAsync` | While the server is running and serving Clients |

Compile-time and startup registration are the normal path. Use runtime registration only when the
set of models genuinely has to change without restarting the server.

## Startup registration

`AddNodeManager` and `AddRuntimeNodeSet` register a factory on `IOpcUaServerBuilder`. The factory is
created before the server starts, and the server builds its address space from all registered
factories while it starts.

```csharp
services.AddOpcUa()
    .AddServer(o => { /* … */ })
    .AddNodeManager(sp => new MyNodeManager(sp.GetRequiredService<ITelemetryContext>()));
```

## Runtime registration

A running server exposes `INodeManagerLifecycle`. Resolve it from dependency injection in a hosted
server, or use `StandardServer.NodeManagerLifecycle` when constructing the server directly.

```csharp
public sealed class ModelLoader(INodeManagerLifecycle lifecycle)
{
    private NodeManagerRegistration? m_registration;

    public async ValueTask LoadAsync(IAsyncNodeManagerFactory factory, CancellationToken ct)
    {
        m_registration = await lifecycle.AddAsync(factory, ct);
    }

    public async ValueTask ReloadAsync(IAsyncNodeManagerFactory replacement, CancellationToken ct)
    {
        m_registration = await lifecycle.ReloadAsync(m_registration!, replacement, ct);
    }

    public ValueTask RemoveAsync(CancellationToken ct)
    {
        return lifecycle.RemoveAsync(m_registration!, ct);
    }
}
```

Each add returns an immutable `NodeManagerRegistration`. Reload returns the next generation and
invalidates the previous handle. Only registrations created by the lifecycle provider can be
reloaded or removed; startup, diagnostics, and core NodeManagers are protected.

`INodeManagerLifecycle` is a host control-plane API. Do not invoke reload or removal from inside an
OPC UA service or Method callback: teardown waits for the requests that already captured the retired
routing generation to complete before disposing it, so a lifecycle call made from within such a
request would wait for itself. The server detects this and throws `InvalidOperationException` rather
than deadlocking. Detection relies on the request being dispatched through the server's service
pipeline, so as a second line of defence the wait is bounded: it lasts at most as long as the
longest deadline still outstanding plus `RequestManager.RequestDrainTimeout`, after which the
lifecycle operation fails with a `TimeoutException` instead of blocking indefinitely.

A server that rejects requests of its own by overriding `StandardServer.OnRequestValidatedAsync`
does not interfere with this: a rejected request is completed before the exception leaves the
server, so it never holds a lifecycle operation up.

A lifecycle operation is transactional. The replacement address space is built and validated before
anything becomes visible to Clients, and any failure is rolled back, so Clients never observe a
partially applied model.

## What happens to Clients

### MonitoredItems

Active MonitoredItems survive reload and removal. A compatible NodeId in a replacement generation
keeps the same MonitoredItem and Subscription without a transient bad status. A removed or
incompatible NodeId is detached and publishes one `BadNodeIdUnknown` data-change notification, as
required by OPC UA Part 4 §5.8.4.1; adding a compatible Node with the same NodeId later revalidates
and reattaches the item automatically. Event MonitoredItems detach and recover their source binding
without synthesizing a data-change status.

That notification is queued in its natural position, because Part 4 §5.13.1.5 requires a Server to
return notifications in the order they are in the queue. It is queued in addition to the configured
`queueSize` and is exempt from overflow discard, mirroring the rule the same section defines for
`EventQueueOverflowEventType`, so a full queue cannot swallow it. The specification does not state
this explicitly for data-change notifications; issue
[#4102](https://github.com/OPCFoundation/UA-.NETStandard/issues/4102) records the ambiguity and
where to change the behaviour if it turns out to be non-compliant. Only one such notification is
pending at a time, and a pending one is not preserved across a durable subscription restart.

The built-in NodeManager and Subscription implementations support these transitions. A custom
implementation that the server cannot migrate safely fails with `NotSupportedException` before any
routing changes, so the operation is rejected rather than half applied.

To make a custom NodeManager participate, derive from `CustomNodeManager2` or
`AsyncCustomNodeManager`, which already implement the MonitoredItem transition contract, or
implement `INodeManagerMonitoredItemLifecycle` directly. That interface needs four operations: report
whether an existing MonitoredItem could attach, detach one without disposing it, attach a detached
one to the matching Node, and give a detached one back when a lifecycle operation is rolled back. A
custom Subscription implementation needs the equivalent snapshots from
`ISubscriptionMonitoredItemLifecycle`.

### Continuation points

Reload and removal invalidate saved Browse continuation points owned by the retired NodeManager. A
later `BrowseNext` with one of those tokens returns `BadContinuationPointInvalid` instead of
invoking a disposed generation.

### Namespaces

Namespace indexes are append-only for the lifetime of a running server. Removing a model removes its
Nodes and routing but leaves its namespace URI in `NamespaceArray`, and a later reload or add reuses
the same index. When a live add appends a URI, the server updates `NamespaceArray` and `UrisVersion`.

### DataTypes

Runtime DataType registrations are additive. Reload accepts an existing DataType only when its
definition is structurally compatible, rejects incompatible changes, and retains removed stand-in
encodeables so existing Sessions and in-flight values remain decodable.

### Change notifications

Every committed lifecycle transaction emits one compressed model-change notification. Reload also
emits a semantic-change notification when values of Properties marked with the `SemanticChange`
access-level bit changed.

## Requirements on a reloadable NodeManager

A NodeManager can be added and removed through the lifecycle provider without extra work.

Reload needs more, because the references other NodeManagers hold into the retired address space
have to be carried over. A NodeManager can only be reloaded when it implements
`INodeManagerReloadParticipant`, which re-adds the references it contributed to Nodes owned by
other NodeManagers to the replacement and reports the inbound references whose target the
replacement no longer contains, so the server can remove their counterparts. Reloading a NodeManager
that does not implement it fails with `NotSupportedException` before anything changes.

Today `RuntimeNodeSetNodeManager` is the only built-in NodeManager that implements the contract, so
runtime NodeSets can be reloaded out of the box. A NodeManager derived from `CustomNodeManager2` or
`AsyncCustomNodeManager` can be added and removed live, and becomes reloadable by implementing
`INodeManagerReloadParticipant`: return the references your NodeManager added to Nodes it does not
own, hand them to the replacement, and report the ones whose target NodeId the replacement no longer
has.

## Related documentation

* [Runtime NodeSets](RuntimeNodeSets.md) — loading NodeSet2 XML without source generation.
* [Source-generated NodeManagers](SourceGeneratedNodeManagers.md) — compile-time models.
* [Dependency Injection](DependencyInjection.md) — the `services.AddOpcUa()` hosting surface.
* [Model Change Tracking](ModelChangeTracking.md) — how Clients observe address-space changes.