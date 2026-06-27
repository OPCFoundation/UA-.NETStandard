# High Availability and OPC UA Redundancy

This guide maps the OPC UA .NET Standard high-availability APIs to OPC 10000-4 §6.6 Redundancy. It documents the implemented server, client, subscription, session, Kubernetes, and active/active extension seams; the worked examples are `Applications/HighAvailabilityServer` and `Applications/RedundantClient`.

## 6.6.1 Redundancy overview

OPC UA defines three independent but composable redundancy dimensions: **server redundancy** gives clients multiple servers that expose the same data, **client redundancy** lets backup clients take over work from an active client, and **network redundancy** gives a client and server more than one communication path. The stack implements standardized discovery and failover metadata in the core server/client packages, and adds opt-in distributed state through `OPCFoundation.NetStandard.Opc.Ua.Server.Distributed`.

Server redundancy is either **transparent** or **non-transparent**. In transparent redundancy, the redundant set looks like one server and failover is hidden from the client. In non-transparent redundancy, each server has its own identity and endpoint, and the client reads `Server.ServerRedundancy` plus `Server.ServiceLevel` to decide what to do.

## 6.6.2 Server redundancy

`Opc.Ua.RedundancySupport` is the stack enum published by `Server.ServerRedundancy.RedundancySupport` and consumed by the client. The modes are:

| Mode | OPC UA meaning | Stack behavior |
| --- | --- | --- |
| `None` | No server redundancy is advertised. | Single-server default. |
| `Cold` | Only one server is active at a time; backups may be unavailable or not running. | Client connects to one server and creates a fresh session/subscriptions on failover. |
| `Warm` | Backup servers are running but have less functionality or no process data. | Client can connect to peers, keep subscriptions created but sampling/publishing inactive on backups, and promote the highest-`ServiceLevel` server. |
| `Hot` | Multiple servers are running and can independently provide data. | Client connects to peers and either hands reporting over to one active server or merges reporting streams. |
| `HotAndMirrored` | Hot servers mirror communication state. | `RedundantManagedClient` keeps one active session, may keep lightweight ServiceLevel status-check sessions to backups, and fails over by opening a channel and re-activating the mirrored session with the existing `AuthenticationToken`; subscriptions are not recreated client-side. |
| `Transparent` | One virtual server identity hides physical failover. | Server publishes `CurrentServerId`; deployment infrastructure supplies the virtual address and shared certificate/endpoint identity. |

### Server.ServerRedundancy model

Register `AddServerRedundancy(...)` on the server builder to populate the live `Server.ServerRedundancy` nodes after the server starts. `ServerRedundancyOptions.Mode` writes `RedundancySupport`. `PeerServerUris` and `RedundantPeers` populate `RedundantServerArray`; for non-transparent modes the startup task also populates `ServerUriArray`, and for transparent mode it publishes `CurrentServerId`.

For non-transparent redundancy, set `RedundantPeers` when clients should resolve peers through `FindServers`; `ConfiguredRedundantServerSetProvider` exposes those `ApplicationDescription` entries server-side. `AdvertiseNtrsCapability` defaults to `true`, so non-transparent modes add the `NTRS` discovery capability for GDS/NTRS registration.

All servers in a `RedundantServerSet` must have identical application AddressSpaces: identical NodeIds, browse paths, AddressSpace structure, and `ServiceLevel` algorithm. Only local server diagnostics may differ. `UseDistributedAddressSpace(...)` helps satisfy this by mirroring node topology and values through `INodeStateStore`/`ISharedKeyValueStore`, but application-specific method handlers and callbacks still need to be attached by each node manager.

