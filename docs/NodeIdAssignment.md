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
- [Who forces a fresh identifier](#who-forces-a-fresh-identifier)

## The contract

`INodeIdFactory` (`Opc.Ua.Types/State/ISystemContext.cs`) has one member:

```csharp
NodeId New(ISystemContext context, NodeState node);
```

It reaches node code through `ISystemContext.NodeIdFactory`. A NodeManager
publishes itself as that factory in its constructor
(`SystemContext.NodeIdFactory = this`), so any node-authoring code holding
the manager's `SystemContext` mints identifiers through the manager. This
points at the *NodeManager*, not at its factory, so that a subclass
overriding `New` is still the one node-level code reaches — which is why
`AsyncCustomNodeManager` and `CustomNodeManager2` do it once for every
manager that derives from them, and a subclass never repeats it.

`IRebasableNodeIdFactory` (`Opc.Ua.Server/NodeManager/IRebasableNodeIdFactory.cs`)
extends that with what a NodeManager needs and a bare `INodeIdFactory`
cannot express: which namespace to mint into, which identifier style to
mint, and an identifier for a node that does not exist yet.

```csharp
NodeIdAssignmentMode Mode { get; }
ushort DefaultNamespaceIndex { get; }
IRebasableNodeIdFactory WithDefaultNamespaceIndex(ushort defaultNamespaceIndex);
IRebasableNodeIdFactory WithMode(NodeIdAssignmentMode mode);
NodeId NextCounterNodeId();
NodeId CreateChildNodeId(NodeId parentNodeId, QualifiedName browseName, ushort namespaceIndex, NamespaceTable namespaceUris);
```

The namespace is why this is a contract rather than constructor
configuration: a namespace belongs to the NodeManager, not to the node, and
a manager learns its own namespace index only after the server's namespace
table has been extended — which is later than a factory registered in
dependency injection was built. Implementations are therefore immutable and
the `With…` methods return a view, so one registered instance serves
managers that own different namespaces.

`AsyncCustomNodeManager.NodeIdFactory` is typed as this interface, so a
caller can put its own rule in front of `DefaultNodeIdFactory` by
decorating it — claim one subtree, delegate the rest — instead of
overriding `New` on the NodeManager and scattering the identifier rule
across it.

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

The path is scratch. In every mode but `String` it is hashed and thrown
away, so it is built into a stack buffer — or, when it does not fit, one
rented from `ArrayPool<char>` — and never becomes a string at all. The
length is measured and written by one routine, so the length reserved for
a segment cannot drift from the length written into it.

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

`Numeric` is the default: it is the most compact form on the wire and the
most readable in a client UI.

`String` is the only mode that cannot collide for distinct browse paths,
because it keeps the canonical path whole and two different paths are
never the same text. The others project the path through SHA-256 and
inherit that hash's collision probability — negligible for `Guid` and
`Opaque` at 128 bits, but real for `Numeric` at 32.

### Collision detection

A 32 bit identifier is a birthday problem: distinct browse paths are
expected to land on one after roughly 2^16 of them. Left alone that is
silent damage, because the predefined-node index takes the last writer —
one node would simply replace the other and the address space would be
quietly wrong.

So the factory records what it mints and raises
`BadConfigurationError` when a second browse path lands on an identifier
that a different one already has. The error names the path that was
refused and the modes that do not have the problem.

Minting the same path twice is normal — `AssignNodeIds` walks a subtree
on every create pass — so the record keeps a witness of the path
alongside the identifier, and only a *differing* witness is a collision.
The witness is the tail of the same hash, so two paths would have to
agree on the identifier and on a further 64 bits before a real collision
could pass as a re-mint.

Three consequences worth knowing:

- The record is scoped to the factory instance, which is scoped to a
  namespace, because identifiers in different namespaces cannot collide.
  NodeManagers sharing a namespace share the instance and are checked
  against each other.
- It is never pruned. An identifier handed to a client stays spoken for
  even after the node goes away, so re-minting it for a different path is
  exactly the collision this catches. It costs roughly 50 bytes per
  distinct path minted.
- It covers what the factory mints, not identifiers a caller assigned
  itself. `String` and `Counter` cannot collide, so they keep no record
  and pay nothing.

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

`New` keeps a node's current NodeId when the caller can plausibly have
chosen it, and mints a fresh one otherwise. Two cases qualify:

- the identifier is **already in this NodeManager's namespace**, which no
  other model's NodeIds ever are;
- the node **stands on its own** rather than hanging off a parent.
  `NodeState.Create` hands a root its NodeId before running the assignment
  pass, so a caller naming a node explicitly arrives this way.

Everything else is re-minted, because it reached `New` through
`AssignNodeIds` walking a subtree copied from a type declaration and still
carrying that declaration's identifiers. See
[Who forces a fresh identifier](#who-forces-a-fresh-identifier).

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

The default is `NodeIdAssignmentMode.Numeric`, so a manager that overrides
nothing gets compact deterministic browse-path identifiers, checked for
collisions.

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

Every builder mints through the owning NodeManager's `New`, so there is no
second identifier shape to fall back to. `WithNodeIdAssignment` needs the
factory itself, which only an `AsyncCustomNodeManager` carries, so on any
other NodeManager it raises `BadConfigurationError` rather than quietly
doing nothing.

## Configuring the factory

Through dependency injection, once for the whole server:

```csharp
builder.AddNodeIdFactory(NodeIdAssignmentMode.Guid);
```

`DependencyInjectionStandardServer` resolves the registered
`IRebasableNodeIdFactory`, `StandardServer` threads it into
`ServerInternalData`, and every `AsyncCustomNodeManager` picks it up
through `INodeIdFactoryProvider`, rebased onto its own namespace.

The registration is keyed on the interface, so an implementation of your
own — typically a decorator holding a `DefaultNodeIdFactory` — can be
registered in its place:

```csharp
builder.AddNodeIdFactory(new ReservingNodeIdFactory(new DefaultNodeIdFactory()));
```

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
