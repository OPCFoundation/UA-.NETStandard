# CoreNodeManager vs CustomNodeManager2

This document outlines the key differences in behavior and implementation between `CoreNodeManager` and `CustomNodeManager2` within the OPC UA .NET Standard Stack.

`CoreNodeManager` is typically used for managing the internal nodes of the Server (Namespace 0) or simple static node sets. `CustomNodeManager2` is designed as a base class for developers implementing custom node managers with specific business logic, dynamic behavior, or backing stores.

## 1. Storage & Data Structures

| Feature | CoreNodeManager | CustomNodeManager2 |
| :--- | :--- | :--- |
| **Node Storage** | Uses a `NodeTable` (`m_nodes`) internally. | Uses a `NodeIdDictionary<NodeState>` (`PredefinedNodes`). |
| **Node Type** | Manages `ILocalNode` interface objects. | Manages `NodeState` objects (and subclasses). |
| **Handle Type** | `GetManagerHandle` returns the `ILocalNode` instance directly. | `GetManagerHandle` returns a `NodeHandle` wrapper containing the `NodeState` and validation status. |
| **Locking** | Uses `DataLock` (object). | Uses `Lock` (object). |
| **Namespace** | Typically manages dynamic nodes in specific indexes or internal server nodes. | Designed to manage specific namespaces passed in the constructor. Uses `IsNodeIdInNamespace` checks. |

## 2. Extensibility

| Feature | CoreNodeManager | CustomNodeManager2 |
| :--- | :--- | :--- |
| **Design Intent** | Sealed-like behavior. Not primarily designed for inheritance or overriding behavior. | Highly extensible. Most methods (`Read`, `Write`, `Browse`, `Call`) are `virtual` to allow custom overrides. |
| **Node Factory** | Does not implement `INodeIdFactory`. | Implements `INodeIdFactory` to generate new NodeIds for the system context. |
| **Address Space** | `CreateAddressSpace` is often empty (`ImportNodes` is used instead). | `CreateAddressSpace` invokes `LoadPredefinedNodes` to load nodes from resources/assemblies. |

## 3. Operational Behavior

### Reading & Writing

* **CoreNodeManager**:
  * **Read**: Directly invokes `ILocalNode.Read`.
  * **Write**: Performs basic type checking (expected data type/value rank) and invokes `ILocalNode.Write`.
* **CustomNodeManager2**:
  * **Read**: Validates the node handle, supports operation caching, and invokes `NodeState.ReadAttribute`. Handles timestamp synchronization (e.g., matching ServerTimestamp to SourceTimestamp for Value attributes).
  * **Write**:
    * Performs **Range Checks** for `AnalogItemState` (InstrumentRange).
    * Generates **Audit Events** (`Server.ReportAuditWriteUpdateEvent`).
    * Detects **Semantic Changes** (e.g., changes to `EURange`, `EnumStrings`) and updates monitored items accordingly.

### Method Calls

* **CoreNodeManager**:
  * **Browse**: Iterates over references stored in `ILocalNode`. Basic masking and filtering.
  * **Translate**: Basic search through internal references.
* **CustomNodeManager2**:
  * **Browse**: Uses `NodeState.CreateBrowser`. Explicitly validates `PermissionType.Browse`. Supports Views (`IsNodeInView`).
  * **Translate**: Uses `CreateBrowser` to navigate path. Supports resolving targets in other node managers via `unresolvedTargetIds`.

### Runtime subtype replacement (`IPredefinedNodeSubtypeReplacer`)

`AsyncCustomNodeManager` implements the `IPredefinedNodeSubtypeReplacer` capability interface. It swaps an already-registered predefined instance node for a **differently-typed instance** (typically a generated subtype) at runtime, while preserving the node's identity in the address space:

* the replacement inherits the existing node's `NodeId`, `BrowseName`, `SymbolicName`, `DisplayName` and `ReferenceTypeId`;
* children shared by both types (matched by `BrowseName` at any depth) keep the existing child's `NodeId` and value, so well-known instance NodeIds survive the swap;
* children that only exist on the replacement take their `NodeId` from a caller-supplied `BrowseName → NodeId` map, or a freshly minted one;
* the old subtree is removed and the new one registered in the manager's `PredefinedNodes` index, and a `ModelChange` is emitted (subject to `ModelChangeEmissionEnabled`) so live clients observe the new type definition and members.