```csharp
services.AddOpcUa()
    .AddServer(server =>
    {
        // normal endpoint, security, and application configuration
    })
    .AddNodeManager<MyNodeManagerFactory>()
    .UseDistributedAddressSpace(options =>
    {
        options.UseLeaderElection = true;
        options.NodeId = Environment.MachineName;
    })
    .AddServerRedundancy(options =>
    {
        options.Mode = RedundancySupport.Warm;
        options.CurrentServerId = Environment.MachineName;
        options.AddRedundantPeer(
            "urn:example:ha-server-2",
            new ArrayOf<string>(["opc.tcp://ha-server-2:4840"]));
        options.PeerServiceLevel = ServiceLevels.DegradedMaximum;
    })
    .AddManualFailover(options =>
    {
        options.ServiceLevelSelector = state => state == ServerState.Running
            ? ServiceLevels.Maximum
            : ServiceLevels.Maintenance;
    });
```

## 6.6.2.4.2 ServiceLevel and 6.6.2.4.3 load balancing

OPC 10000-4 Table 105 defines `ServiceLevel` as a byte split into mandatory sub-ranges. The stack exposes these constants and helpers in `ServiceLevels` and `ServiceLevelSubrange`.

| Range | Name | Client interpretation |
| --- | --- | --- |
| `0` | Maintenance | New clients should not connect; connected clients should disconnect and use `EstimatedReturnTime` before retrying. |
| `1` | NoData | Server is not operational for process data; clients may connect only for status/diagnostics. |
| `2..199` | Degraded | Server is partially operational; clients should prefer any Healthy peer and may choose a higher degraded peer when no Healthy peer exists. |
| `200..255` | Healthy | Server is fully operational; clients connect to the highest value for selection/load balancing and should not fail over while the current server remains Healthy. |

`ConstantServiceLevelProvider` reports a fixed value, defaulting to `255`, preserving single-instance behavior. `LeaderServiceLevelProvider` follows an `ILeaderElection`: the leader reports `255`, Cold standbys report `1`, Warm standbys report `199`, and Hot/HotAndMirrored standbys report `255` unless explicit levels are supplied. Optional health and connected-client delegates cap or decrement Healthy values so Hot servers can load-balance within the 200-255 range. `IServiceLevelController` lets `RequestServerStateChange` override the published value for manual maintenance.

Client-side, `DefaultServerRedundancyHandler.FetchRedundancyInfoAsync` reads `RedundancySupport`, `ServiceLevel`, `EstimatedReturnTime`, `RedundantServerArray`, `ServerUriArray`, and `CurrentServerId` as applicable. `ServerRedundancyInfo.ServiceLevelSubrange` is calculated with `ServiceLevels.GetSubrange`.

```csharp
ManagedSession session = await new ManagedSessionBuilder(configuration, telemetry)
    .UseEndpoint(endpoint)
    .WithServerRedundancy(new DefaultServerRedundancyHandler())
    .ConnectAsync(ct);

var handler = new DefaultServerRedundancyHandler();
ServerRedundancyInfo info = await handler.FetchRedundancyInfoAsync(session, ct);
Console.WriteLine($"Mode={info.Mode}, ServiceLevel={info.ServiceLevel} ({info.ServiceLevelSubrange})");

ServerFailoverDecision decision = handler.ShouldFailover(info, session.ConfiguredEndpoint);
if (decision.IsFailoverWarranted)
{
    ConfiguredEndpoint? target = handler.SelectFailoverTarget(info, session.ConfiguredEndpoint);
    Console.WriteLine($"Fail over to {target?.EndpointUrl}: {decision.Reason}");
}
```

## 6.6.2.4.5 Non-transparent failover modes and client actions

`RedundantManagedClient` implements the Table 107 client patterns over `ManagedSession` instances discovered from `RedundantServerArray`/`ServerUriArray` and resolved with `IRedundantServerEndpointResolver`. The default resolver calls `FindServers` and `GetEndpoints` from the current endpoint's discovery URLs, chooses matching security policy/mode and URL scheme when possible, and caches the result.

