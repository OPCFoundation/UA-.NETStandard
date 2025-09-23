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