# Node Managers

## Table of contents

- [Overview](#overview)
- [Built-in node managers](#built-in-node-managers)
  - [Master node manager](#master-node-manager)
  - [Core node manager](#core-node-manager)
  - [Diagnostics and configuration node manager](#diagnostics-and-configuration-node-manager)
  - [Managers supplied by server features](#managers-supplied-by-server-features)
  - [Runtime lifecycle provider](#runtime-lifecycle-provider)
- [Core vs custom node managers](#core-vs-custom-node-managers)
  - [Storage and data structures](#storage-and-data-structures)
  - [Extensibility](#extensibility)
  - [Operational behaviour](#operational-behaviour)
    - [Reading & Writing](#reading--writing)
    - [Method Calls](#method-calls)
    - [Runtime subtype replacement (IPredefinedNodeSubtypeReplacer)](#runtime-subtype-replacement-ipredefinednodesubtypereplacer)
  - [Monitoring and subscriptions](#monitoring-and-subscriptions)
    - [Sampling interval revision](#sampling-interval-revision)
  - [History](#history)
  - [Security](#security)
  - [Threading contract for nodes and browsers](#threading-contract-for-nodes-and-browsers)
- [Registering node managers](#registering-node-managers)
  - [Startup registration](#startup-registration)
  - [Runtime registration](#runtime-registration)
  - [Node manager lifecycle impact on clients](#node-manager-lifecycle-impact-on-clients)
    - [MonitoredItems](#monitoreditems)
    - [Continuation points](#continuation-points)
    - [Namespaces](#namespaces)
    - [DataTypes](#datatypes)
    - [Change notifications](#change-notifications)
  - [How to make a node manager reloadable](#how-to-make-a-node-manager-reloadable)
  - [Related documentation](#related-documentation)
- [Server address-space metadata](#server-address-space-metadata)
  - [NamespaceMetadata for every namespace](#namespacemetadata-for-every-namespace)
    - [Node-manager authoring note](#node-manager-authoring-note)
  - [Historical-access reconciliation](#historical-access-reconciliation)
  - [See also](#see-also)
- [Source-generated node managers](#source-generated-node-managers)
  - [What the generator produces](#what-the-generator-produces)
  - [Opting in](#opting-in)
    - [Per-class opt-in via [NodeManager] (recommended)](#per-class-opt-in-via-nodemanager-recommended)
    - [Project-wide opt-in via MSBuild property (legacy)](#project-wide-opt-in-via-msbuild-property-legacy)
  - [Wiring callbacks: the Configure partial](#wiring-callbacks-the-configure-partial)
    - [Addressing modes](#addressing-modes)
    - [On-demand virtual node families](#on-demand-virtual-node-families)
    - [Monitored-item creation and lifecycle](#monitored-item-creation-and-lifecycle)
    - [Creating nodes under other managers' nodes (Objects folder)](#creating-nodes-under-other-managers-nodes-objects-folder)
  - [Typed model-traversal — the Configure(I{Manager}NodeManagerBuilder) partial](#typed-model-traversal--the-configureimanagernodemanagerbuilder-partial)
    - [What the generator emits per model](#what-the-generator-emits-per-model)
    - [Methods with arguments — typed OnCall overloads](#methods-with-arguments--typed-oncall-overloads)
  - [Event sources — typed Publish&lt;TEvent&gt; on notifier wrappers](#event-sources--typed-publishtevent-on-notifier-wrappers)
    - [Where the typed overload appears](#where-the-typed-overload-appears)
    - [Two registration shapes](#two-registration-shapes)
    - [Tuning lifecycle with EventPublishOptions](#tuning-lifecycle-with-eventpublishoptions)
    - [Hand-written node managers](#hand-written-node-managers)
  - [Single-file Program.cs — what it looks like](#single-file-programcs--what-it-looks-like)
  - [Multi-namespace and manager-swap subclassing](#multi-namespace-and-manager-swap-subclassing)
  - [NativeAOT publishing](#nativeaot-publishing)
  - [Runtime NodeSet alternative](#runtime-nodeset-alternative)
  - [Building richer node managers — the fluent extension surface](#building-richer-node-managers--the-fluent-extension-surface)
    - [Engineering units & EU range](#engineering-units--eu-range)
    - [Bulk property initialisation](#bulk-property-initialisation)
    - [References & dynamic child objects](#references--dynamic-child-objects)
    - [Creating instances of model types](#creating-instances-of-model-types)
    - [Alarm setup (MVP)](#alarm-setup-mvp)
    - [Boolean supervision → alarm activation (NAMUR pattern)](#boolean-supervision--alarm-activation-namur-pattern)
    - [Simulation timers](#simulation-timers)
    - [Pushing runtime value changes to subscribers](#pushing-runtime-value-changes-to-subscribers)
    - [Subscription-gated sources](#subscription-gated-sources)
    - [Multi-model composition](#multi-model-composition)
    - [Mixing ModelDesign and NodeSet2 in one project](#mixing-modeldesign-and-nodeset2-in-one-project)
    - [NodeSet2 access-level bitmasks](#nodeset2-access-level-bitmasks)
  - [Materialising instances at runtime — NodeId assignment](#materialising-instances-at-runtime--nodeid-assignment)
  - [Current limitations](#current-limitations)
  - [Sample](#sample)

## Overview

A node manager is the server-side component that owns a portion of the server address space and implements the service behavior for the nodes in that portion. In this stack a node manager is an `IAsyncNodeManager` (or the older synchronous `INodeManager` adapted to it) that can create nodes during address-space startup, return manager handles for `NodeId`s it owns, browse and translate references, read and write attributes, dispatch methods, validate monitored items, and participate in history, events, and node-management services.

`StandardServer` creates a `MasterNodeManager` while the server starts. The master node manager is the server's routing layer: OPC UA service implementations call it, and it dispatches each operation to the node manager that owns the requested node. Ownership is resolved primarily from the `NodeId.NamespaceIndex`, then confirmed by asking the candidate manager for a handle. This keeps application models, runtime NodeSets, diagnostics, configuration, and namespace 0 infrastructure independent while presenting one coherent address space to clients.

Developers need to care about node managers when they expose application data, methods, events, alarms, file-system objects, alias names, runtime NodeSets, or companion-spec models from a server. A node manager is also where model-specific behavior is attached: read and write callbacks, method callbacks, historian providers, event notifiers, permissions, model-change notifications, and cross-manager references to nodes owned elsewhere. For simple generated models, the source generator and fluent builder hide much of the plumbing; for dynamic or backed-by-service models, a custom manager is the boundary between the OPC UA services and the application's data source.

The server builds the initial set of node managers before accepting connections. Additional managers can be registered by hosting extensions such as `AddNodeManager` and `AddRuntimeNodeSet`, and the lifecycle API can add, reload, or remove lifecycle-managed managers while the server is running. Regardless of how a manager is supplied, it must cooperate with the master node manager's routing and reference-merging rules so clients can browse, monitor, and call nodes consistently across namespace and manager boundaries.

## Built-in node managers

Every `StandardServer` creates a `MasterNodeManager` and asks the server's `IMainNodeManagerFactory` for the main managers that are always present. The default `MainNodeManagerFactory` creates one `ConfigurationNodeManager` and one `CoreNodeManager`; application-provided managers from `AddNodeManager` or derived-server overrides are appended after those built-ins.

### Master node manager

`MasterNodeManager` is not an address-space model in the same sense as an application manager. It is the coordinator that the server stores as `IServerInternal.NodeManager` and exposes through `IMasterNodeManager`. Service implementations route through it for reads, writes, browsing, TranslateBrowsePaths, method calls, node management, monitored-item setup, history, and event-related operations.

Internally, service-call dispatch and lifecycle coordination are separated. The session-service implementations live in the internal `NodeManagerServiceDispatcher`, which depends only on the lock-free routing-table snapshot and never acquires lifecycle semaphores; `MasterNodeManager` keeps the public service surface (every virtual entry point and protected helper delegates to the dispatcher, so derived classes are unaffected) together with NodeManager lifecycle coordination. The node-management services (AddNodes, DeleteNodes, AddReferences, DeleteReferences) sit between the two: they dispatch per item like other services but serialize address-space mutation with runtime NodeManager lifecycle operations.

The master node manager builds a routing table keyed by namespace index. During construction it ensures the configured dynamic namespace URI is present, registers the configuration/diagnostics manager first, registers the core node manager second, and then registers application managers. For a service request, `GetManagerHandleAsync` uses the `NodeId.NamespaceIndex` to find the candidate manager list and asks each candidate for a handle until one claims the node. If no explicit route exists for the namespace, it falls back to the core node manager. This means a namespace route is a candidate list, not a single-owner map.

Multiple managers can serve the same namespace. `RegisterNamespaceManager(string namespaceUri, IAsyncNodeManager nodeManager)` appends a manager to the namespace route instead of replacing the existing route; the routing table also preserves manager order during lifecycle replacement. This is important for namespace 0 and for generated or runtime models that add nodes in namespaces already used by another manager.

The master node manager also merges references across managers. Each manager receives the shared `externalReferences` table while `CreateAddressSpaceAsync` runs. After all managers have created their nodes and historical-access advertisement has been reconciled, the master calls `AddReferencesAsync(externalReferences, ...)` on every manager. `MasterNodeManager.CreateExternalReference` is the helper most custom managers use to put a cross-manager reference in that table. The target manager then materialises the reference on the node it owns, so a node owned by one manager can be browseable from a parent, folder, metadata object, or notifier owned by another manager. Attaching a child only in the source manager is not enough when the source and target owners differ.

At runtime, the same reference handling is used for lifecycle-managed managers. A prepared manager builds its address space while hidden from client routing, the master adds its external references during commit, and rollback/removal removes the cross-manager references that were added for that generation.

### Core node manager

`CoreNodeManager` is the always-present manager for the core address-space infrastructure. The default master-node-manager constructor registers it for namespace index 0 and for the built-in server namespace route, and it also uses it as the fallback when a namespace has no explicit route. `CoreNodeManager` derives from `AsyncCustomNodeManager`, implements `ICoreNodeManager`, and uses sampling groups for monitored items.

The core manager owns and imports built-in nodes that other server components need to expose as part of the standard server address space. It is also the target for nodes loaded by the diagnostics/configuration manager from generated model output: `DiagnosticsNodeManager.CreateAddressSpaceAsync` loads predefined diagnostics/configuration nodes and then imports them into the core manager with `ImportNodesAsync(..., isInternal: true)`. When application nodes are imported with `isInternal: false`, the core manager updates the diagnostics manager so diagnostics metadata stays in sync.

### Diagnostics and configuration node manager

The default configuration and diagnostics manager is a single object. `MainNodeManagerFactory.CreateConfigurationNodeManager` creates a `ConfigurationNodeManager`; `ConfigurationNodeManager` derives from `DiagnosticsNodeManager` and implements `IConfigurationNodeManager`. `ServerInternalData.SetNodeManager` assigns `DiagnosticsNodeManager`, `ConfigurationNodeManager`, and `CoreNodeManager` from the master node manager, and the master exposes both diagnostics and configuration properties from index 0 of its manager list. In the default server, therefore, `ServerInternal.DiagnosticsNodeManager` and `ServerInternal.ConfigurationNodeManager` refer to the same `ConfigurationNodeManager` instance through different interfaces.

As a diagnostics manager, it loads the standard diagnostics and server-support nodes generated for the stack, manages session and subscription diagnostics, diagnostics enable/disable state, aggregate functions, event notifier updates, and the well-known OPC UA Part 17 alias-name methods that dispatch through the server-wide alias-name registry. It registers namespace URIs for the OPC UA namespace and the diagnostics namespace.

As a configuration manager, the same instance exposes push certificate-management and server-configuration functionality from OPC UA Part 12. It owns the server-configuration methods and state that interact with trust lists, certificate groups, transaction coordination, pending regenerated keys, endpoint and listener registries, and post-`ApplyChanges` effects. The class is a partial split by concern (`ConfigurationNodeManager.PushMethods.cs`, `.PushValidation.cs`, `.CertificateSlots.cs`, `.ApplyChanges.cs`, `.TrustMaterial.cs`, `.CertificateAlarms.cs`, `.NamespaceMetadata.cs`) over one core file, and delegates namespace-metadata tracking and alarm scheduling to the internal `NamespaceMetadataRegistry` and `CertificateAlarmScheduler` collaborators.

### Managers supplied by server features

The default server does not create file-system, alias-name, or runtime-NodeSet managers unless the application opts in. Hosting extensions register their factories as normal startup node-manager factories: for example, file-system support uses `FileSystemNodeManagerFactory`, runtime NodeSet loading uses `RuntimeNodeSetNodeManagerFactory`, and alias-name support can use `AliasNameNodeManager`. Once registered, these managers are routed by the same master-node-manager table and follow the same cross-reference and lifecycle rules as hand-written or source-generated managers.

### Runtime lifecycle provider

`StandardServer` also creates a `NodeManagerLifecycle` provider. It is not itself a node manager; it is the host control-plane object behind `INodeManagerLifecycle`. Hosted servers expose it through dependency injection, and direct `StandardServer` users can access `StandardServer.NodeManagerLifecycle`. The lifecycle provider prepares, commits, reloads, removes, and drains lifecycle-managed managers through the master node manager.

## Core vs custom node managers

This document outlines the key differences in behavior and implementation between `CoreNodeManager` and `CustomNodeManager2` within the OPC UA .NET Standard Stack.

`CoreNodeManager` is typically used for managing the internal nodes of the Server (Namespace 0) or simple static node sets. `CustomNodeManager2` is designed as a base class for developers implementing custom node managers with specific business logic, dynamic behavior, or backing stores.

### Storage and data structures

| Feature | CoreNodeManager | CustomNodeManager2 |
| :--- | :--- | :--- |
| **Node Storage** | Uses a `NodeTable` (`m_nodes`) internally. | Uses a `NodeIdDictionary<NodeState>` (`PredefinedNodes`). |
| **Node Type** | Manages `ILocalNode` interface objects. | Manages `NodeState` objects (and subclasses). |
| **Handle Type** | `GetManagerHandle` returns the `ILocalNode` instance directly. | `GetManagerHandle` returns a `NodeHandle` wrapper containing the `NodeState` and validation status. |
| **Locking** | Uses `DataLock` (object). | Uses `Lock` (object). |
| **Namespace** | Typically manages dynamic nodes in specific indexes or internal server nodes. | Designed to manage specific namespaces passed in the constructor. Uses `IsNodeIdInNamespace` checks. |

### Extensibility

| Feature | CoreNodeManager | CustomNodeManager2 |
| :--- | :--- | :--- |
| **Design Intent** | Sealed-like behavior. Not primarily designed for inheritance or overriding behavior. | Highly extensible. Most methods (`Read`, `Write`, `Browse`, `Call`) are `virtual` to allow custom overrides. |
| **Node Factory** | Does not implement `INodeIdFactory`. | Implements `INodeIdFactory` to generate new NodeIds for the system context. |
| **Address Space** | `CreateAddressSpace` is often empty (`ImportNodes` is used instead). | `CreateAddressSpace` invokes `LoadPredefinedNodes` to load nodes from resources/assemblies. |

### Operational behaviour

#### Reading & Writing

* **CoreNodeManager**:
  * **Read**: Directly invokes `ILocalNode.Read`.
  * **Write**: Performs basic type checking (expected data type/value rank) and invokes `ILocalNode.Write`.
* **CustomNodeManager2**:
  * **Read**: Validates the node handle, supports operation caching, and invokes `NodeState.ReadAttribute`. Handles timestamp synchronization (e.g., matching ServerTimestamp to SourceTimestamp for Value attributes).
  * **Write**:
    * Performs **Range Checks** for `AnalogItemState` (InstrumentRange).
    * Generates **Audit Events** (`Server.ReportAuditWriteUpdateEvent`).
    * Detects **Semantic Changes** (e.g., changes to `EURange`, `EnumStrings`) and updates monitored items accordingly.

#### Method Calls

* **CoreNodeManager**:
  * **Browse**: Iterates over references stored in `ILocalNode`. Basic masking and filtering.
  * **Translate**: Basic search through internal references.
* **CustomNodeManager2**:
  * **Browse**: Uses `NodeState.CreateBrowser`. Explicitly validates `PermissionType.Browse`. Supports Views (`IsNodeInView`).
  * **Translate**: Uses `CreateBrowser` to navigate path. Supports resolving targets in other node managers via `unresolvedTargetIds`.

#### Runtime subtype replacement (`IPredefinedNodeSubtypeReplacer`)

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

### Monitoring and subscriptions

| Feature | CoreNodeManager | CustomNodeManager2 |
| :--- | :--- | :--- |
| **Manager** | Uses `SamplingGroupManager` directly. | Uses `IMonitoredItemManager` abstraction (defaults to `SamplingGroupMonitoredItemManager` or `MonitoredNodeMonitoredItemManager`). |
| **Filter Validation** | Validates `DataChangeFilter` specifically (deadband, EU Range). | Delegates validation to `ValidateMonitoringFilter`, supports `AggregateFilter` (if supported by server) and `DataChangeFilter`. |
| **Events** | Basic event subscription support (`SubscribeToEvents` checks `EventNotifier` bit). | **Full Event Support**: <br/>- Manages `RootNotifiers`. <br/>- Propagates events via `SubscribeToAllEvents`. <br/>- Implements `ConditionRefresh`. <br/>- Validates `PermissionType.ReceiveEvents`. |

#### Sampling interval revision

When a client creates or modifies a monitored item the node manager revises the
requested `samplingInterval` before the server returns it in
`revisedSamplingInterval`. Three inputs take part:

| Input | Where it comes from |
| :--- | :--- |
| Requested sampling interval | `MonitoringParameters.SamplingInterval`; a negative value means "use the default sampling interval" (see below) |
| Node minimum | `BaseVariableState.MinimumSamplingInterval` of the monitored node, and only for the `Value` Attribute |
| Server minimum | `ServerConfiguration.MinSupportedSamplingInterval`, published in `Server.ServerCapabilities.MinSupportedSampleRate` |

The rule applied by `SubscriptionManager.CalculateRevisedSamplingInterval` is:

1. A requested interval below zero is resolved to the default sampling interval:
   the **publishing interval of the subscription** when the item is created, and
   the item's **current sampling interval** when it is modified. (The modify case
   preserves the behaviour of 1.5.378 and earlier, so a `ModifyMonitoredItems`
   call that leaves the sampling interval unspecified does not silently retune
   the item.)
2. If the node declares `MinimumSamplingIntervals.Continuous` (`0`) for the
   `Value` Attribute, it reports by exception and **no** lower bound is applied —
   the requested interval is returned unchanged.
3. Otherwise the interval is raised to the larger of the node minimum and
   `MinSupportedSamplingInterval`. Attributes other than `Value`, nodes that
   declare `MinimumSamplingIntervals.Indeterminate` (`-1`), and nodes that are
   not Variables are only bound by `MinSupportedSamplingInterval`.
4. `double.MaxValue` is capped to one year.

Event monitored items do not sample and are not affected.

`MinSupportedSamplingInterval` defaults to `0`, which means the server does not
impose a server-wide lower bound. Configure it in XML:

```xml
<ServerConfiguration>
  <!-- ... -->
  <MaxNotificationsPerPublish>1000</MaxNotificationsPerPublish>
  <MinSupportedSamplingInterval>2000</MinSupportedSamplingInterval>
  <!-- ... -->
</ServerConfiguration>
```

or with the fluent configuration builder:

```csharp
application.Build(applicationUri, productUri)
    .AsServer([endpointUrl])
    .SetMinSupportedSamplingInterval(2000);
```

With `MinSupportedSamplingInterval` set to 2000 ms, a client that requests a
10 ms sampling interval on a node that declares a minimum of 100 ms is revised
to 2000 ms; a node that declares 5000 ms still wins and is revised to 5000 ms.

`CustomNodeManager2` and `AsyncCustomNodeManager` pick the configured value up
automatically through their `MinSupportedSamplingInterval` property. Node
managers that implement `INodeManager` directly can call
`SubscriptionManager.CalculateRevisedSamplingInterval` to apply the same rule.

### History

* **CoreNodeManager**:
  * `HistoryRead` / `HistoryUpdate`: Iterates nodes and returns `BadNotReadable` / `BadNotWritable` (or `BadHistoryOperationUnsupported` implicit). No infrastructure for history.
* **CustomNodeManager2**:
  * Provides scaffold methods (`HistoryReadRawModified`, `HistoryReadProcessed`, `HistoryUpdateData`, etc.).
  * Checks `AccessLevels.HistoryRead/Write` and `EventNotifier.HistoryRead/Write`.
  * Default implementation returns `BadHistoryOperationUnsupported`, but is structured for easy overriding in derived classes.

### Security

* **CoreNodeManager**:
  * Checks `AccessLevel`, `UserAccessLevel`, `WriteMask` in `Write`.
  * Loads Role Permissions into metadata.
* **CustomNodeManager2**:
  * Explicitly calls `MasterNodeManager.ValidateRolePermissions` during `Browse`, `Call`, and Event processing.
  * Reads and caches validation attributes (`AccessRestrictions`, `RolePermissions`) for optimized access.

### Threading contract for nodes and browsers

A `NodeState` synchronizes itself. Its attributes, children, notifiers and references each sit
behind a private lock inside the node, and `NodeState.CreateBrowser` holds a browse lock while it
assembles the browser. **No caller — inside or outside the stack — may take a lock on a node
instance.** A `lock (node)` is a lock on a monitor that guards nothing: every path that touches the
node's data takes the node's own locks instead, so the two never interlock. This is enforced by
convention rather than by the compiler, so the rule is: if you feel the need to lock a node, the
operation you want is missing from `NodeState` — add it there.

| You want | Use |
| --- | --- |
| A consistent read of one attribute | `ReadAttribute` / `ReadAttributeAsync` — already guarded |
| A consistent read of several attributes | `ReadAttributes` — each attribute is guarded; there is deliberately no cross-attribute transaction |
| A consistent snapshot of the references | `GetReferences`, `GetChildren` — already guarded |
| Add a reference only if it is absent | `AddReferenceIfMissing` — the check and the insert are one critical section |
| Everything browsable, without locking the node | `CreateBrowser` — the node guards the build |

**What `CreateBrowser` does and does not promise.** Browser construction on a node is
serialized, so two concurrent browses do not interleave their `PopulateBrowser` /
`OnPopulateBrowser` work — a handler that mutates the node during population depends on that.
The browser is a point-in-time copy: later changes to the node do not appear in it. It is **not**
an atomic snapshot across the node's children, notifiers and references. Writers take those
collections' own locks, not the browse lock, so a browser built while a writer runs can pair
children from before a change with references from after it. Each collection is read
consistently; the combination is not a transaction. Making it one would mean funnelling every
child, notifier and reference write through the browse lock, which is a far larger contract than
browsing needs.

An **`INodeBrowser` is single-consumer.** It performs no synchronization of its own and belongs
to whoever created it. Where a browser outlives a single service call — the instance parked in a
continuation point for `BrowseNext` — its owner serializes access to it: `AsyncCustomNodeManager`
does so through the continuation point's `BrowserContext`, `CustomNodeManager2` with a lock
around the iteration. A derived browser must not add locking of its own; `NodeBrowser` no longer
exposes one to inherit (see [migration](migrate/2.0.x/node-states.md)).

Two rules follow for node types that customise browsing:

* An override of `PopulateBrowser` runs with the node's browse lock held. Keep it to in-memory
  work — the lock is held for its duration, so blocking on I/O there stalls every other browse of
  that node. A browser that has to reach an underlying system does that lazily in its own
  `Next()`, outside every node lock, as `DirectoryBrowser` does for the file-system provider.
* An override of `CreateBrowser` that builds its own browser instead of delegating to
  `base.CreateBrowser` must fill it through `PopulateBrowserSynchronized`. Calling
  `PopulateBrowser` directly leaves construction unserialized against other browses of the same
  node and skips `OnPopulateBrowser` altogether.

## Registering node managers

A NodeManager owns a part of the server address space. This section explains the three points at
which a NodeManager can be registered with a server, and what the server guarantees when
registrations change while the server is running.

For how to author a NodeManager, see [source-generated NodeManagers](#source-generated-node-managers),
[runtime NodeSets](RuntimeNodeSets.md), and
[CoreNodeManager vs CustomNodeManager2](#core-vs-custom-node-managers).

There are several ways a NodeManager originates and is added to a server, shown in the following
table.

| Registration point | API | When the address space is built |
| --- | --- | --- |
| Compile time | A source-generated or hand-written `AsyncCustomNodeManager` / `CustomNodeManager2` type | When the server creates its address space |
| Startup | `IOpcUaServerBuilder.AddNodeManager(...)`, `IOpcUaServerBuilder.AddRuntimeNodeSet(...)` | During `CreateAddressSpaceAsync`, before the server accepts connections |
| Runtime | `INodeManagerLifecycle.AddAsync` / `ReloadAsync` / `RemoveAsync` | While the server is running and serving Clients |

Compile-time and startup registration are the normal path. Use runtime registration only when the
set of models genuinely has to change without restarting the server.

### Startup registration

`AddNodeManager` and `AddRuntimeNodeSet` register a factory on `IOpcUaServerBuilder`. The factory is
created before the server starts, and the server builds its address space from all registered
factories while it starts.

```csharp
services.AddOpcUa()
    .AddServer(o => { /* … */ })
    .AddNodeManager(sp => new MyNodeManager(sp.GetRequiredService<ITelemetryContext>()));
```

For a one-shot fluent node manager, the callback creates and places the
complete contributed graph. The hosting API does not add an implicit root:

```csharp
const string namespaceUri = "urn:example:line";

services.AddOpcUa()
    .AddServer(o => { /* … */ })
    .AddNodeManager(namespaceUri, builder =>
    {
        ushort namespaceIndex =
            (ushort)builder.Context.NamespaceUris.GetIndex(namespaceUri);
        builder.CreateInstance(
                new QualifiedName("Line", namespaceIndex),
                parent => new FolderState(parent))
            .Configure(node => node.UnderObjectsFolder());
    });
```

### Runtime registration

A running server exposes `INodeManagerLifecycle`. Resolve it from dependency injection in a hosted
server, or use `StandardServer.NodeManagerLifecycle` when constructing the server directly.

```csharp
public sealed class ModelLoader(INodeManagerLifecycle lifecycle)
{
    private NodeManagerRegistration? m_registration;

    public async ValueTask LoadAsync(IAsyncNodeManagerFactory factory, CancellationToken ct)
    {
        m_registration = await lifecycle.AddAsync(factory, callerContext: null, ct);
    }

    public async ValueTask ReloadAsync(IAsyncNodeManagerFactory replacement, CancellationToken ct)
    {
        m_registration = await lifecycle.ReloadAsync(m_registration!, replacement, callerContext: null, ct);
    }

    public ValueTask RemoveAsync(CancellationToken ct)
    {
        return lifecycle.RemoveAsync(m_registration!, callerContext: null, ct);
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

Every lifecycle method takes the operation the caller is running under. Pass it from a NodeManager
or Method callback and the server rejects the call with an `InvalidOperationException` instead of
deadlocking:

```csharp
private async ValueTask<ServiceResult> OnReloadModelAsync(
    ISystemContext context,
    MethodState method,
    IList<Variant> inputArguments,
    IList<Variant> outputArguments,
    CancellationToken ct)
{
    // Throws InvalidOperationException: the call is serving a Client request.
    await m_lifecycle.ReloadAsync(
        m_registration,
        replacement,
        context.GetOperationContext(),
        ct);
    return ServiceResult.Good;
}
```

A control-plane caller — a hosted service, or anything resolved from dependency injection — is not
serving a request and passes `null`.

The guard is an identity check against the requests the server is currently executing, not ambient
state, so an internal operation that was never enrolled as a Client request is allowed through, and
a context whose request has already completed no longer blocks anything. A caller that is inside a
request but passes no operation is not detected; for that case the wait is bounded instead: it lasts
at most as long as the longest deadline still outstanding plus `RequestManager.RequestDrainTimeout`,
after which the lifecycle operation fails with a `TimeoutException` instead of blocking
indefinitely.

A server that rejects requests of its own by overriding `StandardServer.OnRequestValidatedAsync`
does not interfere with this: a rejected request is completed before the exception leaves the
server, so it never holds a lifecycle operation up.

A lifecycle operation is transactional. The replacement address space is built and validated before
anything becomes visible to Clients, and any failure is rolled back, so Clients never observe a
partially applied model.

### Node manager lifecycle impact on clients

#### Reload modes

All reload modes build and validate the replacement generation before Clients can see it. The
commit then switches new service requests to the replacement generation atomically. Namespace URI
indexes are append-only server state, so reloads can add namespace URIs but do not renumber
existing indexes. Requests that already captured the retired generation are allowed to complete
before the server detaches and disposes it.

The modes differ in the client contract for work already attached to the retired generation:

| Mode | Existing MonitoredItems | Browse continuation points and in-flight requests | When to choose it | Cost |
| --- | --- | --- | --- | --- |
| Normal reload, `ReloadAsync` | The server detaches items from the retired generation before the routing switch, attaches compatible items to the replacement after commit, and reports `BadNodeIdUnknown` once for removed or incompatible items. Subscriptions and compatible MonitoredItem ids are preserved. | Existing requests complete on the generation they captured. Continuation points that captured the retired generation are invalidated after that request drain because no old MonitoredItems remain to keep the generation alive. | The default for compatible model updates where Clients should keep subscriptions and receive a clear status only for removed nodes. | Requires the replacement to support monitored-item attachment and may fail the reload if an unexpected item incompatibility is detected. |
| Shadow reload, `ShadowReloadAsync` | Existing items stay on the retired generation and continue sampling there. New MonitoredItems are created on the replacement. The retired generation is disposed only after those old items are deleted, their Sessions close, or they otherwise drain. | Requests and continuation points that already captured the retired generation keep using it while it remains shadow-retired. Cleanup invalidates remaining continuation points only once no old MonitoredItems are active. | Use when existing subscriptions must keep exactly the old model semantics while new Clients move to the replacement, for example during long migrations or when compatible hand-over is not desirable. | Runs two generations at once, including the old sampling/event fan-out, so memory and model resources remain allocated until Clients drain. |
| Immediate reload, `ImmediateReloadAsync` | The server detaches every item owned by the retired generation and reports `BadNodeIdUnknown`; it does not try to attach compatible items to the replacement. Clients may recreate items against the new generation. | Existing requests complete on the generation they captured. Continuation points that captured the retired generation are invalidated after the request drain, and the old generation can then be detached promptly. | Use for destructive or security-sensitive changes where serving or migrating old items is worse than forcing Clients to resubscribe. | Causes deliberate subscription churn and one bad status per old data MonitoredItem. |

All three modes are intentional: normal reload preserves compatible subscriptions, shadow reload
preserves old subscriptions without migration, and immediate reload fails old subscriptions fast.

#### MonitoredItems

Active MonitoredItems survive reload and removal. A compatible NodeId in a replacement generation
keeps the same MonitoredItem and Subscription without a transient bad status. A removed or
incompatible NodeId is detached and publishes one `BadNodeIdUnknown` data-change notification, as
required by OPC UA Part 4 §5.8.4.1; adding a compatible Node with the same NodeId later revalidates
and reattaches the item automatically. Event MonitoredItems detach and recover their source binding
without synthesizing a data-change status.

That notification is queued in its natural position, because Part 4 §5.13.1.5 requires a Server to
return notifications in the order they are in the queue. It occupies an ordinary queue slot, but it
is the one value that is never discarded: once the queue is full, an incoming value is dropped
instead of the notification, so a full queue cannot swallow it.

This applies only when queuing is enabled. At the default `queueSize` of 1 the MonitoredItem has no
queue at all — the last sampled value is what the Client is served — so the notification simply
becomes that value, and a value sampled after the deletion replaces it in the usual way. Losing
values is the accepted behaviour of a MonitoredItem without queuing. Issue
[#4102](https://github.com/OPCFoundation/UA-.NETStandard/issues/4102) records the underlying
specification ambiguity: the protected, over-capacity slot the specification defines applies to
`EventQueueOverflowEventType` only, so it says nothing about how a mandatory data-change
notification survives a full queue. Only one such notification is pending at a time, and a pending
one is not preserved across a durable subscription restart.

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

#### Continuation points

Reload and removal invalidate saved Browse continuation points owned by the retired NodeManager. A
later `BrowseNext` with one of those tokens returns `BadContinuationPointInvalid` instead of
invoking a disposed generation.

#### Namespaces

Namespace indexes are append-only for the lifetime of a running server. Removing a model removes its
Nodes and routing but leaves its namespace URI in `NamespaceArray`, and a later reload or add reuses
the same index. When a live add appends a URI, the server updates `NamespaceArray` and `UrisVersion`.

A Client with [model change tracking](ModelChangeTracking.md) enabled re-reads its namespace table
when it observes the resulting model-change notification, so NodeIds from the newly added namespace
resolve without any application code. See
[Namespace table refresh](ModelChangeTracking.md#namespace-table-refresh). A Client that does not
track model changes keeps the namespace table it fetched while the Session was opened and has to
call `ISession.FetchNamespaceTablesAsync` itself.

#### DataTypes

Runtime DataType registrations are additive. Reload accepts an existing DataType only when its
definition is structurally compatible, rejects incompatible changes, and retains removed stand-in
encodeables so existing Sessions and in-flight values remain decodable.

#### Change notifications

Every committed lifecycle transaction emits one compressed model-change notification. Reload also
emits a semantic-change notification when values of Properties marked with the `SemanticChange`
access-level bit changed.

### How to make a node manager reloadable

A NodeManager can be added and removed through the lifecycle provider without extra work. Reload
needs more, because the references other NodeManagers hold into the retired address space have to be
carried over to the replacement. A NodeManager can only be reloaded when it implements
`INodeManagerReloadParticipant`; reloading one that does not fails with `NotSupportedException`
before anything changes.

The contract is a single method:

```csharp
public interface INodeManagerReloadParticipant
{
    ValueTask<ArrayOf<LocalReference>> PrepareReloadAsync(
        IAsyncNodeManager replacement,
        CancellationToken ct = default);
}
```

The server calls it on the outgoing generation, handing it the already-built replacement, before any
routing changes. The implementation has two jobs:

1. **Re-add the references your NodeManager contributed to Nodes it does not own.** These are the
   cross-manager references you registered while building the address space — for example a
   `Organizes` reference from the ns=0 `Objects` folder to your root. Track them as you add them, and
   in `PrepareReloadAsync` push the same set into the replacement so the foreign Nodes keep pointing
   at the new generation.
2. **Return the inbound references the replacement can no longer satisfy.** For every reference whose
   target NodeId the replacement does not contain, return a `LocalReference` describing the
   *counterpart* edge so the server can delete it from the foreign Node. `LocalReference` is
   `(NodeId sourceId, NodeId referenceTypeId, bool isInverse, NodeId targetId)`, so the counterpart is
   the reference with source and target swapped and `isInverse` negated.

A minimal implementation looks like this:

```csharp
public async ValueTask<ArrayOf<LocalReference>> PrepareReloadAsync(
    IAsyncNodeManager replacement,
    CancellationToken ct = default)
{
    if (replacement is not MyNodeManager target)
    {
        throw new NotSupportedException(
            "This NodeManager can only be reloaded with another instance of the same type.");
    }

    // 1. hand the references we added to foreign Nodes to the replacement.
    Dictionary<NodeId, IList<IReference>> addedReferences = GetAddedReferences();
    await target.AddReferencesAsync(addedReferences, ct).ConfigureAwait(false);

    // 2. report the counterparts whose target the replacement no longer has.
    var dropped = new List<LocalReference>();
    foreach (KeyValuePair<NodeId, IList<IReference>> entry in addedReferences)
    {
        if (target.ContainsNode(entry.Key))
        {
            continue;
        }

        foreach (IReference reference in entry.Value)
        {
            if (!reference.TargetId.IsAbsolute)
            {
                dropped.Add(new LocalReference(
                    (NodeId)reference.TargetId,
                    reference.ReferenceTypeId,
                    !reference.IsInverse,
                    entry.Key));
            }
        }
    }

    return new ArrayOf<LocalReference>(dropped.ToArray());
}
```

Practical guidance:

* **Keep a record of cross-manager references as you create them.** The base classes do not track
  them for you. `AddExternalReference` populates the `externalReferences` dictionary handed to
  `CreateAddressSpaceAsync`; keep that dictionary (or an equivalent map) in a field so
  `PrepareReloadAsync` can replay it.
* **Reject an incompatible replacement.** Throwing `NotSupportedException` when the replacement is
  not the type you expect is safer than silently skipping the reference transfer, and the server
  fails the reload cleanly.
* **Do not mutate your own Nodes.** By the time this runs, the replacement generation owns the
  address space; the retired generation is about to be disposed.
* **Reload is transactional.** If your implementation throws, the whole operation is rolled back and
  Clients never see a partially applied model, so it is safe to fail fast.

`RuntimeNodeSetNodeManager` is the only built-in NodeManager that implements the contract, so runtime
NodeSets are reloadable out of the box — see
`src/Opc.Ua.Server/RuntimeNodeSet/RuntimeNodeSetNodeManager.cs` for the reference implementation. A
NodeManager derived from `CustomNodeManager2` or `AsyncCustomNodeManager` can be added and removed
live without any of this, and becomes reloadable once it implements the interface.

### Related documentation

* [Runtime NodeSets](RuntimeNodeSets.md) — loading NodeSet2 XML without source generation.
* [Source-generated NodeManagers](#source-generated-node-managers) — compile-time models.
* [Dependency Injection](DependencyInjection.md) — the `services.AddOpcUa()` hosting surface.
* [Model Change Tracking](ModelChangeTracking.md) — how Clients observe address-space changes.

## Server address-space metadata

This guide covers two server-startup behaviours that keep the published
address space consistent with what the server can actually serve:

- namespace metadata objects under `Server/Namespaces`;
- historical-access advertisement on variables.

### NamespaceMetadata for every namespace

OPC UA Part 5 requires the `Server/Namespaces` object to describe the
namespaces exposed by a server. Companion specifications repeat the
same requirement in their namespace-metadata clauses so clients can
compare `NamespaceVersion` and `NamespacePublicationDate` against cached
models.

`StandardServer` calls the overridable
`PublishNamespaceMetadataAsync(IServerInternal, CancellationToken)` seam
during startup, after conformance units are published and before the
server accepts sessions. The default implementation uses
`NamespaceMetadataPublisher` to walk `NamespaceArray` and ensure every
namespace URI has a `NamespaceMetadataType` object under
`Server/Namespaces`.

For source-generated models, the publisher fills
`NamespaceVersion` and `NamespacePublicationDate` from the
`ModelDependencyAttribute` stamped on model assemblies. Existing
metadata objects and already-populated values are preserved.

#### Node-manager authoring note

Attaching a child to an object owned by another node manager is not
enough to make it browseable through the master node manager. This is
common for namespace metadata because `Server/Namespaces` is a namespace
0 object owned by the configuration node manager, while the metadata
object may be created by another manager. Register the link as a
cross-manager reference with `AddReferencesAsync` when the owner differs.
`NamespaceMetadataPublisher` does this check automatically for metadata
objects it creates.

Servers that publish namespace metadata themselves can override
`StandardServer.PublishNamespaceMetadataAsync` and either add custom
metadata or return without doing work.

### Historical-access reconciliation

Official companion NodeSets often declare `Historizing="true"` or set
`AccessLevel` bits such as `HistoryRead` on variables whose type is
capable of history. A concrete server still needs a historian provider
before it can serve `HistoryRead` or `HistoryUpdate` for those variables.

During master-node-manager startup, every `AsyncCustomNodeManager`
reconciles this advertisement before external references are applied.
For each variable that advertises historical access, the server checks
whether an `IHistorianProvider` resolves through:

1. the node manager's `GetHistorianProvider(NodeState)` override;
2. the server-wide historian registry (`RegisterForNode`,
   `RegisterForNamespace`, then `RegisterDefault`).

If no provider resolves, the server clears `Historizing` and masks
`HistoryRead` / `HistoryWrite` from `AccessLevel`,
`UserAccessLevel`, and the corresponding attribute read callbacks. This
keeps direct reads of the attributes consistent with the values stored
on the node.

Variables with a historian keep their NodeSet-declared history surface.
Use `builder.UseHistorian()` and `.Historize()` from the fluent server
API, or override `GetHistorianProvider`, when a NodeSet variable should
continue advertising historical access.

### See also

- [Historical Access](HistoricalAccess.md) — historian provider model
  and fluent `.Historize()` wiring.
- [Source-generated NodeManagers](#source-generated-node-managers) —
  NodeSet2 import, fluent node creation, and runtime instance NodeId
  assignment.

## Source-generated node managers

This guide explains how to use the OPC UA stack source generator to emit a
ready-to-host `AsyncCustomNodeManager` for an information model design XML, and
how to wire callbacks (read/write/method/lifecycle) using the fluent
`INodeManagerBuilder` API. The combination is designed for **single-file,
NativeAOT-friendly** servers — see
`samples/MinimalApi/MinimalBoilerServer` for the canonical sample.

### What the generator produces

The base source generator already emits, for each model design:

- `Add{Ns}(NodeStateCollection, ISystemContext)` — populates a node
  collection.
- `Add{Ns}(INodeStateFactoryBuilder)` — registers strongly-typed activators.
- `Add{Ns}DataTypes(IEncodeableFactoryBuilder)` — registers encodeables.

When `ModelSourceGeneratorGenerateNodeManager=true` is set **or** a
class is annotated with `[Opc.Ua.Server.Fluent.NodeManagerAttribute]`,
the generator **additionally** emits, in either the `{ModelNamespace}`
namespace (legacy MSBuild mode) or the user class's namespace
(attribute mode):

- `public partial class {Ns}NodeManager : AsyncCustomNodeManager` (legacy)
  or `public partial class {UserClass} : AsyncCustomNodeManager` (attribute)
  - Constructor `(IServerInternal, ApplicationConfiguration)`.
  - Pre-registers the model namespace URI.
  - `LoadPredefinedNodesAsync` returns
    `new NodeStateCollection().Add{Ns}(context)` wrapped in a
    `ValueTask<NodeStateCollection>`.
  - `CreateAddressSpaceAsync` `await`s `base.CreateAddressSpaceAsync`,
    then builds a fluent `INodeManagerBuilder`, invokes
    `Configure(builder)`, `await`s `CompleteConfigureAsync` (re-running
    the reverse-reference pass so nodes created inside `Configure`
    publish their references to nodes owned by other managers — e.g. an
    inverse `Organizes` to the ns=0 `Objects` folder — into the
    `externalReferences` dictionary), calls `builder.Seal()`, and
    replays `NotifyNodeAdded` for every predefined node so per-node
    lifecycle hooks fire deterministically.
  - `AddPredefinedNodeAsync` / `RemovePredefinedNodeAsync` overrides
    forward to base and then dispatch the lifecycle notification.
  - `OnMonitoredItemCreated` (still synchronous on the base) dispatches
    the per-node hook.
  - Declares `partial void Configure(INodeManagerBuilder builder);` for
    user wiring.
- `public class {Ns}NodeManagerFactory : IAsyncNodeManagerFactory`
  - Returns the namespace URI in `NamespacesUris`.
  - `CreateAsync(IServerInternal, ApplicationConfiguration, CancellationToken)`
    returns a `ValueTask<IAsyncNodeManager>` containing a new manager
    instance.
  - Both members are `virtual` so consumers can subclass to add a second
    namespace or swap in a manager subclass.

`AddNodeManager` on `StandardServer` has overloads for both
`INodeManagerFactory` and `IAsyncNodeManagerFactory`; the generated
async factory binds to the latter automatically.

### Opting in

Add the generator analyzer to your project (this is what
`OPCFoundation.Opc.Ua.SourceGeneration.props` is for) and choose **one**
of the two opt-in modes:

#### Per-class opt-in via `[NodeManager]` (recommended)

Annotate the user-authored partial class that should host the generated
manager:

```csharp
using Opc.Ua.Server.Fluent;

namespace MyCompany.MyServer;

[NodeManager]
public partial class MyDeviceNodeManager
{
    partial void Configure(INodeManagerBuilder builder)
    {
        // wire your callbacks here
    }
}
```

The generator emits a sibling `partial class MyDeviceNodeManager :
AsyncCustomNodeManager` and a `MyDeviceNodeManagerFactory` (implementing
`IAsyncNodeManagerFactory`) in the same namespace as the user class. No
MSBuild flag is required.

When a project carries multiple model designs, disambiguate which
design the attribute targets via either:

```csharp
[NodeManager(NamespaceUri = "http://opcfoundation.org/UA/Boiler/")]
```

or by file stem:

```csharp
[NodeManager(Design = "BoilerDesign")]
```

Set `GenerateFactory = false` to suppress factory emission when you want
to ship a hand-written `IAsyncNodeManagerFactory`.

When the manager also owns namespaces beyond the model's own — most
commonly a separate *instance* namespace for nodes created at runtime —
declare them on the attribute:

```csharp
[NodeManager(
    NamespaceUri = "http://opcfoundation.org/UA/Boiler/",
    AdditionalNamespaceUris = new[]
    {
        "http://opcfoundation.org/UA/Boiler/Instance"
    })]
```

The generated constructor passes them to the base manager together with
the model namespace and the generated factory advertises them in
`NamespacesUris`, so the master node manager routes requests for those
namespaces to this manager from the moment it is built. Calling
`SetNamespaces` later is not sufficient — the master builds its
namespace routing from what the manager reported when it was
constructed.

The URI expressions must be available to Roslyn before this generator
runs: use string literals, `const` values declared in ordinary source,
or constants from a referenced assembly. A constant emitted by another
generator in the **same compilation** is not available. Such an
expression reports `MODELGEN035` at the offending argument instead of
silently dropping the namespace from the generated manager and factory.

#### Project-wide opt-in via MSBuild property (legacy)

If you prefer a generator-derived class identity (`{Prefix}NodeManager` /
`{Prefix}NodeManagerFactory`) without authoring a stub partial, set the
opt-in property:

```xml
<PropertyGroup>
  <ModelSourceGeneratorGenerateNodeManager>true</ModelSourceGeneratorGenerateNodeManager>
</PropertyGroup>

<ItemGroup>
  <AdditionalFiles Include="Generated\MyModelDesign.xml" />
  <AdditionalFiles Include="Generated\MyModelDesign.csv" />
</ItemGroup>
```

This emits `{Prefix}NodeManager` + `{Prefix}NodeManagerFactory` for
every design in the project. Wire callbacks by adding a sibling
`partial class {Prefix}NodeManager` that implements `Configure`.

Without either opt-in, only the existing `Add{Ns}*` extensions are
emitted — hand-written `AsyncCustomNodeManager` (or legacy
`CustomNodeManager2`) subclasses keep working unchanged.

### Wiring callbacks: the `Configure` partial

Author a sibling partial that fills in `Configure`:

```csharp
namespace MyModel;

public partial class MyModelNodeManager
{
    // The source-generated constructor retains the exact startup
    // configuration on FluentNodeManagerBase. Use the protected property
    // from any user-authored partial; no custom factory is required.
    private MyModelConfiguration? Settings =>
        Configuration?.ParseExtension<MyModelConfiguration>();

    partial void Configure(INodeManagerBuilder builder)
    {
        builder
            .Node("Boilers/Boiler #1/Drum1001/LevelIndicator/Output")
            .OnRead(MyReadHandler);

        // Resolve a singleton instance by its TypeDefinitionId — stable
        // across deployments and independent of where the instance sits
        // in the tree. Ideal for well-known types like
        // HistoryServerCapabilities or a single BoilerType instance.
        builder
            .NodeFromTypeId(ExpandedNodeId.ToNodeId(
                MyModel.ObjectTypeIds.BoilerType, Server.NamespaceUris))
            .OnNodeAdded((ctx, node) => /* ... */);

        // For multi-instance types, disambiguate with a BrowseName:
        builder
            .NodeFromTypeId(
                ExpandedNodeId.ToNodeId(MyModel.ObjectTypeIds.BoilerType, Server.NamespaceUris),
                new QualifiedName("Boiler #2", nsIndex))
            .OnRead(MyReadHandler);
    }
}
```

Path syntax is `/`-separated **BrowseNames**, rooted at the model
namespace's predefined nodes. Optional `ns=N;` prefix lets you target a
different namespace.

#### Addressing modes

| Method | Resolves by | Use when |
|--------|-------------|----------|
| `Node(string path)` | BrowseName path | Deterministic tree layout, multiple siblings |
| `Node(NodeId id)` / `Node<TState>(NodeId id)` | Absolute NodeId | You own the id (e.g. generated `Variables.*`) |
| `NodeFromTypeId(NodeId typeId)` / `NodeFromTypeId<TState>(NodeId typeId)` | `BaseInstanceState.TypeDefinitionId` | Singleton instance of a well-known type |
| `NodeFromTypeId(NodeId typeId, QualifiedName browseName)` | TypeDefinitionId + BrowseName | Multi-instance types — pick one |

`NodeFromTypeId` walks every predefined node owned by this manager
(and their sub-trees) at Configure-time. Error matrix:

* `BadNodeIdInvalid` — `typeId` is null or `IsNull`.
* `BadNodeIdUnknown` — no instance carries that `TypeDefinitionId`, or
  the optional `browseName` disambiguator finds no match.
* `BadBrowseNameDuplicated` — more than one candidate and no
  disambiguator was supplied (or multiple candidates share the same
  `browseName`).
* `BadTypeMismatch` — typed overload's `TState` cast fails.

The builder exposes:

| Method | Wires |
|--------|-------|
| `OnRead` / `OnReadAsync` | `BaseVariableState.OnReadValue` |
| `OnWrite` / `OnWriteAsync` | `BaseVariableState.OnWriteValue` |
| `OnCall` / `OnCallAsync` | `MethodState.OnCallMethod*` |
| `OnNodeAdded` / `OnNodeRemoved` | Lifecycle dispatch from `NotifyNodeAdded` |
| `OnEvent`, `OnConditionRefresh`, `OnHistoryRead`, `OnHistoryUpdate` | Node or manager-level dispatch keyed by `NodeId` |
| `OnCreateMonitoredItem`, `OnMonitoredItemCreated`, `OnMonitoredItemModified`, `OnMonitoredItemDeleted`, `OnMonitoringModeChanged` | Data-change monitored-item creation and lifecycle |

`INodeManagerBuilder.NodeManager` is typed as `IAsyncNodeManager`. Use
`builder.NodeManager.SyncNodeManager` to obtain the synchronous
`INodeManager` facade for legacy interop, or cast it to your concrete
manager type if you need direct access.

Ordinary `Node(...)` / `Variable(...)` resolution happens **once**
during `CreateAddressSpaceAsync`, against the in-memory predefined-node
tree. Virtual node families are registered during the same phase but
materialize individual nodes per service operation as described below.
There is no reflection, no `Activator.CreateInstance`, no
`Expression.Compile` — the whole pipeline is NativeAOT-safe.

#### On-demand virtual node families

Use `ResolveNodes` when the manager owns a potentially large or external
address space that must not be copied into `PredefinedNodes`. The first
delegate is a cheap ownership test and must not perform I/O. The second
delegate materializes the requested `NodeState` asynchronously:

```csharp
partial void Configure(INodeManagerBuilder builder)
{
    builder.ResolveNodes(
            nodeId => TryParseRegisterId(nodeId, out _),
            async (context, nodeId, ct) =>
            {
                RegisterAddress address = ParseRegisterId(nodeId);
                RegisterMetadata? metadata =
                    await m_device.DescribeAsync(address, ct);
                if (metadata is null)
                {
                    return null;
                }

                return new BaseDataVariableState(parent: null)
                {
                    NodeId = nodeId,
                    BrowseName = new QualifiedName(metadata.Name, nodeId.NamespaceIndex),
                    DisplayName = metadata.Name,
                    DataType = metadata.DataType,
                    ValueRank = ValueRanks.Scalar
                };
            })
        .OnRead(ReadRegister)
        .OnWrite(WriteRegister)
        .OnCreateBrowser(CreateRegisterBrowser)
        .OnMonitoredItemCreated(StartPushSource);
}
```

Predefined nodes always win. On a predefined-node miss,
`FluentNodeManagerBase` selects exactly one matching virtual family,
creates an unvalidated `NodeHandle`, and invokes the resolver during
normal node validation. Overlapping predicates fail with
`BadConfigurationError` rather than depending on registration order.

The resolver may return `null` for a syntactically valid id whose backing
object does not exist. A returned node with `NodeId.Null` receives the
requested id; a conflicting non-null id is rejected. The stack caches the
result only in its existing per-operation and monitored-component caches:
virtual nodes are never inserted into `PredefinedNodes`.

The returned `IVirtualNodeBuilder` applies one callback template to every
materialized member of the family. It supports read/write/call,
condition/event, history, browser, monitored-item creation, and
monitored-item lifecycle hooks. `OnCreateBrowser` uses the ordinary
`NodeState.CreateBrowser` contract, so custom browsers still participate
in browse filtering, continuation points, and translate-path handling.

#### Monitored-item creation and lifecycle

`OnCreateMonitoredItem` runs before the default sampled item is
allocated. It can keep the default path, reject the request with an exact
status, or supply a factory for a custom
`ISampledDataChangeMonitoredItem`:

```csharp
builder.Node("Buffers/UInt32")
    .OnCreateMonitoredItem((request, ct) =>
    {
        if (!request.Request.RequestedParameters.Filter.IsNull)
        {
            return new ValueTask<MonitoredItemCreateDecision>(
                MonitoredItemCreateDecision.Refuse(
                    StatusCodes.BadFilterNotAllowed));
        }

        if (!request.Request.ItemToMonitor.ParsedIndexRange.IsNull)
        {
            return new ValueTask<MonitoredItemCreateDecision>(
                MonitoredItemCreateDecision.Refuse(
                    StatusCodes.BadIndexRangeInvalid));
        }

        return new ValueTask<MonitoredItemCreateDecision>(
            MonitoredItemCreateDecision.Use(
                factory => new BufferMonitoredItem(factory)));
    })
    .OnMonitoredItemCreated(OnCreated)
    .OnMonitoredItemModified(OnModifiedAsync)
    .OnMonitoringModeChanged(OnModeChangedAsync)
    .OnMonitoredItemDeleted(OnDeletedAsync);
```

The stack allocates the id and owns registration for a custom item. It
supplies the validated filter/range, revised sampling interval and queue
size, manager handle, subscription information, and durability setting
through `MonitoredItemFactoryContext`. The returned item must preserve
that identity and ownership. Both built-in monitored-item managers then
handle modify, monitoring-mode, delete, and manager-lifecycle operations
normally. `Use(factory, queueInitialValue: true)` additionally performs
the standard initial attribute read; push-style items omit it by default.

Manager-level asynchronous batch hooks receive only successful items and
run after the monitored-item manager has applied its changes:

```csharp
builder
    .OnMonitoredItemsCreated(SubscribeRegisterSlicesAsync)
    .OnMonitoredItemsDeleted(UnsubscribeRegisterSlicesAsync);
```

The existing synchronous `OnCreateMonitoredItemsComplete` override remains
supported and runs before the new async create-complete hook.

#### Creating nodes under other managers' nodes (Objects folder)

Anything a NodeSet can declare, `Configure` can declare identically —
including references whose target is owned by **another** node manager,
such as the ns=0 `Objects` folder managed by the `CoreNodeManager`.
Write the inverse reference on your node; after the `Configure`
partials return, the manager re-runs its reverse-reference pass
(`FluentNodeManagerBase.CompleteConfigureAsync`) and publishes the
matching forward edge into the `externalReferences` dictionary that the
master node manager distributes once every manager's address space is
built.

Two helpers make the common placement declarative, and a manager-level
`CreateInstance<TState>` creates root-level (parentless) instances,
materializing the subtree from the type model and minting NodeIds
through the manager's `INodeIdFactory`:

```csharp
partial void Configure(INodeManagerBuilder builder)
{
    // Instantiate a second boiler at runtime and place it under the
    // ns=0 Objects folder — no LoadPredefinedNodesAsync override, no
    // manual externalReferences bookkeeping.
    builder.CreateInstance(
            new QualifiedName("Boiler #2", NamespaceIndexes[1]),
            p => new BoilerState(p))
        .Configure(n => n.UnderObjectsFolder());

    // Arbitrary parents work the same way:
    //   n.OrganizedBy(parentNodeId)
    // adds the inverse Organizes reference; the forward edge reaches
    // the owning manager automatically.
}
```

Inverse `HasNotifier` references to external notifiers get root-notifier
registration through the same pass. This covers **startup-time**
configuration only — for nodes created after startup use
`IMasterNodeManager.AddReferencesAsync`, which dispatches to the live
owning manager.

### Typed model-traversal — the `Configure(I{Manager}NodeManagerBuilder)` partial

Alongside the string/NodeId/TypeId addressing surface above, the
generator emits a **second** `Configure` partial whose builder parameter
exposes one IntelliSense-aware accessor per predefined instance, child,
variable and method in the model. Every wiring site becomes a chain of
properties — typos are compile-time errors, not startup-time
`ServiceResultException`s.

```csharp
public partial class BoilerNodeManager
{
    // Untyped Configure remains available for nodes outside the model
    // (e.g. dynamic instances, foreign-namespace nodes, or just to keep
    // hand-written wiring side-by-side with typed wiring).
    partial void Configure(INodeManagerBuilder builder)
    {
        builder
            .Node("Boilers/Boiler #1/DrumX001/LIX001/Output")
            .OnRead(GenerateDrumLevel);
    }

    // Typed Configure: every accessor below is a generated property
    // resolved against the model. The compiler enforces both the path
    // shape AND the value type of every leaf.
    partial void Configure(IBoilerNodeManagerBuilder builder)
    {
        // Variable: typed Func<double> handler — the generator removed
        // the ref-Variant boilerplate.
        builder.Boilers.Boiler__1.LCX001.Measurement
            .OnRead(GenerateLevelMeasurement);

        // Variable, async: routes through BaseVariableState.ReadAttributeAsync
        // outside the lock so the lambda may freely await.
        builder.Boilers.Boiler__1.PipeX002.FTX002.Output
            .OnRead(GenerateOutputFlowAsync);

        // Method, async: typed OnCall(Func<CancellationToken,ValueTask>)
        // overload. Bind sync Action variants the same way.
        builder.Boilers.Boiler__1.Simulation.Halt
            .OnCall(HaltSimulationAsync);
    }
}
```

Both partials are optional and both run; wiring the same node from
both is illegal and throws at startup. Choose whichever shape best fits
each call site — typed for everything declared in the model, untyped
for everything else.

#### What the generator emits per model

For a model with `N` ObjectTypes and `M` predefined instances/children
the generator emits, into a single `{Manager}.FluentBuilders.g.cs`:

- `internal interface I{Manager}NodeManagerBuilder : INodeManagerBuilder`
  — one accessor per top-level predefined instance.
- `internal sealed class {Manager}NodeManagerTypedBuilder` — proxy that
  forwards `INodeManagerBuilder` members to the runtime builder while
  surfacing the typed accessors.
- One `internal sealed class` per instance node — whose properties map
  to typed `IVariableBuilder<TValue>`, child wrapper instances, and
  method wrappers.
- One `internal sealed class` per method — exposing typed
  `OnCall(...)` overloads bound to the method's declared arguments
  (the generator handles `Variant.TryGetValue` unpacking and
  `Variant.From<T>` boxing — see [Methods with arguments](#methods-with-arguments--typed-oncall-overloads)).
  A method with inputs but no output binds to `OnCall(Action<TIn…>)`;
  a method with neither inputs nor outputs keeps the argument-less
  `OnCall(Action)` / `OnCall(Func<CancellationToken, ValueTask>)`
  overloads.

All emitted types are `internal sealed` because `Configure` is a
private partial — the surface never escapes the assembly. Child
accessors resolve namespace indices lazily through
`ISystemContext.NamespaceUris.GetIndexOrAppend(...)` so the wrappers
work regardless of the namespace-table order at runtime. Object wrappers
use the generated concrete `*State` type when the model declares one,
and manager-level extensions such as `Simulation(...)` work through the
typed proxy just as they do through the untyped builder.

#### Methods with arguments — typed `OnCall` overloads

When a model method declares input or output arguments the generator
emits **typed `OnCall` overloads** that bind directly to the user
handler's parameters and return value. Inputs are unboxed via
`Variant.TryGetValue<T>(out T)`, the boxed result is written back
through `Variant.From<T>(value)`, and `BadInvalidArgument` /
`BadArgumentsMissing` is returned when the wire shape does not match
the declared signature — none of which the user has to spell out.

Two overloads are emitted per method, shaped by the declared arguments:

- **Inputs and outputs** →
  `OnCall(Func<TIn1, …, TResult> handler)` (synchronous dispatch through
  `MethodState.OnCallMethod2`) and
  `OnCall(Func<TIn1, …, CancellationToken, ValueTask<TResult>> handler)`
  (async dispatch through `MethodState.OnCallMethod2Async`, awaited inside
  `AsyncCustomNodeManager.CallAsync` so the lambda may freely `await`).
- **Inputs but no output** (a `void`-returning action) →
  `OnCall(Action<TIn1, …> handler)` and
  `OnCall(Func<TIn1, …, CancellationToken, ValueTask> handler)`. The inputs
  are still unpacked via `Variant.TryGetValue<T>`, so
  `builder.X.SetOutputVal.OnCall((float v) => …)` binds directly to the
  argument.
- **No inputs and no output** → the argument-less `OnCall(Action)` /
  `OnCall(Func<CancellationToken, ValueTask>)` overloads.

Methods with multiple output arguments are bound to a `ValueTuple`
return — slot `i` is written from `__r.Item{i+1}`.

The declared arguments are resolved from the method itself and, when the
method carries none of its own, from its method declaration / method type.
This means **instance methods imported from a NodeSet2** (whose
`InputArguments`/`OutputArguments` live on the referenced declaration) get
the same typed `OnCall` overloads as methods authored in a ModelDesign.

```csharp
[NodeManager(NamespaceUri = "http://opcfoundation.org/UA/Calc/")]
public partial class CalcNodeManager
{
    partial void Configure(ICalcNodeManagerBuilder builder)
    {
        // Sync int+int → int. The generator unpacks each Variant
        // through Variant.TryGetValue<int> and boxes the result back
        // through Variant.From<int>.
        builder.Calculator.Add
            .OnCall((int a, int b) => a + b);

        // Async double+double → double. The CancellationToken is
        // forwarded by AsyncCustomNodeManager.CallAsync so the
        // handler may freely await and honour cancellation.
        builder.Calculator.Multiply
            .OnCall(async (double x, double y, CancellationToken ct) =>
            {
                await Task.Yield();
                ct.ThrowIfCancellationRequested();
                return x * y;
            });

        // Sync string+string → string. Reference-typed inputs and
        // return values use the same Variant.TryGetValue / Variant.From
        // path; the handler can null-coalesce safely because a missing
        // input is reported as BadInvalidArgument before the lambda
        // ever runs.
        builder.Calculator.Concat
            .OnCall((string left, string right) =>
                (left ?? string.Empty) + (right ?? string.Empty));
    }
}
```

The end-to-end sample lives in
`samples/MinimalApi/MinimalCalcServer/` (model in `Model/Calc.xml`, wiring
in `CalcNodeManager.Configure.cs`). The companion AOT round-trip tests
in `tests/Opc.Ua.Aot.Tests/CalculatorNodeManagerAotTests.cs` exercise
each shape over a real `Session.CallAsync(...)`.

### Event sources — typed `Publish<TEvent>` on notifier wrappers

Beyond reads, writes and method calls, the fluent API lets callers
register an `IAsyncEnumerable<TEvent>` against any notifier object so
events flow into the standard `NodeState.ReportEvent` path
automatically. The runtime owns the entire lifecycle: it starts the
iterator the first time a client subscribes to events on the notifier
(or any ancestor that walks via inverse `HasNotifier` /
`HasEventSource` references), cancels it when the last interested
monitored item disappears, and disposes it on manager teardown.

Generated managers derive from `Opc.Ua.Server.Fluent.FluentNodeManagerBase`
out of the box, so wiring is one call:

```csharp
partial void Configure(IBoilerNodeManagerBuilder builder)
{
    // The DrumX001 wrapper exposes Publish<TEvent> because the model
    // declares EventNotifier=SubscribeToEvents on the node. Lazy by
    // default — the iterator only runs while a client is monitoring.
    builder.Boilers.Boiler__1.DrumX001
        .Publish<BaseEventState>(GenerateDrumHeartbeatAsync);
}

private async IAsyncEnumerable<BaseEventState> GenerateDrumHeartbeatAsync(
    BaseObjectState notifier,
    ISystemContext context,
    [EnumeratorCancellation] CancellationToken cancellationToken)
{
    while (!cancellationToken.IsCancellationRequested)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) { yield break; }

        var ev = new BaseEventState(parent: notifier);
        ev.Severity = PropertyState<ushort>.With<VariantBuilder>(
            ev, (ushort)EventSeverity.Medium);
        ev.Message = PropertyState<LocalizedText>.With<VariantBuilder>(
            ev, new LocalizedText("Drum heartbeat"));
        yield return ev;
    }
}
```

The runtime auto-populates `EventId`, `EventType`, `SourceNode`,
`SourceName` (browse name of the notifier), `Time`, `ReceiveTime`,
`Severity` (Medium when 0) and `Message` (empty `LocalizedText` when
unset) on the way out, so the iterator only sets the user-meaningful
fields.

#### Where the typed overload appears

The generator emits `Publish<TEvent>` on a wrapper **only** when the
underlying node qualifies as an event source:

- `ObjectDesign.SupportsEvents == true` (i.e. the model declares
  `EventNotifier=SubscribeToEvents`, `HasNotifier`, or
  `HasEventSource`), or
- The node has a forward `GeneratesEvent` / `AlwaysGeneratesEvent`
  reference.

`TEvent` is constrained to `BaseEventState` — pass any subtype that
fits the model's event hierarchy. For nodes outside the model, or
hand-written managers, the same `Publish<TNotifier, TEvent>` extension
is available directly on `INodeBuilder<TNotifier>` where
`TNotifier : BaseObjectState`.

#### Two registration shapes

```csharp
// Direct stream — registry uses the same instance for every activation.
builder.Boilers.Boiler__1.DrumX001
    .Publish<BaseEventState>(channel.Reader.ReadAllAsync(default));

// Factory — registry calls the factory each time a client subscribes,
// so the iterator can capture the live notifier / context / token.
builder.Boilers.Boiler__1.DrumX001
    .Publish<BaseEventState>(
        (notifier, context, ct) => GenerateAsync(notifier, context, ct));
```

#### Tuning lifecycle with `EventPublishOptions`

```csharp
builder.Boilers.Boiler__1.DrumX001
    .Publish<BaseEventState>(GenerateDrumHeartbeatAsync,
        new EventPublishOptions
        {
            // Keep iterator running even with no monitored items.
            AlwaysOn               = false,

            // Skip default population of EventId / EventType / Time /
            // ReceiveTime / SourceNode / SourceName / Severity / Message.
            SkipDefaultPopulation  = false,

            // Register the notifier as a server-wide root notifier so
            // clients can monitor events on the Server object itself.
            RegisterAsRootNotifier = true,

            // Bound how long the registry waits for the iterator to
            // honour cancellation on deactivation.
            CancellationTimeout    = TimeSpan.FromSeconds(5),

            // Optional fault-handler invoked when the iterator throws.
            OnError = (notifier, exception, context) => { /* log */ }
        });
```

#### Hand-written node managers

Managers that don't use the source generator can opt in by deriving
from `Opc.Ua.Server.Fluent.FluentNodeManagerBase` and calling
`AttachToBuilder(builder)` from inside their address-space-build
callback. Once attached, all `Publish` extensions resolve against the
manager's registry exactly as for generated managers.

The end-to-end sample lives in
`samples/MinimalApi/MinimalBoilerServer/BoilerNodeManager.Configure.cs`
(wiring `GenerateDrumHeartbeatAsync` on the drum). The companion AOT
round-trip test in
`tests/Opc.Ua.Aot.Tests/PublishedEventsAotTests.cs` subscribes a
real client `MonitoredItem` with an `EventFilter` and asserts the
heartbeats arrive end-to-end under NativeAOT constraints (no JIT, no
reflection).

### Single-file `Program.cs` — what it looks like

The shipping `services.AddOpcUa().AddServer(...)` extension wires the
server into the .NET Generic Host: configuration, certificate check,
`ApplicationInstance` lifetime and Ctrl+C/SIGTERM handling are all owned
by the host. User code stays at ~12 lines.

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddConsole();

builder.Services
    .AddOpcUa()
    .AddServer(o =>
    {
        o.ApplicationName = "MyServer";
        o.ApplicationUri  = "urn:localhost:MyServer";
        o.ProductUri      = "uri:opcfoundation.org:MyServer";
        o.AutoAcceptUntrustedCertificates = true;
        o.EndpointUrls.Add("opc.tcp://localhost:51210/MyServer");
    })
    .AddNodeManager<MyModel.MyModelNodeManagerFactory>();

await builder.Build().RunAsync();
```

`AddOpcUa()` registers a `ServiceProviderTelemetryContext` that adapts
the host's `ILoggerFactory` to `ITelemetryContext` — no separate logging
pipeline is required. `IOpcUaServerBuilder.AddNodeManager<T>()` registers
an `IAsyncNodeManagerFactory`; use `AddSyncNodeManager<T>()` for the
legacy `INodeManagerFactory`. For advanced configuration (custom security
policies, additional builder calls), set `OpcUaServerOptions.ConfigureBuilder`.

That's the whole server. The Boiler version is in
`samples/MinimalApi/MinimalBoilerServer/Program.cs`.

### Multi-namespace and manager-swap subclassing

Because the generated factory members are `virtual`, you can extend
without forking:

```csharp
public sealed class MyExtendedFactory : MyModel.MyModelNodeManagerFactory
{
    public override ArrayOf<string> NamespacesUris
    {
        get
        {
            var ns = base.NamespacesUris;
            ns.Add("urn:my:second:namespace");
            return ns;
        }
    }

    public override ValueTask<IAsyncNodeManager> CreateAsync(
        IServerInternal server,
        ApplicationConfiguration cfg,
        CancellationToken cancellationToken = default)
        => new(new MyExtendedNodeManager(server, cfg));
}
```

The `tests/Opc.Ua.Server.Tests/Fluent/GeneratedManagerHybridTests.cs`
suite verifies these subclassing scenarios.

### NativeAOT publishing

The project that hosts the generated manager only needs the standard AOT
settings:

```xml
<PropertyGroup>
  <PublishAot>true</PublishAot>
  <InvariantGlobalization>true</InvariantGlobalization>
  <TargetFramework>net10.0</TargetFramework>
</PropertyGroup>
```

Use `Microsoft.Extensions.Logging.Console` for AOT-friendly logging
(Serilog providers vary in AOT compatibility). Validate with:

```cmd
dotnet publish -c Release -r win-x64
```

`samples/MinimalApi/MinimalBoilerServer` publishes cleanly with **zero AOT/trim
warnings** (~29 MB self-contained EXE).

### Runtime NodeSet alternative

When you want to host a NodeSet2 document without any source generation — for example a companion-spec XML received from a vendor, or a model that changes more frequently than you rebuild — use [AddRuntimeNodeSet](RuntimeNodeSets.md) instead. The runtime path loads a file or stream, imports nodes in topological dependency order, and exposes them through the same untyped `INodeManagerBuilder` surface as the `Configure` partial above. Use `AddRuntimeNodeSet` for startup registration or `INodeManagerLifecycle` to add, reload, and remove a model while the server runs. See [RuntimeNodeSets.md](RuntimeNodeSets.md) for a side-by-side comparison of the two paths.

### Building richer node managers — the fluent extension surface

The Configure callback wires read/write/method/event hooks against
already-loaded predefined nodes, but real-world servers also need to
materialise dynamic instances, attach engineering units to measurements,
build alarms, run simulation loops, populate identification properties,
and compose multiple companion-spec models into a single address space.
The extensions below cover those workflows. All are AOT/trim safe and
follow the same return-the-same-builder chaining contract as the core
`INodeBuilder` API.

#### Engineering units & EU range

`IVariableBuilder<TValue>.WithEngineeringUnits` and `.WithEURange`
attach the standard `EngineeringUnits` and `EURange` property children
on a `BaseAnalogState` variable. The extensions create the property
child on demand (matching the runtime's `AddEngineeringUnits` /
`AddEURange` helpers) and then set the Value attribute.

```csharp
builder.Variable<double>("Pumps/Pump_1/Operational/Measurements/FluidTemperature")
       .OnRead(SimulateTemperature)
       .WithEngineeringUnits(
           new EUInformation("K", "Kelvin",
               "http://www.opcfoundation.org/UA/units/un/cefact"))
       .WithEURange(min: 263.15, max: 393.15);

// Convenience: set both at once.
builder.Variable<double>("Pumps/Pump_1/Operational/Measurements/DifferentialPressure")
       .OnRead(SimulatePressure)
       .WithUnits(EUInformations.Pascal, min: 0, max: 400_000);
```

Fail-fast behaviour: calling these on a non-`BaseAnalogState` variable
throws `ServiceResultException` with
`StatusCodes.BadTypeMismatch` — analog-only properties don't apply to
plain `BaseDataVariableState` nodes.

#### Bulk property initialisation

`INodeBuilder.WithProperty` writes the Value attribute of a property
child, **creating the property first when it does not already exist**.
Typed overloads exist for every built-in OPC UA scalar (`string`,
`int`, `uint`, `double`, `bool`, `DateTimeUtc`, `NodeId`,
`LocalizedText`, `QualifiedName`, etc.) plus a generic `Variant` escape
hatch.

```csharp
builder.Node("Pumps/Pump_1/Identification")
       .WithProperty("Manufacturer", new LocalizedText("SimPump Corp"))
       .WithProperty("Model", new LocalizedText("PumpX-2000"))
       .WithProperty("SerialNumber", "SN-001")
       .WithProperty("DeviceClass", "Pump")
       .WithProperty("ProductInstanceUri",
           "urn:simdevice:SimPump:PumpX-2000:SN-001");
```

Pass the CLR type the model declares for the property — `LocalizedText`
for `Manufacturer` / `Model` / `ComponentName`, `ushort` for
`YearOfConstruction`, `byte` for `MonthOfConstruction`, and so on. The
typed overloads make the choice explicit at the call site.

Reference resolution is by browse-name only (case-sensitive,
namespace-agnostic), matching the AOT-safe constraint of the rest of
the fluent surface. When the child exists it is updated; when it exists
but isn't a variable the call throws `BadTypeMismatch`.

When the child is **missing**, `WithProperty` materialises a new
read-only `PropertyState` (data type inferred from the value) under the
current node and registers it with the owning node manager. This makes
the helper usable on freshly built nodes such as custom DI functional
groups — not just on properties that come from a loaded model:

```csharp
// "Diagnostics" is a custom functional group with no model-defined
// properties; WithProperty creates each one on the fly.
node.WithProperty("LastError", string.Empty)
    .WithProperty("ErrorCount", 0)
    .WithProperty("LastSelfTest", (DateTimeUtc)DateTime.UtcNow);
```

Auto-created properties are read-only by default. Grant write access
with the fluent `Writable()` helper — either standalone on a resolved
variable, or inline via the `WithProperty(name, value, configure)`
overload that positions a builder on the new property:

```csharp
node.WithProperty("LastError", Variant.From(string.Empty), p => p.Writable())
    .WithProperty("ErrorCount", 0);

// or, on an existing variable:
builder.Node("Pumps/Pump #1/Operational/SetPoint").Writable();
```

#### References & dynamic child objects

`INodeBuilder.Organizes`, `.HasComponent`, `.HasProperty` and the
generic `.AddReference(typeId, isInverse, target)` add forward /
inverse references on the current node. They're the foundation for
OPC UA Device Integration (DI)'s FunctionalGroup pattern — group
unrelated variables under a shared object via `Organizes`.

```csharp
// Wire existing measurement variables into a custom FunctionalGroup.
builder.Node("Pumps/Pump #1/Operational/MyGroup")
       .Organizes(temperatureNodeId)
       .Organizes(pressureNodeId)
       .HasProperty(metadataNodeId);
```

`INodeBuilder.OrganizedBy(parentId)` and `.UnderObjectsFolder()` add
the *inverse* `Organizes` reference, placing the current node below an
organizing parent. The parent may be owned by another node manager
(e.g. the ns=0 `Objects` folder) — the forward edge is published
automatically when `Configure` completes; see
[Creating nodes under other managers' nodes](#creating-nodes-under-other-managers-nodes-objects-folder).

`INodeBuilder.AddObject(browseName, typeDefinitionId)` synthesises a
new `BaseObjectState` child under the current node and returns a typed
builder for the new object. NodeIds follow the
`{parentIdentifier}_{childName}` pattern used by the source generator's
default factory. The helper registers the created node with the owning
`AsyncCustomNodeManager`, so the object is immediately browseable and
addressable by NodeId.

```csharp
// Create a custom FunctionalGroup, then attach measurements.
builder.Node("Pumps/Pump #1")
       .AddObject(new QualifiedName("CustomMetrics", pumpsNs))
       .Organizes(t1).Organizes(t2);
```

Newly created objects are reachable through navigation from the parent
and through direct NodeId lookup immediately. Callers do not need to
index nodes created by `AddObject` themselves.

#### Creating instances of model types

`INodeBuilder.CreateInstance<TState>(name, factory)` materialises a
new `BaseInstanceState` subtype using a user-supplied factory delegate
— typically a generated `Create<TypeName>` method from the source
generator output. The returned `IInstanceBuilder<TState>` exposes
`.Configure(builder => …)` for inline child wiring, `.AsNode()` for a
typed `INodeBuilder<TState>` view, and `.Done()` to return to the
parent builder.

```csharp
builder.Node("Pumps")
       .CreateInstance(
           new QualifiedName("Pump #2", pumpsNs),
           pumpTypeId,
           parent => context.CreatePumpType(parent))
       .Configure(p2 =>
           p2.AsNode()
             .WithProperty("Manufacturer", "Vendor B")
             .WithProperty("SerialNumber", "SN-002"));
```

The factory pattern keeps the API reflection-free and AOT safe — the
generator already emits the per-type `Create<Type>` extension methods
that the factory delegate calls into.

Like `AddObject`, `CreateInstance<TState>` registers the materialised
subtree with the owning node manager. The same registration behaviour
is used by the fluent state-machine creators, so generated instances
created from a builder can be browsed, read, and monitored without a
separate `AddPredefinedNodeAsync` call.

For **root-level** instances (no parent node in this manager),
`INodeManagerBuilder.CreateInstance<TState>(name, factory)` accepts a
plain constructor-style factory (`p => new BoilerState(p)`, invoked
with a `null` parent), fully materialises the instance from its type
model, and mints per-instance NodeIds for the whole subtree through
the manager's `INodeIdFactory`. Combine with `.UnderObjectsFolder()`
(or `.OrganizedBy(parentId)`) to place the instance below a node owned
by another manager:

```csharp
builder.CreateInstance(
        new QualifiedName("Boiler #2", NamespaceIndexes[1]),
        p => new BoilerState(p))
    .Configure(n => n.UnderObjectsFolder());
```

#### Alarm setup (MVP)

`INodeBuilder.CreateLimitAlarm`, `.CreateExclusiveLimitAlarm` and
`.CreateOffNormalAlarm` attach a fresh alarm condition under the
current node and return an `IAlarmBuilder<TState>` for further
configuration. The helpers register the condition, add the
`HasCondition` reference, initialise `SourceNode`, `SourceName`,
`ConditionName`, and `InputNode`, and promote the source object and its
ancestors with `EventNotifiers.SubscribeToEvents`. The source is also
registered as a root notifier so clients subscribing to the `Server`
object receive condition events:

```csharp
builder.Node("Pumps/Pump #1/Events")
       .CreateLimitAlarm(new QualifiedName("OverTempAlarm", pumpsNs))
       .WithLimits(highHigh: 380, high: 370, low: 273, lowLow: 263)
       .MonitorVariable(temperatureNode)
       .OnAcknowledge((ctx, condition, eventId, comment) => ServiceResult.Good)
       .OnConfirm((ctx, condition, eventId, comment) => ServiceResult.Good);
```

For full state access (severity tables, retain flag, branches), use
the `.ConfigureAlarm(Action<TState>)` escape hatch:

```csharp
builder.Node("Events")
       .CreateLimitAlarm(new QualifiedName("Custom", ns))
       .WithLimits(high: 100)
       .ConfigureAlarm(alarm =>
       {
           alarm.Retain!.Value = true;
           // any state-class mutation goes here
       });
```

#### Boolean supervision → alarm activation (NAMUR pattern)

`IVariableBuilder<bool>.OnRisingEdge` / `.OnFallingEdge` register
callbacks that fire when the variable's value transitions. The
`.ActivatesAlarm(alarmBuilder)` extension wires the bool variable to
an `AlarmConditionState`'s ActiveState so it flips in lockstep with
the supervision flag, updates `Retain`, adjusts severity, and reports a
condition event — exactly the OPC UA DI / NAMUR NE 107 pattern.

```csharp
IAlarmBuilder<NonExclusiveLimitAlarmState> cavitationAlarm =
    builder.Node("Events").CreateLimitAlarm(name)
        .ConfigureAlarm(a => a.Severity!.Value = (ushort)EventSeverity.Medium);

builder.Variable<bool>("Pump #1/Events/Supervision/ProcessFluid/Cavitation")
       .ActivatesAlarm(cavitationAlarm);
```

Detection is value-change based: transitions only fire when something
else (an `OnWrite` handler, a simulation tick, a client write) actually
mutates the variable.

#### Simulation timers

`INodeManagerBuilder.Simulation(interval).OnTick(...)` registers a
periodic background loop owned by the `FluentNodeManagerBase`. Each
tick fires on a `PeriodicTimer` and is cancelled when the manager is
disposed; exceptions inside handlers are logged and do not kill the
loop.

```csharp
partial void Configure(INodeManagerBuilder builder)
{
    builder.Simulation(TimeSpan.FromMilliseconds(250))
        .OnTick((ctx, elapsed) =>
        {
            m_temperature = 313.15 + 5 * Math.Sin(m_t * 0.01);
            m_pressure = 200000 + 50000 * Math.Sin(m_t * 0.03);
            m_t++;
        });
}
```

Async tick handlers receive a `CancellationToken` honouring manager
disposal — use it for any awaitable work inside the loop. Multiple
`.OnTick` calls on the same `Simulation()` builder all fire on every
tick.

The simulation registry **requires** the manager to derive from
`FluentNodeManagerBase` (the source generator-emitted manager already
does); calling `.Simulation()` on a plain `CustomNodeManager2` throws
`StatusCodes.BadConfigurationError`.

#### Pushing runtime value changes to subscribers

`OnRead` getters are invoked on the **Attribute (Read) service**, but a
value that only lives behind a getter — or in a backing field mutated by
an `OnCall` handler — will **not** reach subscribed MonitoredItems on its
own. In previous implementations the fix was to mutate `Node.Value` and call
`Node.ClearChangeMasks(...)`, but that node handle is deliberately unavailable
through the fluent surface once `Configure` returns (the builder is sealed).

Two fluent mechanisms close that gap.

**1. `Bind(out IValueUpdater<TValue>)` — explicit push.** Capture a runtime
handle during `Configure` and store it on the manager; the handle survives
sealing. `SetValue` assigns the value, timestamp and status and flushes the
change mask in one serialized call, so both reads *and* subscriptions see
the update:

```csharp
private IValueUpdater<float> m_ao01 = null!;

partial void Configure(IMyNodeManagerBuilder builder)
{
    builder.MyEquipment03.AO01.Builder.AsVariable<float>()
           .Bind(out m_ao01);

    builder.MyEquipment03.SetOutputVal
           .OnCall((float value) => m_ao01.SetValue(value));
}
```

`IValueUpdater<TValue>` also exposes `SetValue(value, statusCode)`,
`SetValue(value, statusCode, sourceTimestamp)`, and `NotifyChange()` (flush
a notification after an in-place mutation without changing the value).

**2. `PollEvery(interval, getter)` — opt-in auto-sampling.** Register a
periodic loop that reads the getter and pushes a change only when the value
actually differs, so subscriptions update automatically with no
change-notification code. An initial sample is applied immediately:

```csharp
builder.MyEquipment03.AO01
       .PollEvery(TimeSpan.FromMilliseconds(250), () => m_ao01Value);
```

Like `Simulation`, `PollEvery` reuses the manager-owned loop
infrastructure and therefore **requires** the manager to derive from
`FluentNodeManagerBase`; calling it on a plain `CustomNodeManager2` throws
`StatusCodes.BadConfigurationError`.

#### Subscription-gated sources

Use `PollWhileMonitored` when sampling an external source should consume
resources only while a client is interested. A disabled monitored item
does not keep the source active; `Sampling` and `Reporting` items do:

```csharp
builder.Variable<double>("Dynamic/Temperature")
    .OnFirstSubscriber((context, node, ct) =>
        m_device.StartMonitoringAsync(node.NodeId, ct))
    .OnLastSubscriber((context, node, ct) =>
        m_device.StopMonitoringAsync(node.NodeId, ct))
    .PollWhileMonitored(
        TimeSpan.FromMilliseconds(100),
        (context, ct) => m_device.ReadTemperatureAsync(ct));
```

The zero-to-one transition invokes `OnFirstSubscriber`, samples
immediately, and starts the worker. The one-to-zero transition cancels
the worker and invokes `OnLastSubscriber`. While active, the effective
period is the fastest revised sampling interval among active items,
bounded by the minimum period passed to `PollWhileMonitored`. Create,
modify, mode-change, and delete operations reconcile that period without
overlapping samples. The worker uses the server `TimeProvider`, pushes
only changed values through `IValueUpdater<TValue>`, and is cancelled when
the manager is disposed.

The same `OnFirstSubscriber`, `OnLastSubscriber`, and
`PollWhileMonitored` extensions are available on an
`IVirtualNodeBuilder`; the current materialized node is retained only for
the monitored-item lifetime.

#### Multi-model composition

The only supported mode for combining models is **source-generated
library references**. Each companion spec is built once into its
own model library (a `src/Opc.Ua.{Spec}/` project that
consumes the ModelDesign XML and emits an `AddOpcUa{Spec}`
extension method); the consumer adds project references and calls
the generated extensions directly in dependency order:

```csharp
protected override ValueTask<NodeStateCollection> LoadPredefinedNodesAsync(
    ISystemContext context, CancellationToken ct = default)
{
    var nodes = new NodeStateCollection();
    nodes.AddOpcUaDi(context);
    nodes.AddOpcUaMachinery(context);
    nodes.AddOpcUaPumps(context);
    return new ValueTask<NodeStateCollection>(nodes);
}
```

Source-generated models are AOT-friendly, deterministic, and
produce typed `*State` / `*Client` proxies. **Every application-
owned model must ship as source-generated content** — companion
specs ship as project references; locally-owned NodeSet2 XMLs are
wired through `<AdditionalFiles>` so the source generator emits
the same typed surface inside the consuming assembly. Each
`AddOpcUa{Spec}(context)` extension is idempotent and re-entrant,
so direct chaining in dependency order is the recommended pattern.

#### Mixing ModelDesign and NodeSet2 in one project

A `ModelDesign` XML and a `NodeSet2` XML can be combined in the same
project, and a node in one may reference a type defined in the other.
A common split is to author the reusable **object types** as a
NodeSet2 (e.g. exported from a modelling tool such as SiOME) and the
concrete **instances** as a ModelDesign whose `TypeDefinition`
points at those NodeSet2 types:

```xml
<!-- Instances.ModelDesign.xml -->
<opc:ModelDesign
  xmlns:opc="http://opcfoundation.org/UA/ModelDesign.xsd"
  xmlns:et="http://example.org/EquipmentTypes"
  xmlns="http://example.org/EquipmentInstances"
  TargetNamespace="http://example.org/EquipmentInstances">
  <opc:Namespaces>
    <opc:Namespace Name="EquipmentInstances"
      >http://example.org/EquipmentInstances</opc:Namespace>
    <!-- Bind the same URI to the "et" XML prefix used below. -->
    <opc:Namespace Name="EquipmentTypes" XmlPrefix="et"
      >http://example.org/EquipmentTypes</opc:Namespace>
  </opc:Namespaces>
  <!-- "et:" resolves to the NodeSet2 namespace declared via xmlns:et. -->
  <opc:Object SymbolicName="Equipment01" TypeDefinition="et:SimpleEquipmentType" />
</opc:ModelDesign>
```

```xml
<Project>
  <ItemGroup>
    <AdditionalFiles Include="Model\EquipmentTypes.NodeSet2.xml">
      <ModelSourceGeneratorModelUri>http://example.org/EquipmentTypes</ModelSourceGeneratorModelUri>
    </AdditionalFiles>
    <AdditionalFiles Include="Model\Instances.ModelDesign.xml">
      <ModelSourceGeneratorModelUri>http://example.org/EquipmentInstances</ModelSourceGeneratorModelUri>
    </AdditionalFiles>
  </ItemGroup>
</Project>
```

The generator resolves the cross-model reference automatically — every
input is supplied to the others as a resolution dependency (both
`ModelDesign → NodeSet2` and `ModelDesign → ModelDesign`).

> **Binding a `[NodeManager]` in a mixed project.** A `[NodeManager]`
> may target the namespace of *either* input — the NodeSet2 type model
> or the ModelDesign instance model — by setting its `NamespaceUri` to
> that model's URI. Binding is resolved across both the NodeSet2 and the
> ModelDesign generation passes, so a manager bound to the NodeSet2 types
> is **not** reported as unmatched (`MODELGEN010`) just because the
> project also contains a ModelDesign — and vice-versa. The generated
> node-manager class name and namespace come from the annotated partial
> class itself, **not** from `ModelSourceGeneratorPrefix`/`Name` (those
> control the generated `*State`/type class names — see the note below).

> **C# namespace of a NodeSet2 model.** The generated C# namespace for
> a NodeSet2 input is derived from its `ModelUri` unless you set
> `ModelSourceGeneratorPrefix` (C# namespace / prefix) and
> `ModelSourceGeneratorName` (the `Namespaces` class identifier) on that
> `AdditionalFiles` entry. A `Prefix`/`Name` declared *inside* a
> referencing ModelDesign's `<opc:Namespaces>` does **not** rename the
> NodeSet2's generated types — set the per-file MSBuild metadata on the
> NodeSet2 entry to control it.

#### NodeSet2 access-level bitmasks

NodeSet2 imports preserve the verbatim `AccessLevel` bitmask. This
matters for values such as `AccessLevel="5"` (`CurrentRead |
HistoryRead`): the legacy ModelDesign enum can describe the individual
named values but is not a `[Flags]` enum. The importer stores the raw
mask on `VariableDesign.RawAccessLevel`, and code generation emits the
corresponding `Opc.Ua.AccessLevels` constants instead of collapsing the
value to `Read`. `UserAccessLevel` intentionally mirrors
`AccessLevel`, matching the runtime NodeSet2 importer.

### Materialising instances at runtime — NodeId assignment

Every model gets three families of instance helpers. They differ only in
**who owns the NodeIds** of the nodes they produce:

| Helper | Produces | NodeIds |
| --- | --- | --- |
| `context.CreateInstanceOf<Type>(parent, browseName)` | A full typed subtree | Rebased onto per-instance NodeIds minted by `ISystemContext.NodeIdFactory` whenever a `browseName` is supplied |
| `owner.Add<Child>(context, nodeId = default)` | One optional child (+ its declared subtree) | Per-instance NodeIds, or the explicit `nodeId` you pass |
| `owner.CreateOrReplace<Child>(context, replacement)` | One child slot — also the plumbing behind `NodeState.CreateChild` / `ReplaceChild` | Per-instance NodeIds for a child that carries no NodeId yet or still carries the type-level one |

```csharp
// Two instances of the same type never collide: the factory rebases the
// mandatory children, and every subsequent child materialisation mints
// its own NodeId from the parent chain.
PumpState pump = SystemContext.CreateInstanceOfPumpType(deviceSet, pumpBrowseName);
deviceSet.AddChild(pump);

pump.AddOperational(SystemContext);                     // optional child
pump.CreateChild(SystemContext, someBrowseName);        // CreateOrReplace<Child>

await AddPredefinedNodeAsync(SystemContext, pump, cancellationToken);
```

The generated factory intentionally returns a graph whose create lifecycle
has not completed. Node manager registration completes
`OnBeforeCreate`/`OnAfterCreate` and clears change masks before the graph is
indexed. Asynchronous predefined-node registration repairs typed instance
subtrees which still carry null, foreign-namespace, or
type-declaration-colliding NodeIds first, so lifecycle callbacks see the
identifiers which enter the address space. NodeIds explicitly assigned in a
namespace owned by the manager are preserved. Synchronous registration keeps
the caller's identifiers; fluent helpers which materialise typed subtrees,
such as the state-machine creators, assign their instance child NodeIds before
registration. The behavior hook then receives a created node.
`NodeState.IsCreated` reports whether an individual node has completed that
lifecycle.

Most callers should configure the graph and then register it as shown above.
If code must read state established by `OnAfterCreate`, or write state or
handlers which an `OnAfterCreate` override would replace, call
`CreateAsPredefinedNode` after assembling the subtree and before that
ordering-sensitive code. The call is idempotent per node and still completes
children added since an earlier call. It does not re-run `OnAfterCreate` on
ancestors which were already created; assemble parent-wired children before
the parent's first completion, or wire those late children explicitly.

Notes:

* An explicit `browseName` is what marks a *dynamically materialised
  instance*. `CreateInstanceOf<Type>()` without one (as used by the
  generated `NodeStateActivator`s and when replacing a well-known
  singleton) keeps the declaration NodeIds.
* A NodeId **you** assigned is never overwritten — pass a fully
  configured child as the `replacement`, or use `Add<Child>(context,
  nodeId)`, to keep control.
* The generated type factories opt out through
  `assignInstanceNodeIds: false`: they build declaration subtrees whose
  NodeIds `CreateInstanceOf<Type>` rebases in a single pass afterwards.
  The same parameter is available to you if you need that behaviour.
* `CreateInstanceOf<Type>` assigns identity but does not call
  `CreateAsPredefinedNode`. `AddPredefinedNodeAsync`, `AddNodeAsync`, and the
  synchronous predefined-node registration paths complete the lifecycle
  automatically.
* Assignment only happens when the context carries an
  `ISystemContext.NodeIdFactory`. `AsyncCustomNodeManager` supplies one
  that allocates null IDs in the manager's default namespace. Registration
  selectively retries descendants when an asynchronously registered,
  manager-owned instance subtree still carries foreign declaration IDs, while
  preserving explicitly assigned IDs in an owned namespace and well-known
  namespace-zero roots. Synchronous creators must assign typed child IDs before
  registration. Override `New` only when the address space needs a different
  stable naming strategy, such as deriving IDs from the parent chain.
* **A node copy never assigns.** `NodeState.Create(context, source)`
  initialises each child from its source right after creating it, which
  overwrites any NodeId minted along the way — so minting one would only
  consume identifiers, and leak them for factories that track outstanding
  allocations. The copy therefore calls
  `CreateChild(context, browseName, assignInstanceNodeIds: false)`.

#### Custom node types and assignment control

`NodeState.FindChild` and `NodeState.CreateChild` carry
`assignInstanceNodeIds` as their last parameter. It defaults to `true`, so
callers that state no intent keep materialising children with per-instance
NodeIds. A type that declares children overrides `FindChild`, resolves the
ones it declares, and passes the request on — both to its
`CreateOrReplace<Child>` helpers and to the base:

```csharp
protected override BaseInstanceState? FindChild(
    ISystemContext context,
    QualifiedName browseName,
    bool createOrReplace,
    BaseInstanceState? replacement,
    bool assignInstanceNodeIds = true)
{
    if (browseName.Name == BrowseNames.EnumStrings)
    {
        return !createOrReplace
            ? EnumStrings
            : CreateOrReplaceEnumStrings(context, replacement, assignInstanceNodeIds);
    }

    return base.FindChild(
        context, browseName, createOrReplace, replacement, assignInstanceNodeIds);
}
```

Source generated types emit exactly this shape. Because the request is an
argument, every type — generated or hand-written — sees the real
`ISystemContext` during a copy; nothing wraps the context to hide the
`NodeIdFactory`.

> **Breaking change in 2.0.** The four argument `FindChild` and the two
> argument `CreateChild` are gone. An override written against 1.5.378 fails
> to compile until the parameter is added; see the
> [migration guide](migrate/2.0.x/node-states.md#nodestate-findchild-and-createchild-state-nodeid-assignment).

### Current limitations

- **Browse-path wildcards** (`*`, `**`) are not supported. Wire each
  path explicitly or resolve by NodeId / TypeDefinitionId.
- **Historical access advertisement.** Servers reconcile
  `Historizing` and `HistoryRead` / `HistoryWrite` access-level bits at
  startup. Variables that do not resolve to an `IHistorianProvider`
  have those bits masked before the server accepts clients; variables
  wired with `Historize()` or another historian keep their history
  surface. See [Server address-space metadata](#server-address-space-metadata)
  and [Historical Access](HistoricalAccess.md).
- **Reserved child names.** A component/property whose BrowseName
  matches a built-in `NodeState` attribute member (for example
  `Description` or `DisplayName`) shadows that member on the generated
  `*State` class and produces code that does not compile. Rename such
  children (the OPC UA `Description`/`DisplayName` *attributes* are
  always available without a dedicated child).

### Sample

- `samples/MinimalApi/MinimalBoilerServer/` — a fully self-contained,
  NativeAOT single-file Boiler server. Read it top-to-bottom in
  &lt;200 lines.
- `samples/MinimalApi/MinimalCalcServer/` — a calculator server that
  exercises the typed
  [methods-with-arguments OnCall overloads](#methods-with-arguments--typed-oncall-overloads)
  end-to-end (sync `int+int → int`, async `double+double → double`,
  sync `string+string → string`).
- `samples/DI/PumpDeviceIntegrationServer/` — the full OPC 40223
  Pumps companion server. Exercises every fluent extension above
  (engineering units, identification properties, FunctionalGroup
  wiring, instance creation, limit alarm with NAMUR-style boolean
  supervision, periodic simulation tick, and multi-model loader for
  DI + Machinery + Pumps), and additionally attaches the OPC
  10000-100 software-update facet to a second declarative pump
  device.
