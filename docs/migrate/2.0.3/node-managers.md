# The synchronous node manager API is deprecated

Since 2.0.3 the synchronous node manager API is marked `[Obsolete]`. It keeps
working exactly as before; a later major version removes it. New and migrated
code uses the asynchronous seam (`IAsyncNodeManager`,
`IAsyncNodeManagerFactory`, `AsyncCustomNodeManager`) or the generated and
fluent node managers built on it. This is step 3 of issue
[#4735](https://github.com/OPCFoundation/UA-.NETStandard/issues/4735).

## Contents

- [UA0031](#ua0031)
- [What is deprecated](#what-is-deprecated)
- [What stays and why](#what-stays-and-why)
- [CustomNodeManager2 to AsyncCustomNodeManager](#customnodemanager2-to-asynccustomnodemanager)
  - [Constructor and factory](#constructor-and-factory)
  - [Overridden hooks](#overridden-hooks)
  - [From `Lock` to per-node locking](#from-lock-to-per-node-locking)
- [Direct `INodeManager` implementations](#direct-inodemanager-implementations)
- [Registration](#registration)
- [Changed factories](#changed-factories)
- [Suppressing the warning](#suppressing-the-warning)

## UA0031

On .NET 5 and later every deprecated member reports diagnostic **`UA0031`**,
with a link to this page. On .NET Framework the compiler cannot attach a
diagnostic id to `[Obsolete]`, so the same members report `CS0618`. Each
message names the replacement.

## What is deprecated

| Deprecated | Replacement |
| --- | --- |
| `CustomNodeManager2` | `AsyncCustomNodeManager`; for new code `FluentNodeManagerBase` or the `[NodeManager]` source generator |
| `INodeManager`, `INodeManager2`, `INodeManager3` | `IAsyncNodeManager` and the per-service capability interfaces (`IReadAsyncNodeManager`, `IWriteAsyncNodeManager`, `ICallAsyncNodeManager`, …) |
| `INodeManagerFactory` | `IAsyncNodeManagerFactory` |
| `AsyncNodeManagerAdapter`, `AsyncNodeManagerAdapterFactory.ToAsyncNodeManager(INodeManager)` | Implement `IAsyncNodeManager` directly or derive from `AsyncCustomNodeManager` |
| `IStandardServer.AddNodeManager(INodeManagerFactory)`, `RemoveNodeManager(INodeManagerFactory)`, `NodeManagerFactories` | `AddNodeManager(IAsyncNodeManagerFactory)`, `RemoveNodeManager(IAsyncNodeManagerFactory)`, `AsyncNodeManagerFactories` |
| `IOpcUaServerBuilder.AddSyncNodeManager<TFactory>()`, `AddNodeManager(IOpcUaServerBuilder, INodeManagerFactory)` | `AddNodeManager<TFactory>()` with an `IAsyncNodeManagerFactory`, `AddNodeManager(IOpcUaServerBuilder, IAsyncNodeManagerFactory)` |
| `OpcUaServerNodeManagerRegistration(INodeManagerFactory)`, `OpcUaServerNodeManagerRegistration.SyncFactory` | `OpcUaServerNodeManagerRegistration(IAsyncNodeManagerFactory)`, `AsyncFactory` |
| `IMasterNodeManager.NodeManagers` | `IMasterNodeManager.AsyncNodeManagers` |
| `IMasterNodeManager.GetManagerHandle(NodeId, out INodeManager?)` | `GetManagerHandleAsync(NodeId, CancellationToken)` |
| `IMasterNodeManager.RegisterNamespaceManager(string, INodeManager)`, `UnregisterNamespaceManager(string, INodeManager)` | The `IAsyncNodeManager` overloads |
| `LifecycleChangeExtensions.AsAsync(INodeManagerFactory)` | Implement `IAsyncNodeManagerFactory` |
| `FileSystemNodeManagerFactory.Create`, `ProgramNodeManagerFactory.Create` | `CreateAsync` on the same factory |

The synchronous `MasterNodeManager` service methods that block on their
asynchronous counterparts (`AddReferences`, `DeleteReferences`,
`RemoveReferences`, `TranslateBrowsePathsToNodeIds`, `CreateMonitoredItems`,
`ModifyMonitoredItems`, `TransferMonitoredItems`, `DeleteMonitoredItems`,
`GetManagerHandle(NodeId, out IAsyncNodeManager?)`) and the constructors that
take preconstructed managers were already obsolete in 2.0 and are unchanged.

## What stays and why

`IAsyncNodeManager.SyncNodeManager`, `SyncNodeManagerAdapter` and
`SyncNodeManagerAdapterFactory.ToSyncNodeManager` are **not** obsolete. The
runtime still needs the synchronous view of every manager:

- The master node manager, the routing table, session continuation points and
  subscriptions compare managers by `ReferenceEquals(x.SyncNodeManager,
  y.SyncNodeManager)`, so an `AsyncNodeManagerAdapter` around a legacy manager
  and the manager's own async facade resolve to the same owner.
- Legacy lookups (`IMasterNodeManager.NodeManagers`,
  `GetManagerHandle(NodeId, out INodeManager?)`) return it.

They go away together with `INodeManager`. Until then a direct
`IAsyncNodeManager` implementation still provides the property. Because its
type is the obsolete `INodeManager`, the declaration itself needs a narrow
suppression:

```csharp
#pragma warning disable CS0618, UA0031 // Required by IAsyncNodeManager until INodeManager is removed.
public INodeManager SyncNodeManager => m_syncNodeManager ??= this.ToSyncNodeManager();
#pragma warning restore CS0618, UA0031
```

`AsyncCustomNodeManager`, `FluentNodeManagerBase` and generated managers
already provide it.

## CustomNodeManager2 to AsyncCustomNodeManager

`AsyncCustomNodeManager` has the same shape as `CustomNodeManager2`: the same
`NodeState`-based predefined-node index, the same `NodeHandle`s, the same
monitored-item managers (`useSamplingGroups`), and a `New`/`NodeIdFactory`
NodeId policy. The work is mostly renaming overrides to their `*Async`
equivalents and awaiting what used to be called synchronously. The Boiler
sample (`samples/Quickstarts.Servers/Boiler/BoilerNodeManager.cs`) was ported
this way in 2.0.3 and makes a compact before/after reference.

### Constructor and factory

```csharp
// Before
public class MyNodeManagerFactory : INodeManagerFactory
{
    public INodeManager Create(IServerInternal server, ApplicationConfiguration configuration)
        => new MyNodeManager(server, configuration);

    public ArrayOf<string> NamespacesUris => ["urn:my:model"];
}

public class MyNodeManager : CustomNodeManager2
{
    public MyNodeManager(IServerInternal server, ApplicationConfiguration configuration)
        : base(server, configuration, "urn:my:model") { }
}

// After
public class MyNodeManagerFactory : IAsyncNodeManagerFactory
{
    public ValueTask<IAsyncNodeManager> CreateAsync(
        IServerInternal server,
        ApplicationConfiguration configuration,
        CancellationToken cancellationToken = default)
        => new ValueTask<IAsyncNodeManager>(new MyNodeManager(server, configuration));

    public ArrayOf<string> NamespacesUris => ["urn:my:model"];
}

public class MyNodeManager : AsyncCustomNodeManager
{
    public MyNodeManager(IServerInternal server, ApplicationConfiguration configuration)
        : base(server, configuration, server.Telemetry.CreateLogger<MyNodeManager>(), "urn:my:model") { }
}
```

The constructor overloads match (`namespaceUris`, optional `ILogger`,
optional `useSamplingGroups`). Pass your own logger to keep a meaningful log
category; without one the base class logs under the `CustomNodeManager2`
category, as before.

### Overridden hooks

Every overridable hook of `CustomNodeManager2` has an awaitable counterpart.
Return a completed `ValueTask` where the body stays synchronous.

| `CustomNodeManager2` | `AsyncCustomNodeManager` |
| --- | --- |
| `CreateAddressSpace(externalReferences)` | `CreateAddressSpaceAsync(externalReferences, ct)` — call `await base.CreateAddressSpaceAsync(...)` first |
| `DeleteAddressSpace()` | `DeleteAddressSpaceAsync(ct)` |
| `LoadPredefinedNodes(context)` | `LoadPredefinedNodesAsync(context, ct)` |
| `AddBehaviourToPredefinedNode(context, node)` | `AddBehaviourToPredefinedNodeAsync(context, node, ct)` |
| `AddPredefinedNode(context, node)` | `await AddPredefinedNodeAsync(context, node, ct)` |
| `RemovePredefinedNode(...)`, `OnNodeRemoved(node)` | `RemovePredefinedNodeAsync(...)`, `OnNodeRemovedAsync(node, ct)` |
| `AddReverseReferences(...)`, `AddReferences(...)`, `DeleteReference(...)` | `AddReverseReferencesAsync(...)`, `AddReferencesAsync(...)`, `DeleteReferenceAsync(...)` |
| `AddRootNotifier(...)`, `RemoveRootNotifier(...)` | `AddRootNotifierAsync(...)`, `RemoveRootNotifierAsync(...)` |
| `GetManagerHandle(nodeId)`, `ValidateNode(...)`, `FindNodeInCache(...)` | `GetManagerHandleAsync(nodeId, ct)`, `ValidateNodeAsync(...)`, `FindNodeInCacheAsync(...)` |
| `Read`, `Write`, `Call`, `TranslateBrowsePath`, `IsNodeInView` | `ReadAsync`, `WriteAsync`, `CallAsync`, `TranslateBrowsePathAsync`, `IsNodeInViewAsync` |
| `FindMethodState(...)` | `FindMethodStateAsync(...)` |
| `HistoryRead*`, `HistoryUpdate*`, `HistoryDelete*`, `HistoryReleaseContinuationPoints` | the same names with the `Async` suffix |
| `CreateMonitoredItems`, `ModifyMonitoredItems`, `DeleteMonitoredItems`, `SetMonitoringMode`, `TransferMonitoredItems`, `RestoreMonitoredItems` | the same names with the `Async` suffix |
| `CreateMonitoredItem`, `ModifyMonitoredItem`, `DeleteMonitoredItem` | `CreateMonitoredItemAsync`, `ModifyMonitoredItemAsync`, `DeleteMonitoredItemAsync` |
| `On*MonitoredItemsComplete`, `OnMonitoredItemModified`, `OnMonitoredItemDeleted`, `OnMonitoringModeChanged`, `OnMonitoredItemsTransferred` | the same names with the `Async` suffix (`OnMonitoredItemCreated` keeps its name) |
| `ValidateMonitoringFilter(...)`, `ReviseAggregateFilter(...)` | `ValidateMonitoringFilterAsync(...)`, `ReviseAggregateFilterAsync(...)` |
| `ConditionRefresh`, `SubscribeToEvents`, `SubscribeToAllEvents`, `OnSubscribeToEvents`, `OnReportEvent` | the same names with the `Async` suffix |
| `SessionActivated`, `SessionClosing` | `SessionActivatedAsync`, `SessionClosingAsync` |
| `ValidateRolePermissions(...)` | `ValidateRolePermissionsAsync(...)` |
| `Dispose(bool)` | `Dispose(bool)` and `DisposeAsync()` |

`New`, `NodeIdFactory`, `IsNodeIdInNamespace`, `IsHandleInNamespace`,
`IsReferenceInView`, `ValidateViewDescription`, `ValidationComplete`,
`Find`, `FindPredefinedNode<T>`, `PredefinedNodes`, `SystemContext`,
`NamespaceIndex(es)` and `GetHistorianProvider` keep their names.

A `CustomNodeManager2` subclass that implemented `ICallAsyncNodeManager` (or
another capability interface) only to get asynchronous dispatch can drop that:
`AsyncCustomNodeManager` dispatches every service asynchronously.

### From `Lock` to per-node locking

`CustomNodeManager2.Lock` is one manager-wide monitor. Subclasses typically
wrapped address-space construction and runtime edits in `lock (Lock) { … }`.
`AsyncCustomNodeManager` has no such lock, and none is needed:

- `NodeState` synchronizes its own attributes, children and references.
  Read and write each node through its APIs; there is no lock to take.
- Address-space changes go through the awaitable members
  (`AddPredefinedNodeAsync`, `AddNodeAsync`, `CreateNodeAsync`,
  `DeleteNodeAsync`, `RemovePredefinedNodeAsync`). They coordinate with
  in-flight service calls, monitored items and model-change events.
- Service operations are serialized where they must be: writes through an
  awaitable write semaphore, monitored-item management through its own
  semaphore. See
  [Locking strategy](../../AsyncServerSupport.md#locking-strategy-vs-customnodemanager2).

So when porting:

1. Remove `lock (Lock)` around `CreateAddressSpaceAsync` bodies and
   predefined-node registration; await the asynchronous members instead.
2. **Never replace it with `lock (node)` or `lock (handle.Node)`.** Those
   monitors do not coordinate with the node's private locks and protect
   nothing. See the
   [threading contract](../../NodeManagers.md#threading-contract-for-nodes-and-browsers).
3. If your own data (a device cache, a map from tags to nodes) needs mutual
   exclusion, protect it with a private `System.Threading.Lock` or a
   `SemaphoreSlim` that you own. Do not hold it across an `await` or across a
   call into the server.
4. Individual node operations are atomic; a sequence of them is not. Where a
   multi-attribute update must be observed together, set the attributes and
   publish once with `ClearChangeMasksAsync`.

## Direct `INodeManager` implementations

A class that implements `INodeManager`/`INodeManager2`/`INodeManager3`
directly (like the `SampleNodeManager` that the MemoryBuffer sample still
derives from) is adapted to the asynchronous seam by `AsyncNodeManagerAdapter`
at registration. Port it by deriving from `AsyncCustomNodeManager` and moving
each interface method into the matching override from the table above, or
implement `IAsyncNodeManager` directly if you need full control.

## Registration

| Before | After |
| --- | --- |
| `server.AddNodeManager(new MyFactory())` with an `INodeManagerFactory` | the same call with an `IAsyncNodeManagerFactory` |
| `builder.AddSyncNodeManager<MyFactory>()` | `builder.AddNodeManager<MyFactory>()` |
| `master.RegisterNamespaceManager(uri, syncManager)` | `master.RegisterNamespaceManager(uri, asyncManager)` |
| `master.NodeManagers` | `master.AsyncNodeManagers` |

Synchronous factories reserve their namespaces before asynchronous ones. When
you move a factory from `INodeManagerFactory` to `IAsyncNodeManagerFactory`
and other asynchronous factories are registered too, register it first to keep
its namespace indexes where they were.

## Changed factories

These factories now implement `IAsyncNodeManagerFactory` only. Code that
passed them to `AddNodeManager(...)` or a hosting extension is unaffected;
code that called `Create(...)` on them calls `CreateAsync(...)` instead:

- `Opc.Ua.PubSub.Server.PubSubNodeManagerFactory`
- `Opc.Ua.WotCon.Server.WotConnectivityNodeManagerFactory`
- `Boiler.BoilerNodeManagerFactory` (Quickstarts sample)

Their node managers are no longer wrapped as async → sync → async; the master
receives the asynchronous manager directly. Because synchronous factories
reserve their namespaces first, a server that registers one of these next to
other asynchronous factories may see their namespace indexes in registration
order now.

`FileSystemNodeManagerFactory` and `ProgramNodeManagerFactory` keep both
interfaces; only their synchronous `Create` is obsolete.

## Suppressing the warning

Migrate where you can. Where code must keep using the synchronous API for now,
suppress the diagnostic narrowly and say why:

```csharp
#pragma warning disable CS0618, UA0031 // MyLegacyManager is ported in a follow-up.
public class MyLegacyManager : CustomNodeManager2
#pragma warning restore CS0618, UA0031
```

`UA0031` only suppresses this deprecation; `CS0618` is needed for .NET
Framework targets. To silence it project-wide while you migrate, add
`<NoWarn>$(NoWarn);UA0031</NoWarn>` (plus `CS0618` for .NET Framework).