| Mode | Table 107 actions | `RedundantManagedClient` realization |
| --- | --- | --- |
| Cold | Initial connection to one active server. At failover open a SecureChannel, create/activate a session, create subscriptions/monitored items, activate sampling, then activate publishing. | Starts with the initial endpoint only. `FailoverAsync` selects a peer, connects it, applies subscriptions only to the current session, and enables reporting/publishing. |
| Warm | Connect to more than one server and create subscriptions/monitored items on backups; sampling and publishing become active at failover. | Connects all peers, stores templates with `AddSubscriptionAsync`, creates subscriptions on each connected server, and keeps only the selected server in Reporting/publishing while backups are Sampling/publishing disabled. |
| Hot (a) | Connect to more than one server, create subscriptions everywhere, activate sampling everywhere, publish from one server; at failover activate publishing on the next server. | Default `HotRedundancyNotificationMode.ReportingHandoff`: all connected peers host subscriptions; current server Reports and publishes, backups Sample only. |
| Hot (b) | Connect to more than one server, create subscriptions everywhere, activate sampling and publishing everywhere; client handles duplicate streams. | `HotRedundancyNotificationMode.ReportingMerge`: all connected peers Report and publish; duplicate suppression/merge is a client responsibility above the event callback. |
| HotAndMirrored | Client normally connects to one server; optional status sessions only; on failover open a SecureChannel and call `ActivateSession` against the mirrored session. | Keeps a single active session and, when `EnableHotAndMirroredStatusChecks` is set, periodically reads ServiceLevel from backup status sessions. `FailoverAsync` reuses the mirrored `AuthenticationToken` and single-use nonce by calling `ActivateSession` on the backup; it does not recreate subscriptions because the server mirrors session and subscription state. |

`RedundantManagedClient.RefreshServiceLevelsAsync` reads service levels from connected sessions and selects the highest value. The default handler will not fail over away from a server that is still in the Healthy sub-range, defers retries for Maintenance using `EstimatedReturnTime` or a longer default backoff, and selects a Running peer with an operational `ServiceLevel`.

```csharp
RedundantManagedClient coldClient = await new ManagedSessionBuilder(configuration, telemetry)
    .UseEndpoint(initialEndpoint)
    .ConnectRedundantAsync(ct: ct);

await coldClient.AddSubscriptionAsync("process", subscriptionTemplate, ct);
await coldClient.FailoverAsync(ct);
```

```csharp
RedundantManagedClient warmClient = await new ManagedSessionBuilder(configuration, telemetry)
    .UseEndpoint(initialEndpoint)
    .ConnectRedundantAsync(ct: ct);

await warmClient.AddSubscriptionAsync("process", subscriptionTemplate, ct);
await warmClient.RefreshServiceLevelsAsync(ct);
```

```csharp
RedundantManagedClient hotHandoffClient = await new ManagedSessionBuilder(configuration, telemetry)
    .UseEndpoint(initialEndpoint)
    .ConnectRedundantAsync(
        new RedundantManagedClientOptions
        {
            HotNotificationMode = HotRedundancyNotificationMode.ReportingHandoff
        },
        ct);
```

```csharp
RedundantManagedClient hotMergeClient = await new ManagedSessionBuilder(configuration, telemetry)
    .UseEndpoint(initialEndpoint)
    .ConnectRedundantAsync(
        new RedundantManagedClientOptions
        {
            HotNotificationMode = HotRedundancyNotificationMode.ReportingMerge
        },
        ct);

hotMergeClient.NotificationReceived += (sender, args) =>
{
    // Merge or de-duplicate streams by application key, source timestamp, EventId, or sequence context.
};
```

```csharp
RedundantManagedClient mirroredClient = await new ManagedSessionBuilder(configuration, telemetry)
    .UseEndpoint(initialEndpoint)
    .ConnectRedundantAsync(
        new RedundantManagedClientOptions
        {
            EnableHotAndMirroredStatusChecks = true,
            HotAndMirroredStatusCheckInterval = TimeSpan.FromSeconds(5)
        },
        ct);
```

## 6.6.5 Manual failover and Maintenance

