# NodeId Assignment

Every node a server creates at runtime needs a NodeId. This document
describes the mechanisms the stack uses to produce one: the factory
contract, the identifier formats, how type declarations differ from
instances, what the source generator emits, and what each NodeManager base
class does.

## Contents

- [The contract](#the-contract)
- [DefaultNodeIdFactory](#defaultnodeidfactory)
- [Types and instances](#types-and-instances)
- [What the source generator emits](#what-the-source-generator-emits)
- [NodeManager behaviour](#nodemanager-behaviour)
- [Configuring the factory](#configuring-the-factory)
- [Inventory](#inventory)

## The contract

`INodeIdFactory` (`Opc.Ua.Types/State/ISystemContext.cs`) has one member:

```csharp
NodeId New(ISystemContext context, NodeState node);
```

It reaches node code through `ISystemContext.NodeIdFactory`. A NodeManager
publishes itself as that factory in its constructor
(`SystemContext.NodeIdFactory = this`), so any node-authoring code holding
the manager's `SystemContext` mints identifiers through the manager.

The property is nullable, and null means *do not assign*. A node copy
deliberately hides the factory from the children it materialises, so code
that cannot work without one calls
`ISystemContext.RequireNodeIdFactory()` and fails with a diagnosable
error rather than a `NullReferenceException`.

`New` may be called more than once for the same node — `AssignNodeIds`
walks a subtree on every create pass — so an implementation has to be
idempotent or the address space ends up with orphaned references.

## DefaultNodeIdFactory

`Opc.Ua.Server/NodeManager/DefaultNodeIdFactory.cs` is the stack's
implementation. It is immutable and safe to share.

### The canonical path

Every mode except `Counter` derives from one canonical browse path: a
length-prefixed encoding of the parent identifier and the child browse
name.

```
v1:10:l:6:s=Root:10:l:6:Group1
```

Each segment carries its own length, so no combination of parent and
browse name can produce the same path as a different combination. The
classic `{parentIdentifier}_{browseName}` convention could: `A_B` plus `C`
and `A` plus `B_C` both gave `A_B_C`. The parent segment keeps the
parent's identifier type prefix (`s=`, `i=`, `g=`, `b=`), so a string
parent `"42"` never reads as a numeric parent `42`.

Segments are qualified by namespace:

| Prefix | Meaning |
|---|---|
| `l:` | in the namespace being minted into |
| `z:` | in namespace 0 |
| `u:<uri>:` | in another namespace, named by URI |
| `x:<index>:` | in another namespace that has no URI registered |

The URI form is what makes an identifier independent of namespace-table
ordering. The index form is a fallback for an unregistered namespace; it
is deliberately distinct so identifiers minted before the namespace was
registered cannot collide with the ones minted after.

### Modes

`NodeIdAssignmentMode` selects how the path becomes an identifier.

| Mode | Identifier | Stable across restarts | Collides when browse paths repeat |
|---|---|---|---|
| `None` | none — raises `BadConfigurationError` | — | — |
| `Numeric` | first 32 bits of the path's SHA-256 | yes | yes, plus hash collisions |
| `String` | the canonical path verbatim | yes | yes |
| `Guid` | first 128 bits of the path's SHA-256 | yes | yes |
| `Opaque` | first 128 bits of the path's SHA-256 | yes | yes |
| `Counter` | sequential number | no | no |

`String` is the default. It is the only mode that cannot collide for
distinct browse paths, because the canonical path is injective; the others
project it through SHA-256 and inherit that hash's collision probability
(negligible for `Guid`/`Opaque`, real but small for `Numeric`).

`Counter` is for nodes whose browse paths repeat over time — per-session
diagnostics objects, inference jobs, rediscovered assets. Its counter
starts above `0x40000000`, out of reach of any authored model, so a
long-running server cannot walk a runtime instance onto a type node.
(The predefined-node index takes the last writer, so such a collision
silently replaces the type rather than failing.)

### Which namespace

Every identifier is minted into the factory's `DefaultNamespaceIndex` —
the NodeManager's own namespace. Neither the parent's namespace nor the
browse name's is consulted:

- a parent can belong to a companion-specification model whose NodeIds
  are fixed by its NodeSet (`DeviceSet` in the DI namespace, for
  instance). A server may not mint identifiers there.
- a browse name only names the type that declared the child.

A NodeManager whose instance namespace is not its first one rebases the
factory with `WithDefaultNamespaceIndex`.

### When an existing NodeId is kept

`New` keeps a node's current NodeId only when this factory could have
minted it itself; otherwise it mints a fresh one.

For the derived modes that test is free: minting twice from the same
browse path gives the same identifier, so re-minting is idempotent by
construction, and an identifier that does not match is provably not this
factory's. `Counter` cannot re-derive anything, so it recognises its own
work by the reserved numeric range it mints into.

This is what stops an instance from keeping a type declaration's
identifier — see the next section.

### Nodes with no derivable path

A node with no browse name, or one hanging off a transient parent that
has no NodeId (an event instance and its fields), has no stable path.
Those fall back to the counter whatever mode is selected, so a NodeManager
never has to special-case them.

## Types and instances

An information model ships **type declarations**: `PumpType` and every
child below it, each with a NodeId fixed by the NodeSet. When a server
creates an instance, `NodeState.CreateInstance` copies that subtree — and
the copy carries the declaration's NodeIds.

Rebasing the copy onto per-instance identifiers is what
`NodeState.AssignNodeIds` does: it walks the subtree calling
`INodeIdFactory.New` on each node, collects an old-to-new mapping table,
and hands it to `UpdateReferenceTargets` so references inside the subtree
follow.

If `New` returns the declaration NodeId unchanged, every instance of the
type aliases onto the type's own nodes. The failure is silent: the
predefined-node index replaces rather than rejects, so the ObjectType
quietly becomes an instance node. Guarding against that is the reason
`DefaultNodeIdFactory` re-mints anything it did not mint itself, and the
reason the generator's helpers check the declaration constant before
rebasing.

Type nodes themselves are never passed through `AssignNodeIds`; they are
loaded from the NodeSet and registered as predefined nodes.

## What the source generator emits

For each child of a generated type the generator emits helpers that
materialise it, each with its own NodeId policy.

`Create{Type}` / `CreateInstanceOf{Type}Type(parent, browseName)` — builds
an instance and rebases the subtree through the context's factory.

`Add{Child}(context, nodeId = default)`:

| Argument | Behaviour |
|---|---|
| `nodeId` non-null | the child takes that NodeId, then its descendants are rebased around it |
| `nodeId` null | rebased through the factory, but **only if** the child still carries its declaration constant |

`CreateOrReplace{Child}(context, replacement, assignInstanceNodeIds)`:

| Argument | Behaviour |
|---|---|
| `assignInstanceNodeIds` false | the child keeps whatever NodeId it has |
| `assignInstanceNodeIds` true | rebased, but only if the NodeId is null or still the declaration constant |

The declaration-constant guard (`nodeState.NodeId.Equals(TypeNodeIdConstant)`)
is the generator's compile-time way of asking "is this still a
declaration identifier?". It also means a caller who assigned an
identifier explicitly keeps it.

## NodeManager behaviour

### CustomNodeManager2

Unchanged, for backwards compatibility:

```csharp
public virtual NodeId New(ISystemContext context, NodeState node)
{
    return node.NodeId;
}
```

It mints nothing. A node keeps whatever NodeId it already has, and a node
with none keeps `NodeId.Null`. Subclasses that need identifiers override
`New` themselves. Existing servers built on this base see no change in
behaviour.

### AsyncCustomNodeManager

`New` delegates to the `NodeIdFactory` property and does nothing else. The
manager holds no counter of its own. A subclass selects its identifier
style by assigning that factory in its constructor rather than by
overriding `New`:

```csharp
NodeIdFactory = NodeIdFactory.WithMode(NodeIdAssignmentMode.Counter);
```

`WithMode` and `WithDefaultNamespaceIndex` both return a copy, so a
factory shared through dependency injection is never mutated. Assigning a
factory that names no namespace (a bare `new DefaultNodeIdFactory(mode)`)
adopts the manager's own namespace; one that names a namespace is left
alone.

The default is `NodeIdAssignmentMode.String`, so a manager that overrides
nothing gets deterministic browse-path identifiers.

### FluentNodeManagerBase and the fluent builders

`FluentNodeManagerBase` derives from `AsyncCustomNodeManager` and inherits
its behaviour. The fluent builders — `AddObject`, `WithProperty`,
`CreateInstance`, and the alarm and state-machine helpers — mint through
`FluentNodeRegistration.AssignNodeId`, which routes to the owning
manager's `New`. They no longer each spell out a
`{parentIdentifier}_{browseName}` concatenation.

Inside a `Configure` delegate the mode can be selected for everything
created after it:

```csharp
builder.WithNodeIdAssignment(NodeIdAssignmentMode.Guid);
```

A builder whose NodeManager is not an `AsyncCustomNodeManager` (the
mock-backed unit tests) keeps the older concatenated shape.

## Configuring the factory

Through dependency injection, once for the whole server:

```csharp
builder.AddNodeIdFactory(NodeIdAssignmentMode.Guid);
```

`DependencyInjectionStandardServer` resolves the registered
`DefaultNodeIdFactory`, `StandardServer` threads it into
`ServerInternalData`, and every `AsyncCustomNodeManager` picks it up
through `INodeIdFactoryProvider`, rebased onto its own namespace.

## Inventory

Everything in the stack that participates in NodeId assignment.

### Contract

| Member | Location |
|---|---|
| `INodeIdFactory.New` | `Opc.Ua.Types/State/ISystemContext.cs` |
| `ISystemContext.NodeIdFactory` | `Opc.Ua.Types/State/ISystemContext.cs` |
| `INodeIdFactoryProvider.NodeIdFactory` | `Opc.Ua.Server/NodeManager/INodeIdFactoryProvider.cs` |

### NodeState

| Member | Role |
|---|---|
| `Create(context, nodeId, browseName, displayName, assignNodeIds)` | creates a node, optionally rebasing the subtree |
| `CreateAsPredefinedNode(context)` | create lifecycle without any assignment |
| `AssignNodeIds(context, mappingTable)` | recursive rebase; calls `New` per node |
| `OnBeforeAssignNodeIds(context)` | subclass hook fired before the pass |
| `UpdateReferenceTargets(context, mappingTable)` | rewrites references after a rebase |
| `CreateChild(context, browseName, assignInstanceNodeIds)` | materialises a child, optionally minting for it |
| `FindChild(context, browseName, createOrReplace, replacement, assignInstanceNodeIds)` | the create-if-missing path behind it |
| `SetChildValue(context, browseName, value, copy)` | creates a child on demand, so it mints indirectly |

`BaseDataVariableState` and `MethodState` carry their own
`CreateChild`/`CreateOrReplace` overloads with the same
`assignInstanceNodeIds` parameter.

### NodeInstanceExtensions

| Member | Role |
|---|---|
| `RequireNodeIdFactory(context)` | factory or diagnosable failure |
| `CreateInstance(context, node, browseName, displayName)` | creates an instance of a generated type and rebases its whole subtree |
| `AssignInstanceNodeId(context, node)` | assigns one node, returns its previous NodeId |
| `AssignInstanceChildNodeIds(context, node)` | rebases descendants |
| `AssignInstanceChildNodeIds(context, node, previousNodeId)` | as above, plus reference fixup for the root |
| `AssignInstanceChildNodeIds(context, node, previousNodeId, referenceRoot)` | as above, against a given owning subtree |
| `AssignNewChildInstanceNodeIds` (internal) | rebases newly added children |

### Generated helpers

| Helper | NodeId parameters |
|---|---|
| `Create{Type}` / `CreateInstanceOf{Type}Type` | none — always rebases through the factory |
| `Add{Child}` | `nodeId` |
| `CreateOrReplace{Child}` | `assignInstanceNodeIds` |

### NodeManagers

| Type | Behaviour |
|---|---|
| `IAsyncNodeManager.New` | every async NodeManager mints NodeIds; the interface extends `INodeIdFactory` |
| `IAsyncNodeManager.AddNode` / `AddRootNotifier` | synchronous registration the fluent surface needs |
| `CustomNodeManager2.New` | returns `node.NodeId` — mints nothing |
| `AsyncCustomNodeManager.New` | delegates to `NodeIdFactory` |
| `AsyncCustomNodeManager.NodeIdFactory` | settable; adopts the manager's namespace when unset |
| `AsyncNodeManagerAdapter.New` | delegates to the wrapped NodeManager, so `CustomNodeManager2` behaves exactly as before |
| `FluentNodeRegistration.AssignNodeId` | fluent-created nodes route to the manager's `New` |
| `FluentNodeManagerBuilderExtensions.WithNodeIdAssignment` | selects the mode inside `Configure` |

### DefaultNodeIdFactory

| Member | Role |
|---|---|
| `New` | the factory entry point |
| `CreateChildNodeId` | mints for an explicit parent, browse name and namespace |
| `CreateCanonicalPath` (static) | builds the canonical path |
| `NextCounterNodeId` | mints the next sequential identifier |
| `HasDerivablePath` | whether a node has a stable browse path |
| `WithMode` / `WithDefaultNamespaceIndex` | immutable reconfiguration |
| `GetParentNodeId` (protected virtual) | supplies the parent, for managers tracking them outside the hierarchy |

### Remaining `New` overrides

| NodeManager | Why it is not the factory |
|---|---|
| `FileSystemNodeManager` | the NodeId encodes the file path it resolves back to |
| `RoboticsNodeManager` | a build coordinator reserves identifiers across managers with ownership tracking |

## Who forces a fresh identifier

Two things can be true of a node arriving at `New` with a NodeId already
set: the caller chose that identifier, or it is a type declaration's
identifier that `NodeState.CreateInstance` copied onto an instance. The
factory cannot tell them apart, so the rule is positional rather than
inferred:

- a node that **stands on its own** keeps its NodeId;
- a node **hanging off a parent** is re-minted, because it reached `New`
  through `AssignNodeIds` walking a copied subtree.

A root that must also shed a declaration identifier says so by naming the
identifier it wants. `ISystemContext.CreateInstance` is the short way to
do that: it builds the subtree and then rebases it through
`AssignInstanceNodeId`, which forces a fresh identifier by clearing the
NodeId and asking again. That is the same path the generated
`CreateInstanceOf{Type}` helpers take, so hand-written and generated
authoring agree.

Reaching for `NodeState.Create(..., NodeId.Null, ..., assignNodeIds: true)`
on a generated state object is the trap: the object is born carrying its
own type's NodeId, `Create` only replaces that when handed an identifier,
and the result is an instance sitting on the type's node.

### Why the surrounding machinery stays

Three pieces of this look redundant once there is a single rule, and are
not.

**`AssignInstanceNodeId`'s retry.** After the forced call it retries once,
which looks like belt and braces for a deterministic factory. It is not,
because the second call is not the same as the first when the factory
holds state. A counter advances on every call, so an allocator whose next
value happens to equal the identifier being replaced - a counter sitting
at 0 replacing `i=1`, say - hands that identifier straight back. The
retry is what steps past it. `NodeInstanceExtensionsTests
.AssignInstanceNodeIdRetriesDeclarationIdCollision` pins exactly that
case.

**`assignInstanceNodeIds`**, threaded through `CreateChild`, `FindChild`,
`BaseDataVariableState`, `MethodState` and every generated
`CreateOrReplace`, means *suppress* minting. The rebase rule above is
about *forcing* it, which is the opposite direction, so one does not
subsume the other. Two callers depend on the suppression:

- `NodeState`'s copy path passes `false` for children whose identifiers
  are about to be overwritten, so the copy does not consume identifiers it
  will discard. Under `NodeIdAssignmentMode.Counter` that would burn
  counter values on nodes nobody ever sees.
- The generator emits `assignInstanceNodeIds: false` where it builds a
  *declaration* subtree, which has to keep its model NodeIds.

**The generator's `NodeId.Equals(TypeNodeIdConstant)` guard** is what
distinguishes "still on the declaration's identifier" from "the caller
chose this one". In `Add{Child}` it is close to redundant, because the
child is created immediately above it and therefore always carries the
constant. In `CreateOrReplace{Child}` it is not: the child may already
exist with a caller-assigned NodeId, and `AssignInstanceNodeId` forces
unconditionally, so without the guard that identifier would be replaced.
