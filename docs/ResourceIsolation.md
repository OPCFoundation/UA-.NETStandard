# Server resource isolation

Resource isolation coordinates admission across the listeners and service
pipeline of one server. It complements message limits and connection rate
limits; it is not a total-process-memory or network-DDoS guarantee.

`StandardServer.ResourceIsolationOptions` selects the policy. Hosted servers
use `OpcUaServerOptions.ResourceIsolation`, the fluent configuration below, or
an explicitly registered provider. Directly supplied providers and classifiers
remain host-owned.

## Profiles

| Mode | Behavior |
| --- | --- |
| `SharedOnly` | Compatibility behavior: existing rate, message and shared reassembly limits, without the new runtime fairness/reservation policy. |
| `FairShare` | Bounded owner accounting and weighted decoded-request scheduling, without protected floors. |
| `Balanced` (default) | FairShare plus separate non-borrowable bootstrap, reconnect and applicable control-work floors inside existing capacities. |
| `TrustedReservations` | Balanced plus individually provisioned trusted-owner floors and weights. Requires an explicit classifier and owner configuration; missing provisioning fails startup. |

Reassembly admission is always fail-fast. It never waits while holding a channel
gate or pauses the ordered receive loop to wait for another owner. Fair waiting
is confined to bounded decoded-request queues.

Unused **shared** capacity is work-conserving: one owner can consume it unless
an explicit hard ceiling limits that owner. Protected floors are not shared
capacity and are never lent away. No already-admitted lease is revoked.
Consequently a new ordinary caller can still be refused while shared memory is
held by earlier work; FairShare is not an unconditional admission guarantee.

## Configuration

```csharp
services.AddOpcUa()
    .AddServer(options => options.ApplicationName = "MyServer")
    .ConfigureResourceIsolation(options =>
    {
        options.Mode = ServerResourceIsolationMode.Balanced;
        options.MaxTrackedOwners = 4096;
        options.HandshakeTimeout = TimeSpan.FromSeconds(30);
        options.Stages =
        [
            new ResourceIsolationStageOptions
            {
                Stage = ResourceIsolationStage.RequestExecution,
                OwnerHardLimit = 8
            },
            new ResourceIsolationStageOptions
            {
                Stage = ResourceIsolationStage.Connection,
                OwnerHardLimit = 16
            }
        ];
    });
```

Set `ResourceIsolationOptions` and, if needed, `ResourceIsolationClassifier`
before starting a directly constructed server. An
`IServerResourceIsolationProvider` may instead be assigned through
`ServerBase.ResourceIsolationProvider` or registered in Dependency Injection.
A custom provider must honor the physical queue and transport capacity
envelopes of its consumers; reporting larger admission capacity does not
increase those consumers' limits.

The hosting configuration section is `OpcUa:Server:ResourceIsolation`.
Options are parsed explicitly rather than requiring reflection-based binding:

```json
{
  "OpcUa": {
    "Server": {
      "ResourceIsolation": {
        "Mode": "Balanced",
        "MaxTrackedOwners": 4096,
        "HandshakeTimeout": "00:00:30",
        "Stages": [
          { "Stage": "RequestExecution", "OwnerHardLimit": 8 }
        ]
      }
    }
  }
}
```

Invalid capacities, duplicate stage entries, invalid modes, floor sums,
overflow, or insufficient room for a supported message are rejected explicitly.
Stage `Capacity` overrides can lower but not increase the existing envelope.
Zero reserved capacity explicitly disables that stage's corresponding floor;
it must not be interpreted as a remaining guarantee.

## Accounting and lifetime

| Stage | Charged lifetime |
| --- | --- |
| Connection | Physical connection, including reverse-connect handoff until actual closure |
| Handshake | Connection admission through successful protocol startup, and bounded OpenSecureChannel renewal work; startup has an independent fixed deadline |
| ReassemblyBytes | Actual backing-array bytes retained for intermediate chunks, through completion, abort, replacement, quota error or closure |
| SessionEstablishment | Concurrent CreateSession/ActivateSession processing |
| RequestQueue | Decoded requests awaiting dispatch |
| RequestExecution | Running handlers; released when a supported Publish request parks |
| RequestQueueBytes | Conservative encoded-message admission cost retained through queued/executing/parked processing |
| ParkedRequest | Potentially parked work retained until actual completion |

Leases are idempotent and released on failures, cancellation, rejected
downstream admission, and shutdown. Handler code that ignores cancellation
cannot be forcibly preempted; its resource accounting stays live until the
handler finishes.

The default request-cost charge is `MaxMessageSize` per retained decoded
request, not a measurement of CLR heap size. The aggregate cost envelope derives
from configured queued, executing and parked slot limits. Existing decoder,
operation and body-size limits still apply.

Owner tracking has a hard bound; inactive entries disappear when their last
lease is returned. Active entries cannot be evicted to admit new keys. Some
table capacity is kept available for configured protected classes and trusted
owners. Repeated rejected identities cannot create an unlimited idle cache.

## Reserve sizing