OPC 10000-4 §6.6.5 allows a server to be taken out of the set by shutdown or by moving `ServiceLevel` to Maintenance, either through a vendor tool or `Server.RequestServerStateChange`. `AddManualFailover(...)` wires the standard method and validates administrative access through `IConfigurationNodeManager.HasApplicationSecureAdminAccess` unless `RequestServerStateChangeOptions.AdminAccessValidator` is supplied.

The startup task updates `Server.ServiceLevel`, `Server.ServerStatus.State`, `Server.ServerStatus.SecondsTillShutdown`, `Server.ServerStatus.ShutdownReason`, and `Server.EstimatedReturnTime`. The current implementation publishes the requested maintenance/no-data state so clients back off; it does **not** install a transport-level hook that rejects newly created sessions.

Non-transparent servers registered with `AddServerRedundancy(...)` advertise the `NTRS` capability by default. In Kubernetes deployments, register the non-transparent set or transparent virtual address with GDS/NTRS according to your discovery model.

## HotAndMirrored and Transparent state mirroring

OPC 10000-4 requires HotAndMirrored and Transparent sets to synchronize enough state that a client can continue after failover. The spec names sessions, subscriptions, registered nodes, continuation points, sequence numbers, sent notifications, and synchronized EventIds.

Implemented server seams:

- `UseDistributedSessions(...)` installs `DistributedSessionManager` and `ISharedSessionStore`. Session records include the session id, authentication token, nonces, client certificate chain, security policy/mode, endpoint URL, session timeout, client description, and user identity material. `EnableFastReconnect` defaults to `false`; when enabled, a failover reconnect still performs full `ActivateSession` signature validation against the mirrored server nonce.
- `ISingleUseNonceRegistry` and `SharedSingleUseNonceRegistry` enforce the security boundary: the mirrored `serverNonce` is consumed exactly once across the replica set, and the authentication token is only a lookup key.
- `UseDistributedSubscriptionMirroring(...)` registers `SharedKeyValueSubscriptionStore` as `ISubscriptionStore`. It mirrors subscription definitions and monitored-item definitions: publishing interval, lifetime, keepalive, priority, node id, attribute id, monitoring mode, sampling interval, queue size, filters, discard policy, and related metadata.
- The same store also implements `ISubscriptionRetransmissionStore`. Retransmission state is mirrored asynchronously through a non-blocking background drain: `NextSequenceNumber`, sent `NotificationMessage` entries, acknowledgements, and deletes are coalesced and persisted so `Republish` can continue after failover without blocking the publishing path.
- `IContinuationPointStore` mirrors best-effort `ContinuationPointEnvelope` records for Browse and HistoryRead continuation points. The envelope contains the owner session, continuation point id, kind, and re-issuable request metadata.
- `IEventIdProvider` is an optional event-publishing seam. `DeterministicEventIdProvider` derives EventIds from a shared replica-set seed and stable event fields so Transparent/HotAndMirrored replicas can publish the same logical EventId and clients do not double-process events; the default remains the existing random GUID EventId behavior.
- `RegisterNodes` returns the input NodeIds in this stack, so registered-node handles are already replica-consistent when the AddressSpace NodeIds are identical.
- `UseDistributedAddressSpace(...)` mirrors node topology and values through `INodeStateStore`, helping each server present the same NodeIds and browse paths.

Documented limitations:

- The synchronous core `ISubscriptionStore` definition-persistence contract requires a synchronously-completing backend such as the in-memory or CRDT store. Full async persistence to a backend such as Redis would require an async `ISubscriptionStore`.
- `SharedKeyValueSubscriptionStore` restores definitions and retransmission state, but monitored-item data/event queues are not restored by `RestoreDataChangeMonitoredItemQueue` or `RestoreEventMonitoredItemQueue`.
- Continuation-point mirroring is best-effort. Built-in node-manager `ContinuationPoint.Data` is opaque and is not reconstructed on a backup; after failover a client may receive `BadContinuationPointInvalid` and re-issue Browse or HistoryRead, which OPC 10000-4 §6.6.2.2 permits. Node managers that can serialize their own continuation-point data may opt in through `IContinuationPointStore`.
- Deterministic EventIds are optional and only as stable as the event fields used. Alarms & Conditions clients should still call `ConditionRefresh` after failover as required by OPC UA.

