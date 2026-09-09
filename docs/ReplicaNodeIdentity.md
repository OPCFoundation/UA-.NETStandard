# Replica-consistent NodeIds

OPC UA Part 4, 6.6.2.2 requires a `RedundantServerSet` to expose the same
application NodeIds on every replica. Matching a namespace URI after client-side
translation is not sufficient: the namespace index, identifier type and identifier
value must agree. Built-in server diagnostics and configuration remain local.

`Opc.Ua.Redundancy.Server` supplies `ReplicaNodeIdFactory` and
`UseReplicaNodeIdentity` for both active/passive and active/active address spaces.
The module wraps the standard `DefaultNodeIdFactory`; it does not introduce a
second hashing algorithm or a persistent logical-key-to-NodeId database.

## Configure the whole replica set

Every replica uses the same replica-set ID, ordered namespace list and assignment
mode. Its own ApplicationUri and election/gossip replica ID remain distinct.

```csharp
serverBuilder
    .UseReplicaNodeIdentity(
        "production-line",
        ["urn:example:machine-model", "urn:example:machine-instances"],
        NodeIdAssignmentMode.Numeric,
        writerAssignedIds: true)
    .UseDistributedAddressSpace(options =>
    {
        options.UseLeaderElection = true;
        options.NodeId = localReplicaId;
    });
```

Supply the shared store and record protector using the normal
[HA composition](HighAvailability.md#activepassive-address-space-consistency).
For active/active, leave `writerAssignedIds` false:

```csharp
serverBuilder
    .UseReplicaNodeIdentity(
        "production-line",
        ["urn:example:machine-model", "urn:example:machine-instances"])
    .UseActiveActiveRedundancy(options =>
    {
        options.ReplicaId = replicaId;
        options.GossipPort = 4840;
        options.Tls = mutualTlsOptions;
        options.AddPeer(peerEndpoint);
    });
```

In a real active/active configuration, set `GossipPort`, `Tls`, and peer endpoints
on `ActiveActiveRedundancyOptions`, as shown in the
[active/active example](HighAvailability.md).
Do not combine active/passive and active/active address-space modules on one server.

The configured slots are:

| Index | Meaning |
| --- | --- |
| 0 | Standard OPC UA namespace |
| 1 | This server's local ApplicationUri |
| 2 onwards | Shared namespace URIs, in the configured order |

The layout is installed before node-manager constructors run. Existing entries
are never renumbered; a conflicting slot fails startup. Declare shared model and
instance namespaces before creating nodes, including namespaces needed by their
types, references and NodeId-valued data. New shared namespaces require an explicit
layout revision, not a different runtime append order.

Validation covers namespace-bearing type definitions, data types, modelling rules,
references and role permissions, plus NodeId/ExpandedNodeId/QualifiedName values
inside arrays, matrices, DataValues and encodeable structures (including method
arguments). Structured values are visited through their normal encoding contract,
without reflection. Register the codec for an opaque ExtensionObject before using
it as shared data; an unknown body cannot be certified as namespace-safe.

Runtime manager preparation validates the future ownership composition and
hydrates retained identities into the hidden replacement before its routes are
published. After publication, the module rebinds capture and hydration to the new
manager instances. Active/passive retains the shared store and election;
active/active retains the gossip transport and CRDT map. A failed preparation
recovers bindings for the composition the lifecycle retained rather than leaving
replication attached only to retired managers.

Both server startup modules reject missing identity configuration for replicated
operation. Process-local active/passive experimentation and ordinary standalone
servers retain their existing behavior.

For non-DI hosting, assign a `ReplicaNodeIdFactory` to
`StandardServer.NodeIdFactory` before starting the server. Supply the writer
election, shared state store and record protector to its constructor when using
writer-assigned IDs. The server awaits the same early initialization, and the
address-space startup task validates the same contract.

## Creating, importing and hydrating nodes

Numeric, String, Guid and Opaque modes derive named identities through the
standard canonical-path implementation. Numeric remains the default. A shared
path must not depend on an unregistered namespace or the local ApplicationUri.
Independently created entities need a stable application key represented by their
path or an explicit NodeId. Repeated browse names are not a substitute for distinct
stable keys: supply distinct explicit identifiers when a path is ambiguous.

Counter mode and unnamed-node fallback do not provide independent identity.
With `writerAssignedIds: true`, only the elected writer may register a locally
allocated counter ID in the shared graph. Standbys consume the writer's assigned
IDs unchanged. Startup reserves live IDs and retained tombstones before writer
allocation; hydration also reserves incoming IDs. There is no synchronous
storage or network call in `New`.

Valid supplied IDs and hydrated child IDs are retained, not allocated again.
The shared registration policy checks collisions before indexing, including
collisions with explicit IDs. Hashed shared allocations keep collision detection
enabled in Release as well as Debug. Rebased factory views and supported factory
replacement preserve the policy; an unrelated custom factory cannot replace it.
Custom application identity schemes must supply their stable IDs explicitly.

Transient event instances and their generated fields use the server-local
namespace; they are not independently registered shared entities. Built-in
diagnostics/configuration and namespace-zero infrastructure are excluded from
address-space replication. WoT shared materialization retains the configured
stable factory rather than forcing independent shared counters.

## Stored contracts and peer admission

The versioned descriptor contains the replica-set ID, canonical-path version,
assignment mode, writer/independent policy and ordered namespace layout.

Active/passive stores bind it to the protected strong key
`election/addressspace-identity/v1`. Fluent registration contributes that key
even with custom hybrid routing. A new authoritative store can initialize it
atomically; competing initializers must accept the winning descriptor.
Mismatched, corrupt or unbound legacy state fails without overwriting that state.

An empty CRDT scan is not proof of an empty store. For a **verified new** hybrid
deployment, provision the contract on its fresh linearizable backend before
attaching the also-new replicated payload backend:

```csharp
var identity = new ReplicaNodeIdFactory(
    "production-line",
    ["urn:example:machine-model", "urn:example:machine-instances"]);
await identity.InitializeNewStoreAsync(newSharedRaftStore, recordProtector, ct);
```

Use the same writer policy in the provisioning factory and the hosted factory:
if the host enables writer-assigned IDs, pass its election to the provisioning
factory too. This administrative operation cannot inspect disconnected replicas
and is not a legacy-state adoption API. Never use it to certify an existing
unknown payload store. Export and validate an existing layout offline before
rebuilding or migrating; there is no automatic live renumbering or store wipe.

Active/active frames carry the descriptor over the existing gossip transport and
compare it **before CRDT merge**. An incompatible frame is rejected, does not
advance the successful-apply counter, and invalidates local identity admission.
The configured service-level wiring keeps that replica at maintenance level 0;
correct the configuration and restart it. Configure authenticated gossip as usual:
the descriptor is compatibility metadata, not a replacement for transport security.
All opted-in ownership partitions use one address-space transport per replica,
so multiple node managers do not each bind the same listening port.

Fixed identity does not make eventual values linearizable, infer missing-node
deletion from incomplete scans, or relax the existing HA coordination rules.

## Sample client proof

The [RedundantServer sample](../samples/Redundancy/RedundantServer/README.md)
reserves its shared namespace through the module and exposes factory-created
`HighAvailability/FactoryAssigned/Value` and `Target` nodes. `Target` contains
the exact NodeId of `Value`.

Run the [RedundantClient sample](../samples/Redundancy/RedundantClient/README.md)
with `--identity`, wait for its cached-ID message, then stop the serving replica.
The client verifies a different server through the standard `ServerArray`, reads
the saved IDs without rebrowsing or remapping, and creates a monitored item using
the saved value ID on the surviving replica.