Bootstrap and reconnect floors default to one operation at slot-based stages.
Reassembly floors use a conservative maximum retained-message footprint derived
from the default pool and negotiated chunk bounds. Configure
`MaxRetainedMessageBytes` explicitly for custom buffer managers whose array
length bounds differ. A custom bound is a deployment obligation, not a
measurement of typical traffic.

All floors fit **inside** existing totals. Shared space must still accommodate
one allowed operation/message. The configured Session/SecureChannel capacities
must support the protocol's N+1 relationship and the effective admission
partitions; impossible configurations fail rather than silently increasing
totals or advertising unavailable capacity.

For a 4 MiB message limit, 65,535-byte buffer limit and 64 MiB reference
reassembly budget, the default conservative footprint is 16.25 MiB.
Bootstrap and reconnect each reserve 16.25 MiB, leaving 31.5 MiB shared.
This is not the payload size: it allows for retained arrays and pool overhead.
`CreateRuntimePlan` exposes the effective immutable stage plan for inspection.
The explicit-bound `CreatePlan` calculator remains available for sizing but
does not by itself install enforcement.

With runtime isolation enabled, its byte-class partitions replace the legacy
sessionless occupancy priority, while the shared reassembly budget still
counts actual bytes and enforces its aggregate ceiling. `SharedOnly` preserves
the legacy priority behavior. An independently shared custom global ceiling
or external rate limiter remains an additional rejection condition.

## Trust, anonymous clients and NAT

Ordinary pre-authentication traffic is grouped by observed address, normalized
for IPv4-mapped IPv6. This is an abuse-control key, not identity. A validated
application certificate and a verified live Session continuity key allow
stronger classification later. Certificates supplied under SecurityPolicy None
do not confer trusted classification.

`IResourceIsolationClassifier` is the explicit deployment trust seam. Its
ingress mapping is the only way to designate protected traffic before protocol
authentication. It must use a trusted network/proxy/transport boundary, not
recent successful IP history or caller-supplied headers.

For `TrustedReservations`, configure bounded `TrustedOwners` with keys, weights
and optional `TrustedResourceReservation` overrides. The classifier must return
the matching provisioned key only from verified evidence. An unprovisioned
trusted key is an error, not a fallback that silently bypasses protection.

Multiple authenticated applications behind one NAT can acquire distinct
identities after authentication. Anonymous callers sharing an address cannot
always be distinguished. An activated anonymous Session is not automatically
a trusted tenant. HTTPS logical channels may represent multiple clients; they
must not be treated as one permanent tenant.

Decoded-request classification uses `ISessionBindingProvider` and is revalidated
after queueing. Stale, transferred, expired or unknown tokens gain no protected
privileges. Normal service authentication and authorization still execute.
User keys, tokens and raw addresses must not become diagnostic metric labels.

## Transport behavior and limitations

Raw TCP, Kestrel TCP and UACP WebSockets share the server policy. Admission
occurs before channel/crypto allocation, and isolation refusal does not consume
a subsequent legacy rate-limit token. HTTPS uses physical admission before TLS
and bounded common request-body processing; HTTP streams/requests are not
counted as separate physical UASC connections.

With the default rate provider, Balanced and TrustedReservations also partition
the existing connection-rate and burst totals: one token and one token/second
for bootstrap, reconnect, and each provisioned trusted owner. Shared traffic
cannot consume those tokens; protected traffic can also use available shared
tokens. Connection closure does not refund a consumed rate token. Totals too
small for these floors plus one shared token fail configuration explicitly.
FairShare and SharedOnly retain the ordinary token bucket. Explicit custom rate
providers are respected and may impose additional rejection conditions.

Startup deadlines use shared scheduling, not one timer per socket. Initial
timeouts are configurable up to two minutes by default. Reverse-connect
handoff preserves the connection lease and distinguishes established protocol
startup from a paused reverse connection. A configured client accepting a valid
ReverseHello completes startup admission so it may save the connection for
later use as the specification permits; physical connection capacity stays
charged until actual closure.

At capacity the raw listener reclaims genuinely unused established sessionless
channels, not an active handshake, partial message, queued write, or in-flight
decoded request. Rejection by an owner limit or rate limiter does not evict
another channel merely to discover that the new caller is inadmissible.

Use transport ERR `BadTcpNotEnoughResources` for retained-resource rejection and
service-level `BadServerTooBusy` for decoded admission pressure. HTTP/JSON and
WebSocket adapters keep their profile-specific errors; size-related close 1009
does not stand for arbitrary quota pressure. Existing oldest-unused,
sessionless SecureChannel reclamation and Session transfer semantics remain.

The provider exposes current stage usage, low-cardinality usage/owner gauges, and a bounded
`opcua.server.isolation.rejections` counter tagged by stage and reason, never
by owner identity. Existing telemetry and generated logging carry failures.
Subscription/MonitoredItem storage, PubSub, custom REST contributor body
buffering, and unrelated application allocations have their own bounds; this
policy does not claim to account every allocation.

Network/link/SYN/TLS saturation needs upstream protection. Strict
pre-authentication availability needs protected ingress; unknown anonymous
clients are best effort during a distributed flood. Sharing an injected
provider within a process is supported, but this is not a fleet-wide quota
store or hard-real-time scheduler.