## 6.6.3 Client redundancy

OPC UA client redundancy is implemented with `TransferSubscriptions` plus server diagnostics. `ClientFailoverCoordinator` helps a backup client find the active client's session by `ActiveSessionId` or `ActiveSessionName`, discover subscription ids from diagnostics, verify the backup uses the same user display name when configured, and call `TransferSubscriptionsAsync` with `SendInitialValues` defaulting to `true`.

```csharp
var coordinator = new ClientFailoverCoordinator();
ArrayOf<TransferResult> results = await coordinator.TransferActiveSubscriptionsAsync(
    backupSession,
    new ClientRedundancyTransferOptions
    {
        ActiveSessionName = "LineA-PrimaryClient",
        ActiveUserDisplayName = backupSession.Identity.DisplayName,
        SendInitialValues = true
    },
    ct);
```

OPC UA does not standardize how active and backup clients exchange `SessionId` or subscription ids. The coordinator uses `ServerDiagnostics` when authorized; deployments that disable diagnostics should provide their own coordination channel.

## 6.6.4 Network redundancy

Transparent network redundancy is handled below OPC UA by routers, virtual adapters, load balancers, or Kubernetes Services. Non-transparent network redundancy uses multiple endpoints for the same logical server; the client selects another endpoint and recreates only the SecureChannel while reusing the session and subscriptions.

`NetworkRedundancyOptions.AlternateEndpoints` and `NetworkRedundancyEndpointSelector` model non-transparent endpoint alternates. `ManagedSession` accepts these options and cycles to the next endpoint for the same logical server when reconnecting.

```csharp
ManagedSession session = await new ManagedSessionBuilder(configuration, telemetry)
    .UseEndpoint(primaryEndpoint)
    .WithNetworkRedundancy(new ArrayOf<ConfiguredEndpoint>([secondaryEndpoint, tertiaryEndpoint]))
    .ConnectAsync(ct);
```

## Beyond §6.6: distributed extensions

The base package uses `ISharedKeyValueStore` as the common seam for address-space, session, subscription, retransmission, nonce, and lease records. The in-memory implementation is for tests and single-process samples; multi-pod production deployments need a networked, authenticated, encrypted, and capacity-bounded backend. `IRecordProtector` protects serialized records before they reach the store.

`OPCFoundation.NetStandard.Opc.Ua.Server.Distributed.Crdt` is explicitly beyond OPC 10000-4 §6.6. It provides active/active multi-writer address-space replication with CRDTs and gossip (`UseReplicatedAddressSpace`) and CRDT-backed session metadata (`UseReplicatedSessions`). CRDT state is eventually consistent and cannot provide compare-and-swap; keep the single-use nonce registry and other exactly-once decisions on a strongly consistent store.

```csharp
services.AddOpcUa()
    .AddServer(server => { })
    .AddNodeManager<MyNodeManagerFactory>()
    .UseReplicatedAddressSpace(options =>
    {
        options.ReplicaId = Crdt.ReplicaId.New();
        options.UseTcpGossip(IPAddress.Any, port: 4840);
        options.AddPeer(peerEndpoint);
    })
    .UseReplicatedSessions();
```

## Kubernetes deployment

Use the consolidated [Kubernetes High Availability Deployment](HighAvailabilityKubernetes.md) guide for the `Opc.Ua.Server.Distributed.Kubernetes` package. It covers Kubernetes Lease election, EndpointSlice peer discovery, ServiceLevel-driven readiness, StatefulSet/Deployment and Service manifests, RBAC, probes, time synchronization, secrets, and GDS/NTRS registration.

## Samples

`Applications/HighAvailabilityServer` demonstrates the server-side distributed and redundancy registrations. `Applications/RedundantClient` demonstrates reading server redundancy metadata, comparing requested client failover behavior with the server's reported `RedundancySupport`, and running the client failover modes from the command line.
