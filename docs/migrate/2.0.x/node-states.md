# Node States and INodeCache

> **When to read this:** Read this when migrating custom NodeManagers, `NodeState` clone / read / write helpers (`Clone` -> `CreateCopy`, removed `BaseVariableState` helpers), the new `INodeManager3` role-permission hooks, `OnAfterCreate(CancellationToken)`, predefined-node processing, generics on `BaseVariableState` / `BaseVariableTypeState`, or `INodeCache.InvalidateNode`.

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

> **⚠ Silent regression.** Source-compatible, but **binary-incompatible**. Pre-compiled assemblies whose overrides still target the old `OnAfterCreate(ISystemContext, NodeState)` signature will silently no-op at runtime against 2.0 - the CLR resolves virtual overrides by exact signature, finds no match, and falls back to the base implementation. **No runtime exception is thrown** to alert the developer. The only fix is to **recompile** the consuming assembly against 2.0 so the override binds to the new three-argument signature.

```csharp
protected override void OnAfterCreate(ISystemContext context, NodeState node, CancellationToken ct = default)
{
    base.OnAfterCreate(context, node, ct);
}
```

### INodeManager3 - new role-permission and method-resolution hooks

2.0 introduces `INodeManager3`, an extension of `INodeManager2` that surfaces explicit hooks for per-role permission evaluation and for resolving the target of a `Call` request. `CustomNodeManager2` implements the new members with safe defaults that mirror the previous behavior, so node managers that already derive from `CustomNodeManager2` need no changes.

Custom node managers that implement `INodeManager` / `INodeManager2` **directly** (not via `CustomNodeManager2`) silently lose the new behavior: the server probes for `INodeManager3` at the call site, and node managers that do not implement it fall through to the legacy code path. This is not a build break - it is a silent feature-availability regression. Either derive from `CustomNodeManager2` or implement `INodeManager3` explicitly to participate in role-permission evaluation and the new method-resolution contract.

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
- [Migration Guide](../../MigrationGuide.md) — landing page across versions.

