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
*   **CoreNodeManager**:
    *   **Read**: Directly invokes `ILocalNode.Read`.
    *   **Write**: Performs basic type checking (expected data type/value rank) and invokes `ILocalNode.Write`.
*   **CustomNodeManager2**:
    *   **Read**: Validates the node handle, supports operation caching, and invokes `NodeState.ReadAttribute`. Handles timestamp synchronization (e.g., matching ServerTimestamp to SourceTimestamp for Value attributes).
    *   **Write**:
        *   Performs **Range Checks** for `AnalogItemState` (InstrumentRange).
        *   Generates **Audit Events** (`Server.ReportAuditWriteUpdateEvent`).
        *   Detects **Semantic Changes** (e.g., changes to `EURange`, `EnumStrings`) and updates monitored items accordingly.

### Method Calls
*   **CoreNodeManager**:
    *   **Browse**: Iterates over references stored in `ILocalNode`. Basic masking and filtering.
    *   **Translate**: Basic search through internal references.
*   **CustomNodeManager2**:
    *   **Browse**: Uses `NodeState.CreateBrowser`. Explicitly validates `PermissionType.Browse`. Supports Views (`IsNodeInView`).
    *   **Translate**: Uses `CreateBrowser` to navigate path. Supports resolving targets in other node managers via `unresolvedTargetIds`.

## 4. Monitoring & Subscriptions

| Feature | CoreNodeManager | CustomNodeManager2 |
| :--- | :--- | :--- |
| **Manager** | Uses `SamplingGroupManager` directly. | Uses `IMonitoredItemManager` abstraction (defaults to `SamplingGroupMonitoredItemManager` or `MonitoredNodeMonitoredItemManager`). |
| **Filter Validation** | Validates `DataChangeFilter` specifically (deadband, EU Range). | Delegates validation to `ValidateMonitoringFilter`, supports `AggregateFilter` (if supported by server) and `DataChangeFilter`. |
| **Events** | Basic event subscription support (`SubscribeToEvents` checks `EventNotifier` bit). | **Full Event Support**: <br/>- Manages `RootNotifiers`. <br/>- Propagates events via `SubscribeToAllEvents`. <br/>- Implements `ConditionRefresh`. <br/>- Validates `PermissionType.ReceiveEvents`. |

## 5. History

*   **CoreNodeManager**:
    *   `HistoryRead` / `HistoryUpdate`: Iterates nodes and returns `BadNotReadable` / `BadNotWritable` (or `BadHistoryOperationUnsupported` implicit). No infrastructure for history.
*   **CustomNodeManager2**:
    *   Provides scaffold methods (`HistoryReadRawModified`, `HistoryReadProcessed`, `HistoryUpdateData`, etc.).
    *   Checks `AccessLevels.HistoryRead/Write` and `EventNotifier.HistoryRead/Write`.
    *   Default implementation returns `BadHistoryOperationUnsupported`, but is structured for easy overriding in derived classes.

## 6. Security

*   **CoreNodeManager**:
    *   Checks `AccessLevel`, `UserAccessLevel`, `WriteMask` in `Write`.
    *   Loads Role Permissions into metadata.
*   **CustomNodeManager2**:
    *   Explicitly calls `MasterNodeManager.ValidateRolePermissions` during `Browse`, `Call`, and Event processing.
    *   Reads and caches validation attributes (`AccessRestrictions`, `RolePermissions`) for optimized access.
