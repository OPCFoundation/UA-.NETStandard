# Node States and INodeCache

> **When to read this:** Read this when migrating custom NodeManagers, core / diagnostics / configuration manager interfaces, master-manager services, service or transfer overrides, synchronous adapters, `NodeState` clone / read / write helpers (`Clone` -> `CreateCopy`, removed `BaseVariableState` helpers), the new `INodeManager3` role-permission hooks, `OnAfterCreate(CancellationToken)`, predefined-node processing, generics on `BaseVariableState` / `BaseVariableTypeState`, code that took `lock (node)` on a `NodeState` or used `CoreNodeManager.DataLock` or `NodeBrowser.DataLock`, or `INodeCache.InvalidateNode`.

## Contents

- [Node States](#node-states)
  - [Generics and Typed BaseVariableState and BaseVariableTypeState](#generics-and-typed-basevariablestate-and-basevariabletypestate)
  - [Predefined node processing](#predefined-node-processing)
  - [NodeState Cloning and Lifecycle](#nodestate-cloning-and-lifecycle)
  - [Custom NodeManager interface changes](#custom-nodemanager-interface-changes)
  - [INodeManager3 - new role-permission and method-resolution hooks](#inodemanager3---new-role-permission-and-method-resolution-hooks)
  - [NodeState guards itself; NodeBrowser is single-consumer (UA0027)](#nodestate-guards-itself-nodebrowser-is-single-consumer-ua0027)
  - [Browse continuation lifetime](#browse-continuation-lifetime)
  - [NodeBrowser gains an async iteration seam](#nodebrowser-gains-an-async-iteration-seam)
- [`INodeCache` changes](#inodecache-changes)

## Node States

### Generics and Typed BaseVariableState and BaseVariableTypeState

With the changes to Variant, the generic node state classes reflecting the inner value of the variant "value" have been changed to not rely on "casting" from object to T. The conversion is "baked in" when creating an instance of a typed state using a "builder" struct. Whether the value is scalar, array or matrix is irrelevant to which builder to use. There are 3 situations and the respective builder struct to use:

1. T is a built in type -> use `VariantBuilder`
2. T is a instance of `IEncodeable` (a complex structure) -> Use `StructureBuilder<T>` where T is the name of the structure.
3. T is an instance of Enum (an enumeration) -> Use `EnumBuilder<T>` where T is the name of the enumeration type.

E.g. to create an instance of a `PropertyState<T>` where T is `ArrayOf<ExtensionObject>` use

``` csharp
    var state = new PropertyState<ArrayOf<ExtensionObject>>.Implementation<VariantBuilder>(parent)
    // or
    var state = PropertyState<ArrayOf<ExtensionObject>>.With<VariantBuilder>(parent)
```

To create an instance of a `PropertyState<T>` where T is `Argument` (an IEncodeable type) use

``` csharp
    var state = new PropertyState<Argument>.Implementation<StructureBuilder<Argument>>(parent)
    // or
    var state = PropertyState<Argument>.With<StructureBuilder<Argument>>(parent)
```

To create an instance of a `PropertyState<T>` where T is `MatrixOf<ComplexType>` (an IEncodeable type) use

``` csharp
    var state = new PropertyState<MatrixOf<ComplexType>>.Implementation<StructureBuilder<ComplexType>>(parent)
    // or
    var state = PropertyState<MatrixOf<ComplexType>>.With<StructureBuilder<ComplexType>>(parent)
```

Note: While this looks clunky, it does not use reflection and comes with 0 allocation including any allocations for `Func` or `Action` delegates and works around .net limitations regarding overload resolution for generic arguments (which also required the use of `FromStructure` or `FromEnumeration` on the Variant type instead of using `From`). In future versions it is possible the source generator could generate away some of the redundancies in the above expressions.

### Predefined node processing

Filling the predefined node state list is now generated as source code.  This means the predefined Variable and Object instance states are the generated classes, not the root node states. This has an
impact on the AddBehaviorToPredefinedNode implementations which should use the received node state as "activeNode" and attach functionality to it instead of creating a active node.

Example guidance (mirrors BoilerNodeManager): the node passed to `AddBehaviorToPredefinedNode` is already the generated instance state, so attach behavior directly to it instead of creating a new state. This ensures the predefined list stays consistent and the generated type-specific fields are available.

``` csharp
    protected override void AddBehaviorToPredefinedNode(
        ISystemContext context,
        NodeState node)
    {
        if (node is BoilerTypeState boiler)
        {
            var activeNode = boiler;
            activeNode.Temperature.OnSimpleWriteValue = OnTemperatureWrite;
            activeNode.FlowRate.OnSimpleWriteValue = OnFlowRateWrite;
        }

        // Add callbacks to the node here if necessary
        // If not needed you do not need to implement this call at all.
    }
```

See [NodeStates](../../../src/Opc.Ua.Types/State/readme.md) document for more information.

### NodeState Cloning and Lifecycle

#### Node state does not implement IDisposable anymore.

Node states do not manage resources, they access resources. Therefore the management of resources must be done in a node manager.
If you are overriding Dispose() on a NodeState to manage the node state, make the method public instead of protected, and maintain
a list of node states on which you must call the Dispose() method when the Node Manager is disposed.  Better, associated node states
only via an identifier with a backend "system" that manages all state centrally and in your control.

#### Clone() replaced with CreateCopy()

`NodeState.Clone()` is now a concrete method that calls `CreateCopy()` + `CopyTo()`. The new `protected abstract NodeState CreateCopy()` must be overridden by all direct NodeState subclasses.

```csharp
// Before
public override object Clone()
{
    var clone = new MyNodeState(Parent);
    CopyTo(clone);
    return clone;
}

// After
protected override NodeState CreateCopy()
{
    return new MyNodeState(Parent);
}
```

If you had custom deep-copy logic beyond what `CopyTo()` does, override `CopyTo()` instead.

#### BaseVariableState Read/Write helpers removed

The `protected ServiceResult Read(object, ref object)` and `protected object Write(object)` methods were removed.
Use the `CopyPolicy` property or the new `CopyOnWrite` bool directly with `CoreUtils.Clone()` for copy-on-read/write semantics.

#### OnAfterCreate gains CancellationToken

`OnAfterCreate(ISystemContext, NodeState)` now has an optional `CancellationToken ct = default` parameter.

> **Override migration required.** Update every override to include
> `CancellationToken ct = default`, pass it to the base call, and recompile
> against 2.0. The optional parameter preserves calls that omit the token, not
> overrides with the old signature; those produce `CS0115` when rebuilt.
> The 2.0 create lifecycle calls only the three-argument method. A previously
> compiled two-argument method does not override that callback, so its
> initialization logic is not invoked through it. Rebuild all custom node
> assemblies; do not rely on binary compatibility with 1.5.x.

```csharp
protected override void OnAfterCreate(ISystemContext context, NodeState node, CancellationToken ct = default)
{
    base.OnAfterCreate(context, node, ct);
}
```

#### Generated instance factories and the create lifecycle

Source-generated `CreateInstanceOf<Type>` factories materialise the typed node
graph and assign per-instance NodeIds, but intentionally leave the create
lifecycle open so callers can finish assembling and configuring the subtree.
In 2.0, registering the graph through `AddPredefinedNodeAsync`,
`AddNodeAsync`, or the synchronous predefined-node registration path
automatically runs `OnBeforeCreate`/`OnAfterCreate` and clears change masks
before indexing. Asynchronous predefined-node registration repairs typed
instance subtrees which still carry null, foreign-namespace, or
type-declaration-colliding NodeIds before the callbacks, so they see the
identifiers which enter the address space. Explicit NodeIds in a namespace
owned by the node manager are preserved. Synchronous registration keeps the
caller's identifiers; fluent helpers which materialise typed subtrees assign
their instance child NodeIds before registration. `NodeState.IsCreated`
exposes whether an individual node has completed that lifecycle.

`AddBehaviourToPredefinedNode` and its asynchronous equivalent receive the
created node. An override which replaces a passive node may therefore observe
the passive node's lifecycle before returning the active replacement; the
replacement is also completed before indexing. Keep external lifecycle side
effects idempotent when using that legacy replacement pattern.

If pre-registration code depends on values established by `OnAfterCreate`, or
sets state or handlers which an `OnAfterCreate` override would replace, call
`CreateAsPredefinedNode` first:

```csharp
MyMachineState machine =
    context.CreateInstanceOfMyMachineType(parent, browseName);

machine.CreateAsPredefinedNode(context);
SeedInitialState(machine);

await AddPredefinedNodeAsync(context, machine, cancellationToken);
```

`CreateAsPredefinedNode` is idempotent per node and still completes newly
added children. It does not re-run `OnAfterCreate` on an ancestor which was
already created, so add children whose handlers are wired by a parent callback
before the parent's first completion, or wire those late children explicitly.
This prevents duplicate callback execution when an explicitly completed
subtree is later registered.

Use the overload accepting a `CancellationToken` when completing a large
subtree outside registration. Cancellation is checked between nodes and the
same token is passed to each `OnAfterCreate` override. A later call resumes
completion for nodes whose callback did not finish.

Removing a node from a node manager is not the same as deleting the
`NodeState` object. `DeleteNodeAsync` removes the node from the address-space
index and disconnects monitoring and references, but does not invoke
`OnBeforeDelete`/`OnAfterDelete` and does not reset `IsCreated`.
`NodeState.Delete` performs that object lifecycle recursively. Full
address-space teardown invokes `Delete` for each root.

#### NodeState FindChild and CreateChild state NodeId assignment

`NodeState.FindChild` and `NodeState.CreateChild` now take
`assignInstanceNodeIds` as their last parameter, and the old four argument
`FindChild` / two argument `CreateChild` virtuals are gone. The parameter
defaults to `true`, so **call sites keep compiling and keep the 1.5.378
behaviour** — only overrides have to change:

```csharp
// Before
protected override BaseInstanceState FindChild(
    ISystemContext context,
    QualifiedName browseName,
    bool createOrReplace,
    BaseInstanceState replacement)
{
    if (browseName.Name == BrowseNames.MyChild)
    {
        return createOrReplace
            ? CreateOrReplaceMyChild(context, replacement)
            : MyChild;
    }
    return base.FindChild(context, browseName, createOrReplace, replacement);
}

// After — add the parameter and pass it on
protected override BaseInstanceState FindChild(
    ISystemContext context,
    QualifiedName browseName,
    bool createOrReplace,
    BaseInstanceState replacement,
    bool assignInstanceNodeIds = true)
{
    if (browseName.Name == BrowseNames.MyChild)
    {
        return createOrReplace
            ? CreateOrReplaceMyChild(context, replacement, assignInstanceNodeIds)
            : MyChild;
    }
    return base.FindChild(
        context, browseName, createOrReplace, replacement, assignInstanceNodeIds);
}
```

A missing override raises `CS0115` (`no suitable method found to
override`), so the compiler points at every site that needs the parameter.
Repeat the `= true` default in the override so callers bound to your derived
type keep the same behaviour.

Why: a node copy creates each child and then initialises it from its source,
which overwrites any NodeId minted along the way. Passing
`assignInstanceNodeIds: false` — what `NodeState.Create(context, source)`
now does — stops the `ISystemContext.NodeIdFactory` from being asked for
identifiers that are immediately discarded, and leaked by factories that
track outstanding allocations. Thread the argument into every
`CreateOrReplace<Child>` call your override makes; source generated types
already do. See
[Custom node types and assignment control](../../NodeManagers.md#custom-node-types-and-assignment-control).

### Custom NodeManager interface changes

Custom managers that implement `INodeManager` or `INodeManager2` directly
must update their method signatures when moving from 1.5.378 to 2.0. The
request inputs below use `ArrayOf<T>` instead of `IList<T>`:

| Method | Request input |
| --- | --- |
| `Read` | `ArrayOf<ReadValueId>` |
| `HistoryRead` | `ArrayOf<HistoryReadValueId>` |
| `Write` | `ArrayOf<WriteValue>` |
| `HistoryUpdate` | `ArrayOf<HistoryUpdateDetails>` |
| `Call` | `ArrayOf<CallMethodRequest>` |
| `CreateMonitoredItems` | `ArrayOf<MonitoredItemCreateRequest>` |
| `ModifyMonitoredItems` | `ArrayOf<MonitoredItemModifyRequest>` |

The corresponding async service interfaces and overrides take the same
request inputs. `INodeManager2.GetPermissionMetadata` and
`IAsyncNodeManager.GetPermissionMetadataAsync` change the cache parameter
from `Dictionary<NodeId, List<object>>` to `Dictionary<NodeId, Variant[]>`.
Both `INodeManagerFactory.NamespacesUris` and
`IAsyncNodeManagerFactory.NamespacesUris` return `ArrayOf<string>` instead
of `StringCollection`.

`IConditionRefreshAsyncNodeManager.ConditionRefreshAsync` now returns
`ValueTask<ServiceResult>` rather than `ValueTask`. Return the refresh
result from the implementation.

Direct `IAsyncNodeManager` implementations must also provide
`FindMethodStateAsync`, `SessionActivatedAsync`,
`ValidateEventRolePermissionsAsync`, `ValidateRolePermissionsAsync`,
`IsMultipleEventConsumerNode`, `AddNode` and `AddRootNotifier`. The interface
also inherits `INodeManagementAsyncNodeManager` and `INodeIdFactory`.
Implement their members as well. If the manager does not accept
NodeManagement requests, set `AllowNodeManagement` to `false`; the master
dispatcher rejects requests routed to it with `BadUserAccessDenied`.
`SessionActivatedAsync` should invalidate cached permissions for monitored
items belonging to the session when its identity changes.

`TransferMonitoredItems` adds a `MonitoredItemTransferOptions` parameter.
The async `ITransferMonitoredItemsAsyncNodeManager` method adds the same
parameter before the cancellation token. When `DeferInitialValues` is true,
the manager must not call `SetupResendDataTrigger` during the transfer. When
a coordinated transfer commits, it schedules requested initial values for
successful items whose source is available. If an item's source is lost, it
reschedules the lost-node status instead when initial values were requested.
When the option is false and initial values were requested, the manager
schedules them for its transferred items as before.

`CustomNodeManager2` and `AsyncCustomNodeManager` retain the old transfer
overloads as obsolete methods. They forward calls to the new overloads, but
server dispatch calls the new overloads directly. A subclass that only
overrides an old overload must move its override to the overload accepting
`MonitoredItemTransferOptions`; the old override is not invoked by the new
dispatch path. Review the transfer logic for deferred initial values as part
of that change.

#### Protected service overrides

Subclasses of `CustomNodeManager2` that override the hooks below must update
their signatures. In 1.5.378 these hooks accepted `IList<T>` for the request
collection; the current signatures use the following inputs:

| Protected hook | Request input |
| --- | --- |
| `Read` | `ArrayOf<ReadValueId>` |
| `Write` | `ArrayOf<WriteValue>` |
| `HistoryRead`, `HistoryReleaseContinuationPoints` | `ArrayOf<HistoryReadValueId>` |
| `HistoryReadRawModified`, `HistoryReadProcessed` | `ArrayOf<HistoryReadValueId>` |
| `HistoryReadAtTime`, `HistoryReadEvents` | `ArrayOf<HistoryReadValueId>` |
| `HistoryUpdate` | `ArrayOf<HistoryUpdateDetails>` |
| `HistoryUpdateData` | `ArrayOf<UpdateDataDetails>` |
| `HistoryUpdateStructureData` | `ArrayOf<UpdateStructureDataDetails>` |
| `HistoryUpdateEvents` | `ArrayOf<UpdateEventDetails>` |
| `HistoryDeleteRawModified` | `ArrayOf<DeleteRawModifiedDetails>` |
| `HistoryDeleteAtTime` | `ArrayOf<DeleteAtTimeDetails>` |
| `HistoryDeleteEvents` | `ArrayOf<DeleteEventDetails>` |
| `CallInternalAsync(OperationContext, ...)` | `ArrayOf<CallMethodRequest>` |

The result and error collections remain `IList<T>`. For example, a protected
`Read` override now has this signature:

```csharp
protected override void Read(
    ServerSystemContext context,
    ArrayOf<ReadValueId> nodesToRead,
    IList<DataValue> values,
    IList<ServiceResult> errors,
    List<NodeHandle> nodesToValidate,
    IDictionary<NodeId, NodeState> cache)
{
    base.Read(context, nodesToRead, values, errors, nodesToValidate, cache);
}
```

The old protected signatures are not present in the current implementation.
An override with an old signature will not compile against it. Update the
override and any base calls, then rebuild the subclass. Compatibility with
previously compiled 1.5.378 subclasses has not been qualified.

#### Core, diagnostics and configuration managers

`ICoreNodeManager`, `IDiagnosticsNodeManager`, and
`IConfigurationNodeManager` now extend `IAsyncNodeManager`. Direct
implementations must provide its async service members as well as the
specialized interface members. Several synchronous methods from 1.5.378 have
async replacements:

| Interface | 1.5.378 method | Current method |
| --- | --- | --- |
| `ICoreNodeManager` | `ImportNodes` | `ImportNodesAsync` |
| `IConfigurationNodeManager` | `CreateNamespaceMetadataState` | `CreateNamespaceMetadataStateAsync` |
| `IConfigurationNodeManager` | `GetNamespaceMetadataState` | `GetNamespaceMetadataStateAsync` |
| `IDiagnosticsNodeManager` | `AddAggregateFunction`, `AddModellingRule` | Append `Async` |
| `IDiagnosticsNodeManager` | `CreateServerDiagnostics`, `CreateSessionDiagnostics` | Append `Async` |
| `IDiagnosticsNodeManager` | `CreateSubscriptionDiagnostics` | Append `Async` |
| `IDiagnosticsNodeManager` | `DeleteSessionDiagnostics`, `DeleteSubscriptionDiagnostics` | Append `Async` |
| `IDiagnosticsNodeManager` | `GetDefaultHistoryCapabilities`, `SetDiagnosticsEnabled` | Append `Async` |
| `IDiagnosticsNodeManager` | `UpdateServerEventNotifier` | Append `Async` |

These replacements return `ValueTask` or `ValueTask<T>` and accept an optional
`CancellationToken`. Await them and propagate cancellation through your async
call chain. `GetNamespaceMetadataStateAsync` can return null when no metadata
node is found. The interfaces also declare new members, including
`PublishConformanceUnitsAsync` on `IDiagnosticsNodeManager` and
`DefaultPermissionsChanged` and `DrainPendingApplyChangesAsync` on
`IConfigurationNodeManager`; review the interface definitions when updating a
direct implementation.

The three `IMasterNodeManager` properties also return interfaces rather than
the concrete manager classes:

| Property | Current return type |
| --- | --- |
| `CoreNodeManager` | `ICoreNodeManager?` |
| `DiagnosticsNodeManager` | `IDiagnosticsNodeManager?` |
| `ConfigurationNodeManager` | `IConfigurationNodeManager?` |

Use the interface members and handle null. The default master resolves these
properties from the current system slots; a property returns null when its
slot is withdrawn or its current manager does not implement that role.

`DiagnosticsNodeManager` now derives from `AsyncCustomNodeManager` rather
than `CustomNodeManager2`; `CoreNodeManager` also derives from
`AsyncCustomNodeManager`. Review overrides in subclasses against the current
base classes. For example, the former virtual
`DiagnosticsNodeManager.UpdateServerEventNotifier` is now
`UpdateServerEventNotifierAsync(CancellationToken)`.

`CoreNodeManager` no longer exposes the `GetLocalNode` overloads or
`Find(NodeId, string)`. Its inherited `Find(NodeId)` searches the predefined
node map and returns `NodeState?`, not `ILocalNode`; it does not translate a
browse path. Review callers that traverse paths or depend on local-node
interfaces rather than treating this lookup as a direct replacement.

`CoreNodeManager.DataLock` is also removed. A caller-owned lock does not
synchronize with manager operations. Code that relied on a sequence remaining
atomic with other manager calls needs a different coordination design.

`DiagnosticsNodeManager.OnGetMonitoredItems`, `OnResendData`,
`OnSetSubscriptionDurable`, and `OnUnlockServer` changed from public to
protected. Calls from outside a subclass no longer compile; review those
calls against the application operation they implement. This guide does not
identify a one-for-one replacement. `OnLockServer` remains public, but its
arguments changed from `IList<object>` inputs and outputs to
`ArrayOf<Variant>` inputs and `List<Variant>` outputs.

#### Master NodeManager service calls

`IMasterNodeManager` service methods use `ArrayOf<T>` for the following
request inputs and result values instead of the corresponding 1.5.378
collection types. Their diagnostic results also use `ArrayOf<DiagnosticInfo>`.
Update both callers and direct implementations of the interface.

| Method | Request input | Result values |
| --- | --- | --- |
| `BrowseAsync` | `ArrayOf<BrowseDescription>` | `ArrayOf<BrowseResult>` |
| `BrowseNextAsync` | `ArrayOf<ByteString>` | `ArrayOf<BrowseResult>` |
| `CallAsync` | `ArrayOf<CallMethodRequest>` | `ArrayOf<CallMethodResult>` |
| `HistoryReadAsync` | `ArrayOf<HistoryReadValueId>` | `ArrayOf<HistoryReadResult>` |
| `HistoryUpdateAsync` | `ArrayOf<ExtensionObject>` | `ArrayOf<HistoryUpdateResult>` |
| `ReadAsync` | `ArrayOf<ReadValueId>` | `ArrayOf<DataValue>` |
| `TranslateBrowsePathsToNodeIdsAsync` | `ArrayOf<BrowsePath>` | `ArrayOf<BrowsePathResult>` |
| `WriteAsync` | `ArrayOf<WriteValue>` | `ArrayOf<StatusCode>` |

`CreateMonitoredItemsAsync` and `ModifyMonitoredItemsAsync` also take
`ArrayOf<T>` request inputs, while their result lists remain `IList<T>`.
`RegisterNodes` takes an `ArrayOf<NodeId>` and writes another to its output
parameter; `UnregisterNodes` takes an `ArrayOf<NodeId>`.
`TransferMonitoredItemsAsync` adds `MonitoredItemTransferOptions` before
the cancellation token.

`ArrayOf<T>` distinguishes a null OPC UA array from an empty one. Check
`IsNull` where that distinction matters: `ToArray()` returns null for a null
array, while `ToList()` returns an empty list. See
[ArrayOf and MatrixOf](types.md#arrayof-and-matrixof) for conversion guidance.

Direct implementations must also provide `SessionActivatedAsync`,
`FindNodeInAddressSpaceAsync`, `AddNodesAsync`, `DeleteNodesAsync`, and the
service overloads of `AddReferencesAsync` and `DeleteReferencesAsync`. The
reference service overloads are separate from the existing overloads that take
a `NodeId` and a list of references.

#### Synchronous adapter return type

`ToSyncNodeManager` now returns `INodeManager` rather than `INodeManager2`.
If the supplied async manager already implements `INodeManager`, the factory
returns it unchanged; that instance might not implement `INodeManager2`.
Code that needs the extended interface must check the returned instance and
handle a manager that does not provide it. Compatibility with previously
compiled 1.5.378 callers and providers has not been qualified.

### INodeManager3 - new role-permission and method-resolution hooks

2.0 introduces `INodeManager3`, which extends `INodeManager2` with hooks for
per-role permission evaluation, session activation and resolving the target
of a `Call` request. `CustomNodeManager2` implements these members.

After updating the base interface signatures above, a direct implementation
of `INodeManager` or `INodeManager2` can omit `INodeManager3`. If a synchronous
manager also does not implement `IAsyncNodeManager`, its adapter returns
`Good` for the manager-specific role-validation hooks, returns no method from
the method-resolution hook and does not call a manager-specific session
activation hook. Other server authorization checks may still apply. Implement
`INodeManager3` or derive from `CustomNodeManager2` to participate in those
hooks. The master's call prevalidation checks the object's and method's
`Call` permissions only when the method-resolution hook returns a method.
Review the manager's authorization path if the hook is omitted.

### NodeState guards itself; NodeBrowser is single-consumer (UA0027)

In 1.5.378 a caller wanting a consistent view of a node took a lock on the node
instance itself — `lock (source)` — and `NodeBrowser` handed its own lock to
derived browsers through `protected object DataLock`. Both are gone in 2.0.

**`NodeState` guards its own state.** Attributes, children, notifiers and
references each have a private lock inside the node, and
`NodeState.CreateBrowser` holds a browse lock while it assembles the browser.
No code in the stack locks a node instance, and no caller should:

```csharp
// was
lock (source)
{
    browser = source.CreateBrowser(context, view, referenceType, includeSubtypes,
        browseDirection, default, null, false);
}

// now
INodeBrowser browser = source.CreateBrowser(context, view, referenceType,
    includeSubtypes, browseDirection, default, null, false);
```

Locking the node was also the only way to make a check-then-act pair atomic.
`ReferenceExists` and `AddReference` each guard themselves, but the pair does
not, so use `AddReferenceIfMissing`:

```csharp
// was
lock (node)
{
    if (!node.ReferenceExists(ReferenceTypeIds.HasNotifier, true, ObjectIds.Server))
    {
        node.AddReference(ReferenceTypeIds.HasNotifier, true, ObjectIds.Server);
    }
}

// now
node.AddReferenceIfMissing(ReferenceTypeIds.HasNotifier, true, ObjectIds.Server);
```

**`NodeBrowser.DataLock` is removed.** A browser is single-consumer: it belongs
to whoever created it and performs no synchronization of its own. The exposed
lock was an inheritance-level locking contract that a derived browser could not
reason about — nothing said how long it could be held or what else took it —
while both server browse paths already serialize on the owning side. A derived
browser drops the `lock` statement and keeps the body:

```csharp
// was
public override IReference Next()
{
    lock (DataLock)
    {
        IReference reference = base.Next();
        ...
    }
}

// now
public override IReference Next()
{
    IReference reference = base.Next();
    ...
}
```

If a browser really is shared between threads — the instance parked in a
continuation point for `BrowseNext` is the canonical case — serialize it where
it is owned, not inside the browser.

**Overriding `CreateBrowser`.** A node type that builds its own browser instead
of delegating to `base.CreateBrowser` must fill it through the new
`protected NodeState.PopulateBrowserSynchronized`, not by calling
`PopulateBrowser` directly. `PopulateBrowser` is invoked with the node's browse
lock held, so an override must not block on external work such as I/O; defer
that to the browser's own `Next()`.

Analyzer `UA0027` reports every remaining `NodeBrowser.DataLock` reference.

### Browse continuation lifetime

A Browse continuation stores a weak identity for its recorded generation. The
identity alone does not retain the generation, its NodeManager, or a routing
view. `ContinuationPoint.Data` and `NodeToBrowse` can still retain cursor data,
a node handle, or objects reachable from them. The base
`ContinuationPoint.Dispose` implementation disposes `Data` at most once when it
implements `IDisposable` but does not clear the property. Unlike 1.5.x, it does
not catch exceptions from that call. Code that retains the point can therefore
retain those objects after disposal.

In 1.5.x, `ContinuationPoint.Manager` stored the assigned manager directly. The
setter now records a weak generation identity. The getter returns that
generation's manager when available and `null` when the identity cannot be
resolved, even though the property is declared non-nullable. A stack adapter can
resolve to the existing generation of its wrapped manager, so the getter can
return a different object from the one assigned. Base disposal clears the
identity, and assigning `null` throws `ArgumentNullException`.

A `BrowseNext` request that resumes a point must enter the recorded generation's
gate. If entry fails, the server returns `BadContinuationPointInvalid`. A
release request can dispose the point even if the gate is closed. Targets on
later pages are resolved through the current request view.

A lazy browser can keep its cursor in `ContinuationPoint.Data` and return
references through `Next()` or `NextAsync()` as before. The standard file-system
`DirectoryBrowser` works without pre-enumerating entries or capturing target
owners. A point that retains this browser can also retain its
`FileSystemNodeManager` host, even after the point is disposed.

### NodeBrowser gains an async iteration seam

`INodeBrowser` and `NodeBrowser` gain
`ValueTask<IReference?> NextAsync(CancellationToken cancellationToken = default)`.
`AsyncCustomNodeManager.BrowseAsync` and `TranslateBrowsePathAsync` iterate a
browser through it, so a browser whose references depend on I/O — the
aggregation case, where the references come from another server — can `await`
the fetch instead of blocking a request worker on it.

**Nothing changes for an existing browser.** The default `NextAsync` completes
synchronously with the result of `Next()`, so a browser that only overrides
`Next()` is iterated exactly as before. Only a type that implements
`INodeBrowser` directly, without deriving from `NodeBrowser`, has to add the
member.

A browser that was bridging sync-over-async moves the real work into
`NextAsync` and keeps `Next()` as the bridge for the remaining synchronous
consumers (`UANodeSetHelpers` export, `CustomNodeManager2`):

```csharp
// was — the fetch ran under GetResult() on every browse
public override IReference Next()
{
    return NextAsync().GetAwaiter().GetResult();
}

private async Task<IReference> NextAsync() { ... }

// now — the async server awaits the fetch; Next() is only reached by sync callers
public override async ValueTask<IReference?> NextAsync(CancellationToken cancellationToken)
{
    IReference? reference = base.Next();
    if (reference != null)
    {
        return reference;
    }
    ...
}

public override IReference? Next()
{
    return NextAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult();
}
```

Browser creation stays synchronous: `OnCreateBrowser` runs under the node's
browse lock, so a browser does its first fetch lazily in `NextAsync`, not in its
constructor.

## `INodeCache` changes

Version 2.0 collapses the two parallel node-cache contracts into a single public interface and removes the remaining synchronous wrappers from the cache surface.

**Key changes**:

- **`ILruNodeCache` is removed.** `LruNodeCache` now implements only `INodeCache`. All members previously on `ILruNodeCache` (the   NodeId-keyed `Get*` family and `LoadTypeHierarchyAsync`) are now
  members of `INodeCache`.
- **All async methods on `INodeCache` return `ValueTask` / `ValueTask<T>`** (was `Task<T>` for `FindAsync`, `FetchNodeAsync`, `FetchNodesAsync`, `FetchSuperTypesAsync`, `FindReferencesAsync`).
  Callers that simply `await` these methods need no change. Callers that store the result in a `Task` variable, return the bare task, or re-await the same task must wrap with `.AsTask()` once.
- **`void INodeCache.LoadUaDefinedTypes(ISystemContext)` is removed.** The LRU implementation populates lazily and the prior method body was a no-op. Drop the call from your code; the cache is ready to
  use.
- **`bool ILruNodeCache.IsTypeOf(NodeId, NodeId)` is removed.** Use `IAsyncTypeTable.IsTypeOfAsync(NodeId, NodeId, CancellationToken)` instead — `INodeCache` inherits from `IAsyncTypeTable` so the
  method is reachable on the same instance.
- **`NodeCacheObsolete` synchronous extensions are removed.** The blocking wrappers `Find`, `FetchNode`, `FetchNodes`, `FetchSuperTypes`, `FindReferences`, `GetDisplayText`, `IsKnown`, `FindSuperType`, and
  `Exists` were obsoleted in 1.5.378 and now no longer compile. Switch to the matching async methods (`FindAsync`, `FetchNodeAsync`, …).
- ** Moving of several methods to extension classes**: The following members were moved to extension methods on `NodeCacheExtensions` (in the same `Opc.Ua` namespace, so no `using` changes needed). These methods are thin wrappers around the core `INodeCache` surface and preserve the old signatures where possible.

    | Removed from interface | Replacement |
    |---|---|
    | `GetSuperTypeAsync(NodeId, ct)` | inherited `IAsyncTypeTable.FindSuperTypeAsync(NodeId, ct)` (identical semantics — the interface methods returned the same `NodeId.Null`-on-miss value) |
    | `FindReferencesAsync(ExpandedNodeId, NodeId, bool, bool, ct)` | inherited `IAsyncNodeTable.FindAsync(source, refType, isInverse, includeSubtypes, ct)` (identical signature). A thin extension method preserves the old name for callers that prefer it. |
    | `FindReferencesAsync(ArrayOf<ExpandedNodeId>, ArrayOf<NodeId>, …)` | extension method on `NodeCacheExtensions` (same signature). |
    | `FindAsync(ArrayOf<ExpandedNodeId>, ct)` | extension method on `NodeCacheExtensions` that loops over the inherited `FindAsync(ExpandedNodeId)`. |
    | `FetchSuperTypesAsync(ExpandedNodeId, ct)` | extension method that loops `FindSuperTypeAsync`. |
    | `GetNodeWithBrowsePathAsync(NodeId, ArrayOf<QualifiedName>, ct)` | extension method on `NodeCacheExtensions`. |
    | `GetBuiltInTypeAsync(NodeId, ct)` | extension method on `NodeCacheExtensions`. |
    | `GetDisplayTextAsync(INode \| ExpandedNodeId \| ReferenceDescription, ct)` | three extension methods on `NodeCacheExtensions`. |

  External implementations of `INodeCache` no longer need to implement these members. Call sites that already used `using Opc.Ua;` keep compiling unchanged because the extensions live in the same namespace.

The new `INodeCache` deliberately keeps two name conventions side by side. The XML doc on `INodeCache` spells this out as well:

| Family | Identity | Result | Behavior |
|---|---|---|---|
| `Find*` / `Fetch*` | `ExpandedNodeId` | nullable | `Find*` consults the cache, then the server; `Fetch*` always re-reads from the server. |
| `Get*` | `NodeId` | non-nullable / throws | LRU-style direct hit; cheaper for in-process callers that already have a local `NodeId`. |

**Migration**:

```csharp
// Before — Task-returning + sync helpers
INodeCache cache = session.NodeCache;
cache.LoadUaDefinedTypes(session.SystemContext); // removed
ArrayOf<INode?> nodes = await cache.FindAsync(nodeIds);
Task<Node?> tn = cache.FetchNodeAsync(nodeId);   // returned Task<T>
bool isType = cache.IsTypeOf(sub, super);        // sync, was on ILruNodeCache
```

```csharp
// After — single INodeCache surface, all async, no sync IsTypeOf
INodeCache cache = session.NodeCache;
ArrayOf<INode?> nodes = await cache.FindAsync(nodeIds);
ValueTask<Node?> tn = cache.FetchNodeAsync(nodeId);
bool isType = await cache.IsTypeOfAsync(sub, super);
```

---

**See also**

- Related: [types.md](types.md), [alarms-model-change.md](alarms-model-change.md), [sessions-subscriptions.md](sessions-subscriptions.md).
- [2.0 migration index](README.md) — analyzer quick-start + symptom → sub-doc table.
- [Migration Guide](../../MigrationGuide.md) — landing page across versions.
