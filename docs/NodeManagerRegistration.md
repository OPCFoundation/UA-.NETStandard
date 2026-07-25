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
request would wait for itself.

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

The built-in NodeManager and Subscription implementations support these transitions. Custom
implementations fail closed with `NotSupportedException` before routing changes, when the server
cannot migrate their active items safely.

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

A NodeManager can be added and removed through the lifecycle provider without extra work. To be
reloaded safely it must implement `INodeManagerReloadParticipant`, which transfers inbound
cross-manager references to retained NodeIds and removes the counterparts of dropped Nodes. The
built-in runtime NodeSet manager implements this contract.

## Related documentation

* [Runtime NodeSets](RuntimeNodeSets.md) — loading NodeSet2 XML without source generation.
* [Source-generated NodeManagers](SourceGeneratedNodeManagers.md) — compile-time models.
* [Dependency Injection](DependencyInjection.md) — the `services.AddOpcUa()` hosting surface.
* [Model Change Tracking](ModelChangeTracking.md) — how Clients observe address-space changes.