**When to use it.** Reach for this capability when a well-known instance node's concrete type is a *runtime* decision — for example modelling `Server.ServerRedundancy` as `TransparentRedundancyType` vs `NonTransparentRedundancyType` from configuration, and changing that mode live (see `Opc.Ua.Redundancy.Server.ServerRedundancyController`). It is the right tool whenever you would otherwise mutate a node's `TypeDefinitionId` in place and hand-build the subtype-specific children.

**When not to use it.** If you only need to re-index an already-reparented replacement of the *same* type (e.g. promoting a passive nodeset node to a typed proxy), the lighter `ReplacePredefinedNode(nodeId, node)` index-only swap is sufficient — this is what `RoleStateBinding` and the `ConfigurationNodeManager` passive→typed promotion do today. If you are *creating* a new node subtree, use `AddNodeAsync` / `AddPredefinedNodeAsync` or the fluent `CreateInstance<TState>(...)` builder instead.

Create the replacement with the generated `CreateInstanceOf<Type>` factory, then hand it to the capability:

```csharp
// server.DiagnosticsNodeManager (or any AsyncCustomNodeManager) exposes the capability.
if (server.DiagnosticsNodeManager is IPredefinedNodeSubtypeReplacer replacer)
{
    ISystemContext context = server.DefaultSystemContext;
    ServerObjectState serverObject = server.ServerObject;
    var existing = serverObject.ServerRedundancy;

    // Build the target subtype instance (typed, generated).
    NonTransparentRedundancyState subtype = context.CreateInstanceOfNonTransparentRedundancyType();

    await replacer.ReplacePredefinedInstanceSubtypeAsync(
        context,
        existing,
        subtype,
        // well-known NodeIds for members that only exist on the subtype
        newChildNodeIds: new Dictionary<QualifiedName, NodeId>
        {
            [new QualifiedName(BrowseNames.ServerUriArray, 0)]
                = VariableIds.Server_ServerRedundancy_ServerUriArray
        },
        // keep the parent's typed backing slot in sync (setters don't reparent)
        onReplaced: node => serverObject.ServerRedundancy = (ServerRedundancyState)node,
        cancellationToken);
}
```

The operation is deliberately exposed as a capability interface method rather than a construction-time fluent builder: the fluent `INodeBuilder` surface models building a node *before* it is registered, whereas subtype replacement mutates a node that is already live in the address space. Callers that already hold a fluent builder can still create the replacement instance with `CreateInstance<TState>(...)` and then pass the built node to the capability.

## 4. Monitoring & Subscriptions

| Feature | CoreNodeManager | CustomNodeManager2 |
| :--- | :--- | :--- |
| **Manager** | Uses `SamplingGroupManager` directly. | Uses `IMonitoredItemManager` abstraction (defaults to `SamplingGroupMonitoredItemManager` or `MonitoredNodeMonitoredItemManager`). |
| **Filter Validation** | Validates `DataChangeFilter` specifically (deadband, EU Range). | Delegates validation to `ValidateMonitoringFilter`, supports `AggregateFilter` (if supported by server) and `DataChangeFilter`. |
| **Events** | Basic event subscription support (`SubscribeToEvents` checks `EventNotifier` bit). | **Full Event Support**: <br/>- Manages `RootNotifiers`. <br/>- Propagates events via `SubscribeToAllEvents`. <br/>- Implements `ConditionRefresh`. <br/>- Validates `PermissionType.ReceiveEvents`. |

## 5. History

* **CoreNodeManager**:
  * `HistoryRead` / `HistoryUpdate`: Iterates nodes and returns `BadNotReadable` / `BadNotWritable` (or `BadHistoryOperationUnsupported` implicit). No infrastructure for history.
* **CustomNodeManager2**:
  * Provides scaffold methods (`HistoryReadRawModified`, `HistoryReadProcessed`, `HistoryUpdateData`, etc.).
  * Checks `AccessLevels.HistoryRead/Write` and `EventNotifier.HistoryRead/Write`.
  * Default implementation returns `BadHistoryOperationUnsupported`, but is structured for easy overriding in derived classes.

## 6. Security

* **CoreNodeManager**:
  * Checks `AccessLevel`, `UserAccessLevel`, `WriteMask` in `Write`.
  * Loads Role Permissions into metadata.
* **CustomNodeManager2**:
  * Explicitly calls `MasterNodeManager.ValidateRolePermissions` during `Browse`, `Call`, and Event processing.
  * Reads and caches validation attributes (`AccessRestrictions`, `RolePermissions`) for optimized access.
