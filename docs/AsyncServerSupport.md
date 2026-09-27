# Support of the TAP (Task Asynchronous Pattern) for server operations

The OPC UA .NET Standard stack has long supported asynchronous server
operations through the APM (Asynchronous Programming Model). APM uses
`IAsyncResult` and the Begin/End pattern. .NET Framework 1.0 introduced
this pattern, which remains supported in .NET 8.0.

The stack also supports the TAP (Task Asynchronous Pattern) for server
operations. TAP uses `Task` and the `async`/`await` keywords introduced
in .NET Framework 4.0. It is the recommended way to implement asynchronous
operations in modern .NET applications.

Server applications can implement node managers with TAP. This pattern
improves scalability by using server resources more efficiently.

The server library provides these TAP features:

- Task-based `RequestQueue` and `TransportListenerCallback`
- Generated code and `MasterNodeManager` support Task-based operations.
- The `IAsyncNodeManager` interface supports fully asynchronous node managers.
- `AsyncNodeManagerAdapter` and `SyncNodeManagerAdapter` let synchronous
  and asynchronous node managers run side by side.

## Contents

- [Upgrading an existing server](#upgrading-an-existing-server)
- [Async method calls](#async-method-calls)
- [`AsyncCustomNodeManager`](#asynccustomnodemanager)
  - [Registering an AsyncCustomNodeManager](#registering-an-asynccustomnodemanager)
  - [Async browse iteration](#async-browse-iteration)
  - [Locking strategy vs CustomNodeManager2](#locking-strategy-vs-customnodemanager2)
    - [Global write semaphore](#1-global-write-semaphore--m_writesemaphore)
    - [Monitored-item semaphore](#2-monitored-item-semaphore--m_monitoreditemsemaphore)
    - [Per-node locking](#3-per-node-locking-for-read-and-attribute-access)
  - [Monitored-item manager selection](#monitored-item-manager-selection)
  - [Fully asynchronous change notifications](#fully-asynchronous-change-notifications)
- [Creating a custom node manager](#creating-a-custom-node-manager)

## Upgrading an existing server

- Update `INodeManager.CreateMonitoredItems` to support the new `MonitoredItemIdFactory`.
- `AsyncCustomNodeManager` is the recommended base class for fully
  asynchronous node managers; see [AsyncCustomNodeManager](#asynccustomnodemanager).
- Keep using `CustomNodeManager2` and implement the asynchronous interfaces
  your server needs:
  - `IAsyncNodeManager` for full async support
  - `ICallAsyncNodeManager` for async method calls
  - `IReadAsyncNodeManager` for async reading
  - `IWriteAsyncNodeManager` for async writing
  - `IHistoryReadAsyncNodeManager` for async history read
  - `IHistoryUpdateAsyncNodeManager` for async history update
  - `IConditionRefreshAsyncNodeManager` for async condition refresh
  - `ITranslateBrowsePathAsyncNodeManager` for async translate browse path
  - `IBrowseAsyncNodeManager` for async browsing
  - `ISetMonitoringModeAsyncNodeManager` for async monitoring mode changes
  - `ITransferMonitoredItemsAsyncNodeManager` for async monitored item transfer
  - `IDeleteMonitoredItemsAsyncNodeManager` for async monitored item deletion
  - `IModifyMonitoredItemsAsyncNodeManager` for async monitored item modification
  - `ICreateMonitoredItemsAsyncNodeManager` for async monitored item creation

> `MasterNodeManager` detects which asynchronous interfaces a node manager
> implements. It uses an asynchronous implementation when available and
> otherwise falls back to the synchronous implementation.

- The server supports fully asynchronous node managers that implement
  `IAsyncNodeManager`. Register one with
  `StandardServer.RegisterNodeManager(IAsyncNodeManagerFactory)`.
  `IAsyncNodeManager.SyncNodeManager` is required for compatibility; pass
  the node manager to `SyncNodeManagerAdapter` to provide it.

## Async method calls

`CustomNodeManager2` supports asynchronous method callbacks. Implement
`IAsyncNodeManager` on your node manager to enable them. Generated method
state classes expose asynchronous callbacks such as
`UpdateCertificateMethodState.OnCallAsync`; the server uses these callbacks
when the node manager implements the interface. For a generic method handler,
use `MethodState.OnCallMethod2Async`.

## AsyncCustomNodeManager

`AsyncCustomNodeManager` is the recommended base class for building fully async, TAP-native node managers.
Unlike the older `CustomNodeManager2`, it implements `IAsyncNodeManager` **directly** rather than
`INodeManager3`. This has two practical consequences:

- All virtual methods are `async ValueTask`-returning from the start, so there is no boilerplate
  wrapping of synchronous code inside `Task.Run` or similar helpers.
- The `SyncNodeManager` property (required by `IAsyncNodeManager`) is satisfied automatically: the
  constructor calls `this.ToSyncNodeManager()` and stores the resulting `INodeManager3` adapter.
  Callers that still require an `INodeManager3` reference (e.g. legacy subscription code) use that
  adapter; the node manager itself never needs to implement the synchronous interface.

### Registering an AsyncCustomNodeManager

Use the `IAsyncNodeManagerFactory` overload of `StandardServer.RegisterNodeManager`:

```csharp
server.RegisterNodeManager(context =>
    new MyAsyncNodeManager(server, configuration));
```

### Async browse iteration

Every hook a custom node manager overrides is awaitable, including browse
operations. `AsyncCustomNodeManager.BrowseAsync` and
`TranslateBrowsePathAsync` iterate an `INodeBrowser` with
`NextAsync(CancellationToken)` instead of `Next()`. A browser can therefore
fetch data from an underlying system asynchronously. For example, it can
retrieve references from another server without occupying a request worker
with a blocking call. The default `NextAsync` wraps `Next()`, so the browsers
provided by the stack and existing custom browsers continue to work.
See [NodeManagers.md](NodeManagers.md#threading-contract-for-nodes-and-browsers)
for the browser contract.

### Locking strategy vs CustomNodeManager2

`CustomNodeManager2` protects its entire address space with a **single coarse-grained monitor
lock** stored in the `Lock` property:

```csharp
lock (Lock)
{
    // all reads and writes go through this single lock
}
```

While simple, this serialises all concurrent requests for the whole node manager and blocks the
calling thread, which prevents the use of `await` inside the critical section.

`AsyncCustomNodeManager` replaces this with a **two-tier, await-compatible locking model**:

#### 1. Global write semaphore — `m_writeSemaphore`

A `SemaphoreSlim(1, 1)` that serialises all **write** operations across the node manager.
Because it is a `SemaphoreSlim` it can be acquired with `await`:

```csharp
await m_writeSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
try
{
    // safe to write any node
}
finally
{
    m_writeSemaphore.Release();
}
```

Only one write request runs at a time, preventing concurrent modifications of the address space.
Read operations do **not** acquire this semaphore.

#### 2. Monitored-item semaphore — `m_monitoredItemSemaphore`

A second `SemaphoreSlim(1, 1)` serializes **monitored-item management**
operations. It keeps subscription state consistent without blocking reads
or writes. The semaphore protects:

- create, modify, and delete operations
- monitoring-mode changes
- event subscriptions and condition refresh
- monitored-item transfers

```csharp
await m_monitoredItemSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
try
{
    // create / delete / modify monitored items
}
finally
{
    m_monitoredItemSemaphore.Release();
}
```

#### 3. Per-node locking for read and attribute access

Reads do not acquire any manager-wide lock. Instead they lock **only the individual `NodeState`
object** being accessed. This allows many reads to run truly in parallel across different nodes:

```csharp
lock (handle.Node)
{
    errors[ii] = handle.Node.ReadAttribute(
        systemContext,
        nodeToRead.AttributeId,
        nodeToRead.ParsedIndexRange,
        nodeToRead.DataEncoding,
        value);
}
```

The same per-node lock is used for the old-value read inside `WriteAsync` and for
`FindChildBySymbolicName` lookups in the component cache.

| Concern                          | `CustomNodeManager2`       | `AsyncCustomNodeManager`           |
|----------------------------------|----------------------------|------------------------------------|
| Address-space reads              | Global `lock (Lock)`       | Per-node `lock (node)` (parallel)  |
| Address-space writes             | Global `lock (Lock)`       | `await m_writeSemaphore` (serial)  |
| Monitored-item management        | Global `lock (Lock)`       | `await m_monitoredItemSemaphore`   |
| `await` inside critical section  | Not possible               | Supported everywhere               |
| Implemented interface            | `INodeManager3`            | `IAsyncNodeManager`                |

### Monitored-item manager selection

The constructor accepts an optional `useSamplingGroups` flag:

```csharp
// Default: change-triggered (MonitoredNodeMonitoredItemManager)
public MyNodeManager(IServerInternal server, ApplicationConfiguration config)
    : base(server, config) { }

// Opt-in: timer-based sampling (SamplingGroupMonitoredItemManager)
public MyNodeManager(IServerInternal server, ApplicationConfiguration config)
    : base(server, config, useSamplingGroups: true) { }
```

- **`MonitoredNodeMonitoredItemManager`** (default): node value changes are propagated to
  subscribers immediately by awaiting `NodeState.ClearChangeMasksAsync` after every successful
  write (see [Fully asynchronous change notifications](#fully-asynchronous-change-notifications)).
  No background threads are created per subscription.
- **`SamplingGroupMonitoredItemManager`**: a background timer thread samples the current node
  value at the negotiated `SamplingInterval`. Write changes are *not* pushed immediately; instead
  the next scheduled sample detects and delivers them.  Choose this mode when the data source
  produces values independently of OPC UA write requests (e.g. hardware polling).

### Fully asynchronous change notifications

`AsyncCustomNodeManager` sends value changes and events to monitored items
through an asynchronous push path. This path does not block a thread when a
node uses an asynchronous value-read handler or when the per-node notification
channel applies back-pressure.

`NodeState` exposes asynchronous counterparts of its change-notification API alongside the existing synchronous members:

| Synchronous | Asynchronous |
|-------------|--------------|
| `ClearChangeMasks(context, includeChildren)` | `ClearChangeMasksAsync(context, includeChildren, ct)` |
| `OnStateChanged` / `StateChanged` | `OnStateChangedAsync` / `StateChangedAsync` |
| `ReportEvent(context, e)` | `ReportEventAsync(context, e, ct)` |
| `OnReportEvent` | `OnReportEventAsync` |

These additions do not break existing synchronous callers or sinks.
`MonitoredNodeMonitoredItemManager` wires the asynchronous sinks. After a
successful write, `AsyncCustomNodeManager.WriteAsync` flushes changes by
awaiting `handle.Node.ClearChangeMasksAsync(...)`.

The asynchronous sink reads each monitored attribute through
`NodeState.ReadAttributeAsync`. When registered, an asynchronous
`OnReadValueAsync` or `OnSimpleReadValueAsync` handler supplies the value. The
sink then enqueues that snapshot through the bounded per-node channel and
awaits completion. It reads the value before enqueueing the change, so each
notification captures the node value at the time of the change. Awaiting both
operations keeps the producing thread from blocking.

The synchronous `ClearChangeMasks` and `ReportEvent` methods also drive the
asynchronous sinks. They complete inline when the node is synchronously
readable and the channel has capacity. They block the calling thread only
while an asynchronous read is in flight or the channel is full. Synchronous
node managers continue to work without changes, but may block in those two
cases.

Each monitored item's queue controls overflow and discard behavior,
including FIFO/LIFO `DiscardOldest` and the `Overflow` status bit. The shared
per-node channel does not drop notifications. As a result, the configured
queue policy applies to the full stream of changes.

### Creating a custom node manager

Derive from `AsyncCustomNodeManager` and override only the virtual methods you need:

```csharp
public class MyNodeManager : AsyncCustomNodeManager
{
    public MyNodeManager(IServerInternal server, ApplicationConfiguration config)
        : base(server, config, "http://my.org/UA/Data/")
    {
    }

    public override async ValueTask CreateAddressSpaceAsync(
        IDictionary<NodeId, IList<IReference>> externalReferences,
        CancellationToken cancellationToken = default)
    {
        await base.CreateAddressSpaceAsync(externalReferences, cancellationToken)
                  .ConfigureAwait(false);

        // build your nodes here
        var myVar = new BaseDataVariableState(null);
        myVar.NodeId  = new NodeId("MyVar", NamespaceIndex);
        myVar.Value   = 42;
        await AddNodeAsync(SystemContext, default, myVar, cancellationToken)
              .ConfigureAwait(false);
    }
}
```
