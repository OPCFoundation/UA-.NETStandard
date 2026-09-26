# Server resource isolation

Resource isolation limits how many connections and requests one server accepts,
how much incomplete-message data it retains, and how it shares request execution
between callers. The listeners and request dispatcher enforce the same policy.
It complements message-size and connection-rate limits; it does not cap the
whole process heap or protect against network saturation.

`StandardServer.ResourceIsolationOptions` selects the policy. Hosted servers
use `OpcUaServerOptions.ResourceIsolation` or the fluent configuration below.
**Balanced is the default**, for both direct and hosted `StandardServer`
instances. A host can instead supply an `IServerResourceIsolationProvider`.

## Terms

These terms describe resource accounting, not permission to access OPC UA data.

| Term | Meaning here |
| --- | --- |
| Admission | Checking available capacity before accepting a connection, retaining data, or starting work. Refusal means that check failed. |
| Classifier / provider | The host's classifier maps verified evidence to a caller group. The provider applies resource limits to that group. Neither replaces service authorization. |
| Physical connection checks | Listener checks for an actual network connection, before allocating protocol-channel state. HTTP requests on that connection are not additional physical connections. |
| Isolation refusal | A rejection by the resource-isolation provider, for example because capacity or a per-caller limit is exhausted. A rate limiter or normal service authorization can reject work independently. |
| Owner | The accounting group charged for work: an observed address, a verified application or Session identity, or an explicitly configured group. It is not necessarily one person or one client process. |
| Stage | One separately counted resource, such as connections, queued requests, or retained bytes. The API names are listed under [Accounting and lifetime](#accounting-and-lifetime). |
| Floor / reservation | Capacity kept for a particular class of work or configured owner, inside the existing total. Other classes cannot borrow it, even when it is unused. |
| Shared capacity / work-conserving | Capacity available to all classes. If only one owner has work, it can use otherwise idle shared capacity up to its hard limit; the server does not divide it into fixed equal shares. Reserved capacity is excluded. |
| Lease | The provider's disposable record of capacity in use. Returning it releases the charge; it does not mean a time-limited Session or credential. |
| Bootstrap / reconnect | Protected initial startup / recovery of a connection or Session. These classes require trusted ingress classification or, where available, verified live Session continuity; a request's name alone is not evidence. |
| Control work | Recovery or maintenance requests, classified using a verified Session or an explicit trusted mapping rather than the service name alone. |
| Weighted fairness | Selecting queued requests in turns between eligible owners, with more turns for a higher configured weight. It is not a CPU-time share or a response-time guarantee. |
| Allocation cost | A conservative charge used before retaining a decoded request or HTTP body, not a measurement of actual managed-heap allocation. |
| Protected ingress | A network entry path whose access restrictions are enforced by the deployment, so a classifier can identify protected traffic before OPC UA authentication. An IP address alone does not establish identity. |
| UASC | UA Secure Conversation, the OPC UA secure-channel protocol. Its incomplete multi-chunk messages retain arrays until completion or cleanup. |

## Profiles

### SharedOnly

`SharedOnly` is a **current supported mode**, not a deprecated feature or a
temporary migration mode. With no explicitly supplied isolation provider,
`StandardServer` installs no default isolation provider and does not run its
finite-capacity plan validation. Existing rate limits, message limits, shared
reassembly accounting, and first-in-first-out request dispatch remain in force.

Use it when shared limits are the intended policy, or when a deployment needs
existing zero/unlimited settings or totals that cannot support the other
profiles. For example, a controlled set of clients can continue sharing a
reassembly budget without reserving separate startup capacity:

```csharp
server.ResourceIsolationOptions.Mode = ServerResourceIsolationMode.SharedOnly;
```

Avoid it if competing callers need request scheduling by owner or protected
capacity. Under saturation, earlier callers can occupy the shared resource and
later callers are refused: an over-budget intermediate UASC chunk results in
`BadTcpNotEnoughResources` and channel closure; a full decoded-request queue
results in `BadServerTooBusy`. Zero/unlimited settings retain their existing
meaning; `SharedOnly` does not disable all limits.

### FairShare

`FairShare` adds bounded owner accounting, optional per-owner hard limits, and
weighted scheduling of decoded requests. It reserves no capacity for startup,
reconnect, control work, or trusted owners. All stage capacity is shared.

Use it for competing applications that need turns at request execution but
do not need protected reserves. For example, two owners with queued reads take
turns as workers become available, while a lone active owner can use idle
shared capacity. Configure a hard limit if one owner should not occupy all
execution slots:

```csharp
server.ResourceIsolationOptions = new ServerResourceIsolationOptions
{
    Mode = ServerResourceIsolationMode.FairShare,
    Stages =
    [
        new ResourceIsolationStageOptions
        {
            Stage = ResourceIsolationStage.RequestExecution,
            OwnerHardLimit = 8
        }
    ]
};
```

Avoid it when recovery must have reserved resources, or when configuration
cannot meet the finite bounds described below. Nonzero reservations and
`TrustedOwners` are invalid in this profile. At saturation, already queued work
can wait for execution, but new work that cannot fit in the bounded queue is
refused with `BadServerTooBusy`. Retained chunks never wait for fairness:
insufficient bytes produce `BadTcpNotEnoughResources`. No existing charge is
revoked to let a new owner in.

### Balanced (default)

`Balanced` adds separate startup and reconnect reserves to FairShare, plus
control-work reserves at decoded-request stages. Ordinary traffic cannot use
those reserves. Eligible protected traffic uses its own reserve first and can
also use available shared capacity, subject to its hard limit.

Keep the default for finite-capacity servers that need to prevent ordinary
traffic from occupying every resource needed for recovery. For example, a
verified live Session's recovery request can use reconnect capacity when the
shared request queue is full. A new physical connection still needs protected
ingress classification to use connection reserves.

**Reserving capacity does not make every new client eligible for it.** Without
an ingress classifier, unknown connections use shared capacity even when the
bootstrap and reconnect reserves are idle. Startup does not require a
classifier for Balanced, and it does not silently remove those reserves.
Ordinary callers can receive `BadServerTooBusy` for decoded requests or
`BadTcpNotEnoughResources` for intermediate chunks while protected capacity is
still free. A connection rejected before protocol startup may simply close.

The [configuration example](#configuration) uses Balanced. Do not use an
undersized configuration and expect the server to reduce reserves or raise
totals automatically. If reserved capacity is not the intended policy, select
FairShare; if existing unlimited or otherwise unsupported totals are required,
select SharedOnly.

### TrustedReservations

`TrustedReservations` adds a separate reserve and scheduling weight for each
owner provisioned in `TrustedOwners`, in addition to Balanced's class reserves. Other owners cannot
borrow an owner's reserve. `TrustedResourceOwnerOptions.Reservations` can
override each stage's reserved amount and hard limit; omitted stages reserve
one operation or one maximum-message charge for that owner.

Use it when a bounded, known group, such as plant-control applications, needs
capacity that reporting applications cannot occupy. For example, the
[trusted-ingress example](#example-dedicated-trusted-ingress) gives that group
its own resources and more request-dispatch turns. At shared saturation it can
still use its free reserve. If its reserve, available shared capacity, or hard
limit is exhausted, it receives the same busy/resource errors as other callers;
being trusted does not bypass limits.

Do not select this profile with only a list of client names in JSON.
`CreateRuntimePlan` rejects missing owner provisioning, and the default
provider's constructor rejects a missing `IResourceIsolationClassifier`.
The deployment supplies a real classifier that verifies each mapping.
An unprovisioned trusted key returned by it causes an explicit runtime error,
not a fallback to unrestricted access.

## Configuration

This hosted example chooses finite limits explicitly. Its values illustrate
sizing, not a required configuration for every server. The connection hard
limit of 16 is per owner, even though the server permits 100 Sessions in total.

```csharp
services.AddOpcUa()
    .AddServer(options =>
    {
        options.ApplicationName = "MyServer";
        options.MaxMessageSize = 4 * 1024 * 1024;
        options.ConfigureBuilder = configuration => configuration
            .SetMaxSessionCount(100)
            .SetMaxChannelCount(200);
    })
    .WithChunkReassemblyBudget(64L * 1024 * 1024)
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

For direct construction, assign options before starting the server with its
`ApplicationConfiguration`. Here `telemetry` is the application's
`ITelemetryContext`:

```csharp
var server = new StandardServer(telemetry)
{
    ResourceIsolationOptions = new ServerResourceIsolationOptions
    {
        Mode = ServerResourceIsolationMode.Balanced
    },
    ChunkReassemblyBudget = new ChunkReassemblyBudget(64L * 1024 * 1024)
};
```

Use `Opc.Ua.Server` for the options and server types, `Opc.Ua` for the stage
names, `Opc.Ua.Bindings` for `ChunkReassemblyBudget`, and
`Microsoft.Extensions.DependencyInjection` for the hosting extensions.

### JSON configuration

The hosting section is `OpcUa:Server:ResourceIsolation`. Load it through the
server builder, for example
`services.AddOpcUa().AddServer(configuration.GetSection("OpcUa:Server"))`.
The binder parses these options explicitly, without reflection-based binding:

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

The same section also accepts `MaxOwnerKeyLength`, `DefaultWeight`,
`MaxRetainedMessageBytes`, `BootstrapReservedBytes`, `ReconnectReservedBytes`,
and `TrustedOwners`. Each trusted owner has `Key`, `Weight`, and `Reservations`;
each reservation has `Stage`, `Reserved`, and optional `HardLimit`.
JSON does not implement or register a classifier.

### Providers and startup ownership

At startup, `StandardServer` creates and validates a runtime plan, constructs
the default provider, and supplies it to its listeners and request dispatcher.
It disposes the provider it creates. The exceptions are SharedOnly and an
explicitly supplied provider: neither causes installation or validation of a
default provider.

A host can assign `ServerBase.ResourceIsolationProvider` directly or register
`IServerResourceIsolationProvider` in DI. That provider takes precedence even
when the options select SharedOnly. `StandardServer` does not dispose it;
its creator or DI container owns its lifetime. The same ownership rule applies
to an explicitly supplied classifier.

The custom-provider author is responsible for validating the provider's
capacities against the actual listener and request-queue limits. Consumers
still enforce their own limits; reporting extra capacity does not enlarge
their queues or connection limits and can invalidate reservation guarantees.
Default-options validation is not a validator for arbitrary provider code.

## Startup validation and sizing

`ServerResourceIsolationOptions.CreateRuntimePlan` validates FairShare,
Balanced, and TrustedReservations during default-provider startup. Invalid
values cause configuration exceptions rather than silent reserve removal or
hidden increases to configured totals. The JSON binder also rejects malformed
numbers and unknown enum values when options are loaded.

| Check | Available settings and corrective action |
| --- | --- |
| Finite resource bounds | Set positive `ServerConfiguration.MaxSessionCount`, `MaxChannelCount`, and `TransportQuotas.MaxMessageSize` / `MaxBufferSize`. Use SharedOnly if existing unlimited settings are intentional. |
| Connection space after reservations | Adjust `MaxChannelCount`, the intended `MaxSessionCount`, or `Stages` entries for `Connection`. Shared connection capacity must still hold Sessions plus one; see below. |
| Every stage can hold work | A `Stages[].Capacity` override can only lower its existing total. After all reservations, shared capacity must hold at least one operation or maximum-message charge. |
| Valid reserves and owner limits | Each nonzero reserve must hold at least one operation/message. `OwnerHardLimit` and trusted `HardLimit` must fit the stage and its applicable reserves. Reduce requested reserves, explicitly raise the underlying total, or choose a different profile. |
| Retained-message space | Size `ChunkReassemblyBudget` through `WithChunkReassemblyBudget(...)` or direct assignment. Reduce `MaxMessageSize` only if the workload permits it. A custom `MaxRetainedMessageBytes` must be a real upper bound, not a value chosen to pass validation. |
| Bounded accounting | `MaxTrackedOwners`, `MaxOwnerKeyLength`, and `DefaultWeight` must be positive. The owner table needs room for shared traffic, each protected class in use, and every trusted owner. |
| Valid profile entries | Duplicate stages/owner keys, invalid stages, arithmetic overflow, incompatible reservations, or trusted owners outside TrustedReservations are rejected. |
| Startup deadline | `HandshakeTimeout` must be positive and at most two minutes; two minutes is also the default. |

The server validates `HandshakeTimeout` even in SharedOnly mode and when a
custom isolation provider is supplied, before replacing an existing policy.
Listener-settings creation rechecks the value in case configuration changed
after initialization. Failures name `HandshakeTimeout`. Protected rate-limit
failures identify `ConnectionsPerSecond` or `ConnectionBurst` and the minimum
required by the configured reservations.

Slot totals come from `MaxChannelCount` for Connection/Handshake,
`ServerRateLimitOptions.MaxConcurrentSessionEstablishment` for SessionEstablishment
(its default applies to nonpositive values), and the existing request-dispatch
limits for queued, executing, and parked work. The dispatcher already uses at
least 100 queue slots and 100 execution slots; the plan mirrors those existing
minimums. It does not raise configured totals to make reservations fit.
`Stages[].Capacity` can apply a smaller isolation limit.

For Balanced and TrustedReservations with the default connection rate limiter
enabled, startup also checks the rate and burst. Each must accommodate one
bootstrap token, one reconnect token, one per trusted owner, and at least one
shared token. For example, Balanced needs effective `ConnectionsPerSecond` and
`ConnectionBurst` of at least 3. Set those fields explicitly to suit the
deployment, or select a profile without rate reservations. A custom
`IServerRateLimiterProvider` is not replaced or wrapped by this policy.

### Sessions, channels, and reserved capacity

The Session/SecureChannel N+1 relationship means a server configured for
`N = MaxSessionCount` needs room for an additional channel so a client can
replace a connection. **This implementation validates that the shared
Connection capacity itself is at least N+1**, after subtracting all connection
reservations. Protected ingress capacity is not counted toward ordinary
replacement-channel space.

For example, `MaxSessionCount = 100` and `MaxChannelCount = 101` satisfy the
unpartitioned N+1 count but fail Balanced's default runtime plan: its two
connection reserves leave only 99 shared slots. `MaxChannelCount = 103` leaves
101 shared slots and two reserved slots. A trusted owner with the default
one-connection reserve would require another slot. These are aggregate checks,
not a promise that every owner can occupy the advertised Session maximum:
per-owner limits and other callers' usage still apply.

### Reserve sizes and explicit opt-outs

Balanced defaults to one startup and one reconnect operation at slot-based
stages. It also reserves one control operation at decoded-request stages.
For ReassemblyBytes, each startup/reconnect reserve holds the default pool's
conservative maximum retained-message footprint. For RequestQueueBytes, each
reserve holds one `MaxMessageSize` charge.

With a 4 MiB message limit, a 65,536-byte buffer limit, and a 64 MiB reassembly
budget, the default retained-message bound is 16.25 MiB. Bootstrap and reconnect
each reserve 16.25 MiB, leaving 31.5 MiB shared. This bound allows for retained
arrays and pool overhead; it is not the payload size.

For custom buffer managers or transports, the host supplies
`MaxRetainedMessageBytes` if their maximum retained footprint differs from the
default bound. Startup checks its positivity and fit, but cannot prove that
the custom allocator respects it.

To disable a particular stage's protection intentionally, set that stage's
`BootstrapReserved`, `ReconnectReserved`, or `ControlReserved` to `0`.
For a trusted owner, set that stage's `TrustedResourceReservation.Reserved` to
`0`. This removes that guarantee; it is not an automatic fallback.
The top-level `BootstrapReservedBytes` and `ReconnectReservedBytes` instead
require at least one retained-message footprint in Balanced/TrustedReservations;
use the **ReassemblyBytes stage overrides**, not those top-level properties,
to disable byte reserves. Omitting an override keeps the profile default.

### Inspect the plan in application code

The server developer or deployment's configuration checker can run this code
before starting a server. Use a .NET 10 console project referencing the server library,
but no running listener, certificates, debugger, or custom classifier:

```csharp
using System;
using Opc.Ua;
using Opc.Ua.Bindings;
using Opc.Ua.Server;

var configuration = new ApplicationConfiguration
{
    ServerConfiguration = new ServerConfiguration
    {
        MaxSessionCount = 100,
        MaxChannelCount = 103,
        MinRequestThreadCount = 1,
        MaxRequestThreadCount = 100,
        MaxQueuedRequestCount = 100
    },
    TransportQuotas = new TransportQuotas
    {
        MaxMessageSize = 4 * 1024 * 1024,
        MaxBufferSize = 65536
    }
};
var options = new ServerResourceIsolationOptions();
var rateLimits = new ServerRateLimitOptions
{
    MaxConcurrentSessionEstablishment = 64
};
var budget = new ChunkReassemblyBudget(64L * 1024 * 1024);
ServerResourceIsolationPlan plan =
    options.CreateRuntimePlan(configuration, rateLimits, budget);

foreach (ResourceIsolationStage stage in Enum.GetValues(typeof(ResourceIsolationStage)))
{
    ResourceIsolationStagePlan limits = plan.GetStage(stage);
    Console.WriteLine(
        $"{stage}: total={limits.Capacity}, shared={limits.SharedCapacity}, " +
        $"bootstrap={limits.BootstrapReserved}, reconnect={limits.ReconnectReserved}, " +
        $"control={limits.ControlReserved}, trusted={limits.TrustedReserved}, " +
        $"ownerLimit={limits.OwnerHardLimit}");
}
```

For example, Connection prints total `103`, shared `101`, and bootstrap and
reconnect `1` each. ReassemblyBytes prints total `67108864`, shared `33030144`,
and bootstrap and reconnect `17039360` each. Use the actual application's
configuration, rate options, and budget in place of these illustrative values.

Creating a plan does not start enforcement, test ingress trust, or validate the
separately constructed connection rate limiter. Even with `Mode = SharedOnly`,
an explicit call to `CreateRuntimePlan` asks for a finite plan and validates it;
normal SharedOnly startup skips that call. `CreatePlan` is the separate
reassembly-only sizing calculator, not a complete plan for `GetStage`.
For an installed `DefaultServerResourceIsolationProvider`, its `Plan` property
exposes the snapshot actually in use.

## Accounting and lifetime

| Stage | What stays charged |
| --- | --- |
| `Connection` | A physical connection until actual closure, including reverse-connect handoff. |
| `Handshake` | Initial protocol startup until completion or closure, and OpenSecureChannel renewal work while processing. Initial startup has a fixed deadline. |
| `ReassemblyBytes` | UASC intermediate chunks: actual byte lengths of retained arrays. Common HTTPS body readers: a conservative charge taken before reading. |
| `SessionEstablishment` | Concurrent CreateSession/ActivateSession processing, not the lifetime of the resulting Session. |
| `RequestQueue` | Decoded requests awaiting dispatch. |
| `RequestExecution` | Running handlers; a supported Publish request releases this slot when it parks, waiting for a response without occupying a worker. |
| `RequestQueueBytes` | One `MaxMessageSize` charge per retained decoded request, through queued, executing, and parked processing. |
| `ParkedRequest` | Potentially parked work, charged before queueing and retained until completion when Publish decoupling is supported and enabled. |

Listeners never wait for reassembly capacity or stop the ordered receive loop
to wait for another owner. They reject a chunk that cannot fit. Waiting for
execution is confined to bounded decoded-request queues. Higher weights give
eligible queued owners more dispatch turns; they do not reserve bytes or
preempt a running handler.

Consumers return leases on completion, failure, cancellation, rejected later
checks, and shutdown. Returning a lease twice does not release capacity twice.
A handler that ignores cancellation cannot be forcibly stopped; its charge
remains until it finishes. No admitted lease is revoked to admit another caller.

The default RequestQueueBytes total is `MaxMessageSize` multiplied by the sum of
queued, executing, and parked slot bounds. This is a request-retention limit,
not a heap measurement. Decoder, operation, and body-size limits still apply.

Owner tracking is bounded by `MaxTrackedOwners`. An entry is removed when its
last lease is returned; an active entry is not evicted to admit a new key.
Some table slots are kept for protected classes and provisioned trusted owners.
Repeated rejected identities cannot create an unlimited idle cache.

With the default isolation provider active, its ReassemblyBytes class reserves
replace the shared budget's sessionless occupancy rule. The
`ChunkReassemblyBudget.MaxBytes` aggregate limit still applies. SharedOnly
retains the `MaxBytesWithoutSession` occupancy rule described in
[Rate limiting](RateLimiting.md#incomplete-messages). A separately shared budget
or custom rate limiter can reject work even when the isolation plan has room.

For partial messages, a Session token may not yet have arrived. The default
provider therefore asks the authoritative `ISessionBindingProvider` whether
the transport channel already carries an activated Session. If it does, its
reassembly can draw on the existing continuity/reconnect byte reserve even
without a tenant classifier. Sessionless traffic cannot consume this reserve.
This is a capacity guarantee for a session-bound channel, not a claim that its
anonymous user is a trusted tenant.

This membership-based classification is restricted to `ReassemblyBytes`: it
cannot obtain protected connection, rate, session-establishment, or request
execution capacity. It has a separate accounting key from the sessionless
traffic sharing the same peer address or application identity. Classification
is retained for that one partial message; the next message checks membership
again after closure or transfer.
It uses ordinary owner-table space, so byte continuity traffic cannot consume
the identity slot reserved for a genuine reconnect caller. An explicit
classifier mapping to the shared class is honored and is not automatically
promoted.

The reserve still has a finite size. Several session messages may share it if
they fit, but one maximum-sized message can occupy the reserve sized for one
message. It is shared by session-bound reassembly and explicitly classified
reconnect traffic, not a separate guaranteed allowance for each. This does not
promise simultaneous progress for arbitrarily many
activated clients. Increase the configured reserve or use provisioned
trusted-owner reservations when stronger separation is required.

Custom providers can opt into this behavior through
`IResourceIsolationReassemblyProvider`. Providers that do not implement that
capability retain the shared budget's original sessionless occupancy threshold,
even if they enable fair decoded-request scheduling.

## Trust, anonymous clients and NAT

Ordinary pre-authentication traffic is grouped by observed address, normalized
so IPv4-mapped IPv6 addresses do not create a second group. This limits abuse;
it does not authenticate the caller. NAT (network address translation) can make
many clients appear to share one address. A previously successful client at an
address does not make the next connection from that address trustworthy.

`IResourceIsolationClassifier.TryClassifyIngress` is the host's way to map
protected traffic before OPC UA authentication. The deployment operator
establishes the trusted network/proxy/transport boundary; the classifier author
uses that evidence. The provider validates the returned key and policy class,
but cannot verify the deployment's firewall or proxy configuration.

After authentication, validated application certificates and verified live
Session bindings allow stronger grouping. Certificates supplied under
SecurityPolicy None are removed from the context passed to the classifier.
A secure application certificate does not, by itself, grant a provisioned
trusted-owner reserve. An activated anonymous Session is not automatically a
trusted owner either.

Decoded-request classification uses `ISessionBindingProvider` and is checked
again before execution. Stale, transferred, expired, or unknown Session tokens
cannot preserve an earlier protected classification. Normal service
authentication and authorization still execute. HTTPS logical channels may
represent multiple clients; the classifier cannot treat one logical channel as
proof of a permanent client identity.

Repeated requests can reuse immutable owner classifications and certificate
snapshots. The cache holds at most eight immutable entries per live channel-id
object and uses weak lifetime keys, not a permanent dictionary of every observed
caller. Read-side lookup does not lock; a small set avoids one HTTP peer
immediately evicting another when they share a logical listener channel.
Each lookup still checks the current Session snapshot, channel evidence and
any configured classifier. Mutated certificates, changed mappings, another
Session, or reactivation invalidate the cached result. Reassembly-only
classifications are not usable as decoded-request authorization snapshots.

### Example: dedicated trusted ingress

This illustrative classifier is appropriate **only** for a gateway dedicated to
one provisioned group. Before forwarding traffic, that gateway authenticates
membership in the group. Network controls ensure no other sender can reach the
server using the gateway's observed source address, and untrusted traffic is
never forwarded through it. The example maps that already-protected path; the
address comparison does not supply those protections.

Configure `ProtectedIngress:GatewayAddress` in the host's `IConfiguration` with
the gateway address actually observed by the server. It is operator
configuration, not a request header or a client-supplied owner name.

```csharp
using System;
using System.Net;
using Microsoft.Extensions.Configuration;
using Opc.Ua;
using Opc.Ua.Server;

public sealed class DedicatedIngressClassifier : IResourceIsolationClassifier
{
    public DedicatedIngressClassifier(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        string address = configuration["ProtectedIngress:GatewayAddress"] ??
            throw new InvalidOperationException("Configure ProtectedIngress:GatewayAddress.");
        m_gatewayAddress = Normalize(IPAddress.Parse(address));
    }

    public bool TryClassifyIngress(
        IPEndPoint? remoteEndpoint,
        out ResourceIsolationIdentity identity)
    {
        if (remoteEndpoint != null &&
            Normalize(remoteEndpoint.Address).Equals(m_gatewayAddress))
        {
            identity = new ResourceIsolationIdentity("plant-control", ResourceIsolationClass.Trusted);
            return true;
        }
        identity = default;
        return false;
    }

    public bool TryClassify(
        SecureChannelContext channelContext,
        SessionBindingContext? sessionBinding,
        out ResourceIsolationIdentity identity)
    {
        identity = default;
        return false;
    }

    private static IPAddress Normalize(IPAddress address)
    {
        return address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
    }

    private readonly IPAddress m_gatewayAddress;
}
```

Returning `false` from `TryClassify` leaves the provider to apply the ingress
mapping or its ordinary verified-Session/application/address classification.
The code never trusts a caller's claimed group. All clients through this
gateway share **one** owner's limits; it is not an example of distinguishing
individual clients behind an arbitrary proxy or NAT.

Register the classifier and matching owner in a Generic Host (which supplies
`IConfiguration`), with room for the extra reserves:

```csharp
services.AddOpcUa()
    .AddServer(options =>
    {
        options.ApplicationName = "MyServer";
        options.MaxMessageSize = 4 * 1024 * 1024;
        options.ConfigureBuilder = configuration => configuration
            .SetMaxSessionCount(100)
            .SetMaxChannelCount(200);
    })
    .WithChunkReassemblyBudget(128L * 1024 * 1024)
    .ConfigureResourceIsolation(options =>
    {
        options.Mode = ServerResourceIsolationMode.TrustedReservations;
        options.TrustedOwners =
        [
            new TrustedResourceOwnerOptions
            {
                Key = "plant-control",
                Weight = 4,
                Reservations =
                [
                    new TrustedResourceReservation(ResourceIsolationStage.RequestExecution, 2, 8)
                ]
            }
        ];
    })
    .AddResourceIsolationClassifier<DedicatedIngressClassifier>();
```

Here the owner has two reserved execution slots, an execution hard limit of
eight, and weight four versus ordinary owners' default weight one. Other stages
use that owner's default reserves. For a direct server, assign the same options
and `server.ResourceIsolationClassifier = new DedicatedIngressClassifier(configuration)`
before startup. To protect Balanced startup/reconnect ingress instead, implement
an equally verified mapping returning `Bootstrap` or `Reconnect`, without
`TrustedOwners`; this Trusted classifier is not interchangeable with it.

## Transport behavior and overload signals

Raw TCP, Kestrel TCP, and OPC UA binary WebSockets share the server policy. They check
connection and handshake capacity before protocol-channel and cryptographic
state allocation. HTTPS checks physical connections before TLS and applies
bounded common request-body processing. Rate limits remain separate checks.

When the default connection rate limiter is enabled, Balanced and
TrustedReservations partition the existing rate and burst totals into shared
and protected tokens. Bootstrap, reconnect, and each trusted owner have one
token of burst and one token per second. Protected traffic
can also use available shared tokens; shared traffic cannot take protected
tokens. Closing a connection does not refund its token. FairShare and
SharedOnly keep the ordinary shared token bucket.

Initial startup has a fixed deadline independent of renewable channel
lifetimes. Reverse-connect handoff retains the physical connection charge
until actual closure. A configured client accepting a valid ReverseHello can
finish startup and save the connection for later use without releasing its
connection slot.

When a raw TCP connection could be accepted only by reclaiming an unused
channel, the rate limiter must approve the attempt **before** reclamation.
That approval consumes one token even if a concurrent change later prevents
reclamation. The retry does not consume a second token. This limits expensive
reclamation attempts and prevents a caller rejected by the rate limiter from
closing another client's channel. Owner-limit rejection does not consume a
rate token, and a failed attempt never borrows another class's reserved tokens.

At capacity, the raw listener can reclaim an unused established channel without
a Session, but not one with an active handshake, partial message, queued write,
or in-flight decoded request. An owner-limit or rate-limit refusal does not
evict another channel just to make room for that inadmissible caller.

| Condition | Client-visible result |
| --- | --- |
| Physical connection refused before protocol startup | Closure/transport error; a protocol status cannot always be sent. |
| UASC retained-chunk capacity exhausted | Transport ERR `BadTcpNotEnoughResources`, then closure; closure alone if ERR cannot be sent. |
| Decoded-request or SessionEstablishment capacity exhausted | Service-level `BadServerTooBusy`; clients should back off before retrying. |
| Configured Session maximum reached | `BadTooManySessions`, independently of isolation capacity. |
| Common HTTPS body-processing capacity refused | HTTP 503 (Service Unavailable), including refusal before a WebSocket upgrade. This is distinct from a rate limiter's HTTP 429. |

WebSocket close code 1009 means excessive message size, not arbitrary resource
pressure. See [Rate limiting](RateLimiting.md) for retry behavior and additional
overload signals.

OpenAPI WebSockets can process concurrent messages so a waiting Publish does
not stop other requests. Each pending frame acquires count and retained-byte
capacity before it is copied or scheduled, and holds that capacity through
response sending. A per-connection bound also applies without an isolation
provider. Capacity refusal after upgrade closes with 1013 (Try Again Later),
not the message-size code 1009. Shutdown cancels pending work; a handler that
outlives the shutdown wait keeps its accounting until it actually exits.

The default provider exposes `GetUsage(stage)` and `TrackedOwnerCount`, plus
`opcua.server.isolation.usage`, `opcua.server.isolation.owners`, and
`opcua.server.isolation.rejections` metrics. Rejections use only bounded stage
and reason labels (`Capacity`, `OwnerLimit`, `OwnerTableFull`, `InvalidOwner`).
Application diagnostics should not add owner keys, tokens, or raw addresses as
metric labels.

## Limitations

- Unknown callers receive best-effort shared service. Neither fairness nor idle
  reserves guarantee that all unknown clients can connect or make progress,
  particularly during a distributed flood. Protected pre-authentication access
  requires a deployment-enforced ingress boundary and a classifier.
- Reservations protect only the resources counted here. A full independent
  budget, custom limiter, exhausted protected reserve, or blocked handler can
  still prevent progress. There is no fixed response-time guarantee.
- This is not a whole-heap cap. Subscription/MonitoredItem storage, PubSub,
  custom REST body buffering, response buffers, and unrelated application
  allocations need their own limits. Request allocation costs are conservative
  accounting units, not measured heap sizes.
- Network bandwidth, connection-backlog attacks, and TLS saturation still need
  upstream protection. The server cannot authenticate an IP address by observing
  traffic history.
- A provider can be shared within a process, but it is not a quota store across
  a fleet. The host remains responsible for aligning every consumer's physical
  capacities with a custom shared policy.
