# Node States and INodeCache

> **When to read this:** Read this when migrating custom NodeManagers, `NodeState` clone / read / write helpers (`Clone` -> `CreateCopy`, removed `BaseVariableState` helpers), the new `INodeManager3` role-permission hooks, `OnAfterCreate(CancellationToken)`, predefined-node processing, generics on `BaseVariableState` / `BaseVariableTypeState`, code that took `lock (node)` on a `NodeState` or used `NodeBrowser.DataLock`, or `INodeCache.InvalidateNode`.

## Node States

### Generics and Typed BaseVariableState and BaseVariableTypeState

With the changes to Variant, the generic node state classes reflecting the inner value of the variant "value" have been changed to not rely on "casting" from object to T. The conversion is "baked in" when creating an instance of a typed state using a "builder" struct. Whether the value is scalar, array or matrix is irrelevant to which builder to use. There are 3 situations and the respective builder struct to use:

1. T is a built in type -> use `VariantBuilder`
2. T is a instance of `IEncodeable` (a complex structure) -> Use `StructureBuilder<T>` where T is the name of the structure.
3. T is an instance of Enum (an enumeration) -> Use `EnumBuilder<T>` where T is the name fo the enumeration type.

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

See [NodeStates](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/src/Opc.Ua.Types/State/readme.md) document for more information.

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

> **⚠ Silent regression.** Source-compatible, but **binary-incompatible**. Pre-compiled assemblies whose overrides still target the old `OnAfterCreate(ISystemContext, NodeState)` signature will silently no-op at runtime against 2.0 - the CLR resolves virtual overrides by exact signature, finds no match, and falls back to the base implementation. **No runtime exception is thrown** to alert the developer. The only fix is to **recompile** the consuming assembly against 2.0 so the override binds to the new three-argument signature.

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
[Custom node types and assignment control](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/NodeManagers.md#custom-node-types-and-assignment-control).

### INodeManager3 - new role-permission and method-resolution hooks

2.0 introduces `INodeManager3`, an extension of `INodeManager2` that surfaces explicit hooks for per-role permission evaluation and for resolving the target of a `Call` request. `CustomNodeManager2` implements the new members with safe defaults that mirror the previous behavior, so node managers that already derive from `CustomNodeManager2` need no changes.

Custom node managers that implement `INodeManager` / `INodeManager2` **directly** (not via `CustomNodeManager2`) silently lose the new behavior: the server probes for `INodeManager3` at the call site, and node managers that do not implement it fall through to the legacy code path. This is not a build break - it is a silent feature-availability regression. Either derive from `CustomNodeManager2` or implement `INodeManager3` explicitly to participate in role-permission evaluation and the new method-resolution contract.

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
    | `GetDisplayTextAsync(INode | ExpandedNodeId | ReferenceDescription, ct)` | three extension methods on `NodeCacheExtensions`. |

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
- [Migration Guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/MigrationGuide.md) — landing page across versions.
