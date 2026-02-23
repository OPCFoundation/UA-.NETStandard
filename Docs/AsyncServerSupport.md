# Support of the TAP (Task Asynchronous Pattern) for server operations

The OPC UA .NET Standard stack has supported asynchronous operations for a long time. The asynchronous operations are based on the IAsyncResult pattern, which is also called the APM (Asynchronous Programming Model) or the Begin/End pattern. This pattern was introduced with .NET Framework 1.0 and is still supported in .NET 8.0.

In addition to the APM, the TAP (Task Asynchronous Pattern) is now also supported for server operations. The TAP is based on the Task and async/await keywords introduced in .NET Framework 4.0 and is the recommended way to implement asynchronous operations in modern .NET applications.

In the future APM support will be deprecated, as it was never implemented for NodeManagers.

Starting with 1.5.378 the server library allows users to also implement Task based NodeManagers.
Implementing the TAP allows to improve the scalability of the server, as the TAP is significantly more efficient in terms of resource usage and performance.

In order to support the TAP pattern, the following changes have been made to the server library:

- Introduce a Task based `RequestQueue`
- Introduce a Task based `TransportListenerCallback`
- Update the generated Code to support Task based operations
- Update of the `MasterNodeManager` to support Task based operations
- Introduce a Task based `IAsyncNodeManager` interface
- Introduce `AsyncNodeManagerAdapter` and `SyncNodeManagerAdapter` classes to support sync and async node managers side by side.

## Upgrading an existing server

- Update `INodeManager.CreateMonitoredItems` to support the new `MonitoredItemIdFactory`.
- In a future release an `CustomNodeManagerAsync` class will be provided to simplify the creation of fully async NodeManagers.
- The existing `CustomNodeManager2` class can be used as is, and async operations can be implemented as needed using the different interfaces provided by the server library:
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

--> The MasterNodeManager automatically detects if a NodeManager implements any of the async interfaces and uses the async implementation if available. If no async interface is implemented, the sync implementation is used.

- The Server already allows to register fully async NodeManagers, which implement the `IAsyncNodeManager` interface. To register a fully async Nodemanager use `StandardServer.RegisterNodeManager(IAsyncNodeManagerFactory)`.
  For compatibility reasons the IAsyncNodeManager has a property `SyncNodeManager`, this needs to be implemented by passing your IAsyncNodeManager to the `SyncNodeManagerAdapter`.

## Async Method call

Support for async method callbacks is already implemented by `CustomNodeManager2` to enable the support just add `IAsyncNodeManager` to your NodeManager implementation.
All generated code already has support for Async Methods e.g. `UpdateCertificateMethodState.OnCallAsync`. If the NodeManager implements `IAsyncNodeManager` the async callback is used automatically.
If a generic Method handler shall be used the `MethodState.OnCallMethod2Async` handler shall be used.


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

A second `SemaphoreSlim(1, 1)` that serialises all **monitored-item management** operations
(create, modify, delete, set-monitoring-mode, subscribe-to-events, condition-refresh, transfer).
This keeps subscription state consistent without blocking reads or writes:

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
  subscribers immediately by calling `NodeState.ClearChangeMasks` after every successful write.
  No background threads are created per subscription.
- **`SamplingGroupMonitoredItemManager`**: a background timer thread samples the current node
  value at the negotiated `SamplingInterval`. Write changes are *not* pushed immediately; instead
  the next scheduled sample detects and delivers them.  Choose this mode when the data source
  produces values independently of OPC UA write requests (e.g. hardware polling).

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
