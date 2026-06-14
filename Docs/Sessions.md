# Sessions, Reconnection, and Subscription Engines

This document describes how the OPC UA client stack composes the building
blocks that turn a configured endpoint into a running, fault-tolerant OPC
UA session: the raw `Session` and its reconnect helper, the
`ManagedSession` facade, the pluggable subscription engines, and how to
choose between them.

## Quick reference

| Type | Creates | Reconnect | Subscription engine | Recommended for |
|---|---|---|---|---|
| `Session` | itself (via constructor or `Session.Create...Async`) | none — caller drives via `Session.ReconnectAsync` / `SessionReconnectHandler` | `ClassicSubscriptionEngine` by default; pass `ISubscriptionEngineFactory` to opt in to V2 | callers that own session lifecycle and existing reconnect code |
| `DefaultSessionFactory` | raw `Session` | none — caller drives reconnect | configurable via `SubscriptionEngineFactory` init property | drop-in `ISessionFactory` for the legacy flow |
| `SessionReconnectHandler` | nothing (drives an existing session) | yes, against a `Session` (only) | inherited from the wrapped `Session` | legacy callers that already own a `Session` |
| `ManagedSession` | itself (via `CreateAsync`) | yes — built-in `ConnectionStateMachine` + `ReconnectPolicy` | `DefaultSubscriptionEngine` (V2) by default | new code; long-lived clients that need reconnect / failover |
| `ManagedSessionFactory` | `ManagedSession` | inherited from `ManagedSession` | inherited from `ManagedSession` | drop-in `ISessionFactory` that yields managed sessions |
| `ChannelManagerSessionFactory` | raw `Session` | channel-level reconnect through `IClientChannelManager` | classic engine | raw sessions that share channels and coordinate transport reconnect centrally |
| `ManagedSessionBuilder` | `ManagedSession` | inherited from `ManagedSession` | configurable via `UseSubscriptionEngine` | fluent / DI scenarios |
| `IClientChannelManager` | `IManagedTransportChannel` leases (shared) | channel-level reconnect, coalesced and notified to participants | n/a (channel-only) | central transport-channel sharing & reconnect for sessions / discovery clients |

Everything below explains how these pieces fit together.

## 1. `Session` — the OPC UA session primitive

`Session` (`Libraries/Opc.Ua.Client/Session/Session.cs`) is the lowest-level
client object that maps directly to a UA secure-channel + session pair on
the server. It implements `ISession` and exposes the full surface of the
OPC UA service set (Read, Write, Browse, Call, AddNodes, etc.) plus
session-level concerns: keep-alive, namespace tables, the type tree, the
node cache, and a publish pipeline.

A `Session` is bound to:

- An `ApplicationConfiguration` (security policies, certificates, transport
  quotas, telemetry).
- A `ConfiguredEndpoint` (URL, security mode, security policy URI).
- An `ITransportChannel` created against that endpoint.

Lifecycle transitions on a `Session` are explicit and synchronous-looking
from the caller's perspective:

- `Session.ReconnectAsync()` re-uses the existing channel where possible
  and re-establishes the activated session.
- `ISessionFactory.RecreateAsync(...)` builds a fresh `Session` that
  inherits the previous session's identity, subscriptions, and node cache,
  swapping in a new channel.

`Session` does **not** run a background reconnect state machine. If the
keep-alive callback raises a bad status, the caller decides whether to
reconnect, recreate, or abandon the session.

### Plugging in a subscription engine

`Session` accepts an optional `ISubscriptionEngineFactory` at
construction. The factory creates an `ISubscriptionEngine` that owns the
publish loop and the live subscription set. Two implementations ship in
the box:

- **`ClassicSubscriptionEngineFactory.Instance`** (default when no
  factory is supplied) — the historical fire-and-forget publish engine
  that drives `Subscription` and `MonitoredItem` directly from
  `Session`.
- **`DefaultSubscriptionEngineFactory.Instance`** — wraps the new
  options-based `SubscriptionManager` (the V2 subscription pipeline)
  inside an `ISubscriptionEngine` adapter so even a raw `Session` can
  expose the `ISubscriptionManager` API.

The engine is selected once, at construction. It is then reused across
reconnect / recreate cycles. See section 4 for the engine architecture.

### `DefaultSessionFactory`

`DefaultSessionFactory` (`Libraries/Opc.Ua.Client/Session/DefaultSessionFactory.cs`)
is the drop-in `ISessionFactory` that builds raw `Session` instances. It
exposes a single tunable knob beyond `ITelemetryContext`:

```csharp
var factory = new DefaultSessionFactory(telemetry)
{
    SubscriptionEngineFactory = DefaultSubscriptionEngineFactory.Instance, // optional; null => classic
    ReturnDiagnostics = DiagnosticsMasks.SymbolicId,
};
ISession session = await factory.CreateAsync(...);
```

When `SubscriptionEngineFactory` is `null`, `Session` falls back to
`ClassicSubscriptionEngineFactory` — preserving the historical default.

## 2. `SessionReconnectHandler` — legacy reconnect driver

`SessionReconnectHandler` (`Libraries/Opc.Ua.Client/Session/SessionReconnectHandler.cs`)
is the original reconnect helper that ships with `Session`. It implements
the timer-driven retry loop, picks between
`Session.ReconnectAsync` and `ISessionFactory.RecreateAsync`, handles
endpoint refreshes when the server certificate changes, and cooperates
with `ReverseConnectManager`.

```csharp
using var handler = new SessionReconnectHandler(
    telemetry,
    reconnectAbort: true,
    maxReconnectPeriod: 30000);

session.KeepAlive += (_, e) =>
{
    if (e.Status != null && ServiceResult.IsNotGood(e.Status))
    {
        handler.BeginReconnect(session, 1000, OnReconnectComplete);
    }
};
```

### Supported session types

`SessionReconnectHandler` drives `Session.ReconnectAsync` and
`ISessionFactory.RecreateAsync` directly, and replaces the channel on the
underlying object. It therefore only supports the concrete `Session`
class (or types derived from it). Passing any other `ISession`
implementation — most notably `ManagedSession` — throws
`NotSupportedException`, because those facades already run their own
reconnect state machine and exposing two reconnect drivers on the same
session would race.

```csharp
// OK: raw Session
ISession raw = await new DefaultSessionFactory(telemetry).CreateAsync(...);
handler.BeginReconnect(raw, 1000, callback);

// Throws NotSupportedException
ISession managed = await new ManagedSessionFactory(telemetry).CreateAsync(...);
handler.BeginReconnect(managed, 1000, callback);
```

`SessionReconnectHandler` is retained as a supported entry point for callers that
own raw `Session` instances. New code should prefer `ManagedSession` or — for raw
`Session` usage — wire an `IClientChannelManager` (see
[section 4](#4-iclientchannelmanager--centralised-channel-sharing-and-reconnect)
below) so that reconnect is handled transparently by the channel manager.

## 3. `ManagedSession` — the connection-state-machine facade

`ManagedSession` (`Libraries/Opc.Ua.Client/Session/ManagedSession.cs`) is
an `ISession` facade that wraps a raw `Session` and adds:

- A **connection state machine** (`ConnectionStateMachine`) that owns
  transitions between `Disconnected`, `Connecting`, `Connected`,
  `Reconnecting`, `Closing`, `Closed`.
- A **reconnect policy** (`ReconnectPolicy` / `ReconnectPolicyOptions`)
  with pluggable backoff strategies (`Exponential`, `Linear`,
  `Constant`), jitter, max-retry caps, bounded delays, and a shared
  `MaxTotalReconnectTime` budget.
- A **server-redundancy handler** (`IServerRedundancyHandler`,
  default: `DefaultServerRedundancyHandler`) that reads the server's
  `ServerRedundancy` object and can fail over to a backup endpoint.
- A **service gate** (`m_serviceLock`) that pauses caller-issued service
  calls (Read/Write/Browse/Call/...) for the duration of a reconnect or
  failover, so consumers see one transparent retry rather than a torn
  state.
- A **default V2 subscription engine** so the new options-based
  subscription API (`ISubscriptionManager`) is available on
  `ManagedSession.SubscriptionManager` out of the box.

```csharp
ManagedSession session = await ManagedSession.CreateAsync(
    configuration,
    endpoint,
    sessionFactory: new DefaultSessionFactory(telemetry),
    reconnectPolicy: new ReconnectPolicy(new ReconnectPolicyOptions
    {
        Strategy = BackoffStrategy.Exponential,
        InitialDelay = TimeSpan.FromSeconds(1),
        MaxDelay = TimeSpan.FromSeconds(30),
        MaxRetries = 0, // unlimited
        JitterFactor = 0.1,
        MaxTotalReconnectTime = TimeSpan.FromMinutes(5)
    }),
    redundancyHandler: new DefaultServerRedundancyHandler(),
    telemetry: telemetry);
```

### `ManagedSessionFactory`

`ManagedSessionFactory` (`Libraries/Opc.Ua.Client/Session/ManagedSessionFactory.cs`)
is the `ISessionFactory` that produces `ManagedSession` (cast as
`ISession` to satisfy the interface). Internally it composes a
`DefaultSessionFactory` to build the raw inner `Session`, then wraps it.
The factory is the drop-in replacement for code that currently calls
`DefaultSessionFactory.CreateAsync(...)` and wants managed reconnect for
free:

```csharp
ISessionFactory factory = new ManagedSessionFactory(telemetry);
ISession session = await factory.CreateAsync(...); // actually a ManagedSession
```

> **Naming note.** `ManagedSessionFactory` creates `ManagedSession`
> instances. `DefaultSessionFactory` creates raw `Session` instances. The
> two factories are siblings under `ISessionFactory`; `ManagedSessionFactory`
> uses an inner `DefaultSessionFactory` only as an implementation detail
> for building the underlying transport+session pair.

### `ManagedSessionBuilder`

`ManagedSessionBuilder` (`Libraries/Opc.Ua.Client/ClientBuilder/ManagedSessionBuilder.cs`)
is the fluent / DI-friendly entry point. `Build()` returns an immutable
`ManagedSessionOptions` snapshot; `ConnectAsync()` wraps `Build()` plus
`ManagedSession.CreateAsync(...)`:

```csharp
ManagedSession session = await new ManagedSessionBuilder(configuration, telemetry)
    .UseEndpoint(endpoint)
    .WithSessionName("MyClient")
    .WithSessionTimeout(TimeSpan.FromSeconds(60))
    .WithReconnectPolicy(p => p with
    {
        Strategy = BackoffStrategy.Exponential,
        InitialDelay = TimeSpan.FromSeconds(1),
        MaxDelay = TimeSpan.FromSeconds(30)
    })
    .WithServerRedundancy()
    .UseSubscriptionEngine(DefaultSubscriptionEngineFactory.Instance) // optional; V2 by default
    .ConnectAsync(ct);
```

The builder is also the integration point for
`OpcUaClientBuilderExtensions.AddClient` — DI consumers
inject a `Func<CancellationToken, Task<ManagedSession>>` that lazily
connects on first use and caches the resulting session.

### Reconnect semantics on `ManagedSession`

When the underlying session's keep-alive fires a bad status, the
connection state machine transitions `Connected → Reconnecting` and:

1. Pauses the service gate so all in-flight and incoming service calls
   wait.
2. Drives the configured `ReconnectPolicy` to compute the next delay
   (with jitter), bounded by `InitialDelay` / `MaxDelay`, capped by
   `MaxRetries` (where `0` means unlimited), and constrained by the
   shared `MaxTotalReconnectTime` retry budget.
3. Calls `Session.ReconnectAsync` (channel reuse path) and on failure
   `ISessionFactory.RecreateAsync` (channel rebuild path), updating the
   inner session reference atomically.
4. If the redundancy handler is enabled and the active server is
   unreachable, it consults
   `IServerRedundancyHandler.GetNextEndpointAsync` and recreates the
   session against the failover endpoint.
5. On success, transitions `Reconnecting → Connected` and releases the
   service gate, replaying any deferred calls.
6. On exhaustion (`MaxRetries` reached), transitions to `Closed` and
   surfaces a `ServiceResultException` to outstanding callers.

When a `ManagedSession` is backed by `IClientChannelManager`, participant reactivation uses the
manager's `IChannelReconnectPolicy`. The default `ExponentialBackoffChannelReconnectPolicy`
bounds each participant callback with a `ParticipantTimeout` of 30 seconds.

Because all of this is driven internally, callers must **not** wrap a
`ManagedSession` with `SessionReconnectHandler`; doing so throws
`NotSupportedException`.

## 4. `IClientChannelManager` — centralised channel sharing and reconnect

`IClientChannelManager`
(`Stack/Opc.Ua.Core/Stack/Client/IClientChannelManager.cs`) is the new
central registry of client-side transport channels introduced by issue
[#3288](https://github.com/OPCFoundation/UA-.NETStandard/issues/3288). It
sits *below* `Session` / `ManagedSession` and is concerned with the
underlying `ITransportChannel`, not with sessions or subscriptions. It
adds three behaviours that the previous one-channel-per-client model
could not support:

1. **Channel sharing** — multiple sessions (or discovery / registration
   clients) whose `ConfiguredEndpoint` and (optional) reverse-connect
   handle match share *one* underlying secure channel. The shared
   channel stays open until the last lease is released.
2. **Coalesced reconnect** — concurrent reconnect triggers (e.g. from
   multiple sessions on the same channel observing a keep-alive
   failure) collapse into a single reconnect cycle. Each attached
   participant is notified once via `IReconnectParticipant.OnReconnectAsync`
   in parallel.
3. **Transparent gating** — service calls through an
   `IManagedTransportChannel` block until the channel is `Ready` (gate
   released after both transport open AND participant reactivation
   complete). Internal reactivation traffic (e.g. `ActivateSession`
   issued by a participant inside `OnReconnectAsync`) bypasses the
   gate via an `AsyncLocal` scope managed by the manager.

### Session factory choices

Code that accepts an `ISessionFactory` can choose the session and
reconnect model without changing the consuming client:

- **`DefaultSessionFactory`** creates a raw `Session`. It does not run a
  reconnect state machine; the caller owns reconnect / recreate.
- **`ManagedSessionFactory`** creates `ManagedSession`, which wraps the
  raw session in the built-in reconnect state machine.
- **`ChannelManagerSessionFactory`** creates a raw `Session` whose
  transport channel comes from an `IClientChannelManager`. Multiple
  compatible sessions share channels and channel-level reconnect is
  coordinated centrally by the manager.

```csharp
IClientChannelManager channelManager = new ClientChannelManager(config, telemetry);
ISessionFactory factory = new ChannelManagerSessionFactory(
    channelManager,
    telemetry,
    DiagnosticsMasks.SymbolicId);

ISession session = await factory.CreateAsync(
    config,
    endpoint,
    updateBeforeConnect: true,
    sessionName: "SharedChannelSession",
    sessionTimeout: 60_000,
    identity: null,
    preferredLocales: default,
    ct);
```

Use `ChannelManagerSessionFactory` when the desired surface is still a
raw `Session`, but the transport should be shared with other sessions or
channel-only clients such as discovery and registration clients.

### Channel identity (`ManagedChannelKey`)

Two participants share a channel only when their `ManagedChannelKey`
values are equal. The key is composed of:

- Endpoint URL.
- Security policy URI and message security mode.
- Server-certificate SHA-1 thumbprint (matches X.509 thumbprint
  conventions; used only as a stable hash, not for security).
- A stable hash of the `EndpointConfiguration` value properties
  (timeouts, encoding, message-size limits).
- Client instance-certificate thumbprint.
- Reverse-connect identity (the `ITransportWaitingConnection` instance
  used to acquire the channel; `null` for forward connections).

Forward and reverse channels to the same server are **never** shared:
forward keys carry `null` for the reverse identity while reverse keys
carry the waiting-connection instance.

### State model

`IManagedTransportChannel.State` follows a three-stage gate model:

| State | Service calls allowed | Meaning |
|---|---|---|
| `Disconnected` | no (`BadSecureChannelClosed`) | Initial, or after graceful close. |
| `TransportConnecting` / `TransportReconnecting` | no (queue with cancellation) | Underlying transport is being opened. |
| `TransportConnectedSessionReactivating` | only via the internal reactivation bypass | Transport up; participants running `OnReconnectAsync`. |
| `Ready` | yes | Fully usable. |
| `Faulted` | no | Manager's retry policy exhausted. |
| `Closed` | no | Manager teardown. |

Only `Ready` releases the public service-call gate. Discovery and
session-service requests are **not** generally exempted; only the
internal reactivation bypass crosses the gate during
`TransportConnectedSessionReactivating`.

### Participant model — `IReconnectParticipant`

Clients that want to react to channel reconnect implement
`IReconnectParticipant`. `Session` implements it: when a channel
manager is wired into a session, the manager invokes
`Session.OnReconnectAsync` after each successful transport reconnect.
The session runs the `ActivateSession` slice and returns one of:

| Result | Meaning |
|---|---|
| `Reactivated` | Session is alive on the reconnected channel. |
| `RequiresSessionRecreate` | Channel is OK; this participant's server-side session was lost (e.g. `BadSessionIdInvalid`) — the manager dispatches participant recreation out of band. |
| `TransientFailure` | Transient channel-level failure; manager retries per policy. |
| `FatalForParticipant` | Authentication / cert problem specific to this participant — detach only this participant. |
| `FatalForChannel` | Fatal channel error; transition to `Faulted`. |

When a participant returns `RequiresSessionRecreate`, the manager invokes
`IReconnectParticipant.RecreateAsync(ct)` fire-and-forget and does not block the channel's
transition to `Ready` on that work. `Session` implements the callback by recreating its
server-side session in place. On target frameworks without default interface method support,
participants opt in with `IRecreateAwareReconnectParticipant`.

### Retry policy — `IChannelReconnectPolicy`

The default `ExponentialBackoffChannelReconnectPolicy` mirrors the
historical `SessionReconnectHandler` backoff defaults:
`500 ms → 30 s` with unlimited attempts. It also sets `ParticipantTimeout`
to 30 seconds, bounding each participant's `OnReconnectAsync` callback. A
participant timeout is treated as `TransientFailure`; retry exhaustion still
transitions the channel to `Faulted`. The `IChannelReconnectPolicy` default
interface member remains `Timeout.InfiniteTimeSpan` for custom-policy
backward compatibility, and older TFMs can opt in with `IParticipantTimeoutPolicy`.
The policy is configurable on the `ClientChannelManager` constructor.

### HTTPS resilience vs channel-mgr reconnect

On .NET 8 and later, HTTPS endpoints have two independent resilience layers:

- **HTTP resilience pipeline** — the named `HttpClient` created through
  [`IOpcUaHttpClientFactory`](../Stack/Opc.Ua.Core/Stack/Https/OpcUaHttpClientFactory.cs)
  uses `AddStandardResilienceHandler` from the
  `Microsoft.Extensions.Http.Resilience` package. The standard handler
  applies request-level rate limiting, a total request timeout, retries,
  circuit breaking, and per-attempt timeouts. Current package defaults are:
  3 retries with exponential backoff and jitter, a 30 s total request
  timeout, a 10 s attempt timeout, a circuit breaker that opens after a
  10% transient-failure ratio over a 30 s sample with at least 100
  requests, and a 1,000-request concurrency limit with no queue.
- **Channel manager reconnect** — `IChannelReconnectPolicy` runs per
  managed channel after the request has escaped the HTTP layer. Its
  default `ExponentialBackoffChannelReconnectPolicy` is
  `500 ms → 30 s` with unlimited attempts.

Use the HTTP pipeline for transient failures of one HTTPS call: the call
is retried or timed out, then the channel remains usable. Use the
channel manager for failures that require the full OPC UA channel to be
torn down and opened again, such as a server reboot, a closed TLS
connection, or a TLS rehandshake requirement.

With the DI defaults, an HTTPS request fails through the HTTP pipeline
first. If all HTTP retries are exhausted (or the timeout / circuit
breaker rejects the call), the resulting `HttpRequestException`, timeout,
or equivalent transport exception bubbles up to the OPC UA channel. The
channel manager then treats that as a channel-level failure and starts
its reconnect policy.

Avoid making both layers aggressive. The channel-manager `MaxAttempts`
should be sized assuming the HTTP layer already retried the individual
request. For HTTPS deployments, prefer a bounded channel policy such as
`MaxAttempts <= 5`, or use a shared `IRetryBudget` when the HTTP layer
participates in one so a single end-to-end retry budget caps both layers.

DI registration wires the standard handler automatically:

```csharp
services.AddOpcUa()
    .AddClient(opt =>
    {
        opt.Configuration = config;
        opt.Session.Endpoint = endpoint;
    });
```

To replace the DI default with custom HTTP resilience, configure the
same named client and remove the default handler before adding your own:

```csharp
services.AddHttpClient("Opc.Ua.Client")
    .RemoveAllResilienceHandlers()
    .AddStandardResilienceHandler(opts =>
    {
        opts.Retry.MaxRetryAttempts = 5;
        opts.AttemptTimeout.Timeout = TimeSpan.FromSeconds(30);
    });
```

For direct builder usage on .NET 8 and later, call
[`ManagedSessionBuilder.WithHttpsResilience(...)`](../Libraries/Opc.Ua.Client/Fluent/ManagedSessionBuilder.cs):

```csharp
ManagedSession session = await new ManagedSessionBuilder(config, telemetry)
    .UseEndpoint(endpoint)
    .WithHttpsResilience(opts =>
    {
        opts.Retry.MaxRetryAttempts = 5;
        opts.AttemptTimeout.Timeout = TimeSpan.FromSeconds(30);
    })
    .ConnectAsync(ct);
```

### HTTPS factory + OPC UA cert validation: secure-by-default fallback

`HttpsTransportChannel.CreateHttpClient` chooses between two `HttpClient`
construction paths:

- **Direct path** (`CreateDirectHttpClient`) — builds an
  `HttpClientHandler` configured with:
  - `ServerCertificateCustomValidationCallback` wired to the OPC UA
    `ICertificateValidatorEx` / `CertificateValidator` (validates against
    the OPC UA trust list / issuer chain).
  - The OPC UA application instance certificate attached as a client
    certificate (mutual TLS using the OPC UA application identity).
  - `AllowAutoRedirect = false` so an attacker-controlled redirect cannot
    silently move the OPC UA call to a different host.
  - `MaxConnectionsPerServer` and `MaxRequestContentBufferSize` quotas
    enforced at the HTTP layer.
- **Factory path** — calls `IOpcUaHttpClientFactory.CreateClient(...)`
  and uses the returned named `HttpClient` (its primary handler is owned
  by `IHttpClientFactory` and CANNOT be reconfigured by
  `HttpsTransportChannel` after the fact).

When an OPC UA `CertificateValidator` is configured for a channel — the
normal case for any non-`SecurityPolicies.None` HTTPS profile —
`HttpsTransportChannel.CanUseHttpClientFactory()` returns `false` and
the direct path is always taken. This is the secure default: it
guarantees that the OPC UA trust list, OPC UA mTLS, and the redirect
lock are in effect on every OPC UA HTTPS connection. If a non-`Shared`
factory was supplied but bypassed for security reasons,
`CreateHttpClient` emits a one-time `LogWarning` so operators see that
the named HttpClient pipeline (Polly resilience handler, etc.) is NOT
applied to this OPC UA HTTPS channel.

To keep BOTH Polly resilience AND OPC UA cert validation on the same
channel, register the named `Opc.Ua.Client` HttpClient with a
configured primary handler that wires OPC UA validation in yourself.
The handler must be re-buildable per channel from the configuration
because the OPC UA validator and client certificate are channel-bound,
not process-bound:

```csharp
services.AddHttpClient(OpcUaHttpClientDefaults.ClientName)
    .ConfigurePrimaryHttpMessageHandler(sp =>
    {
        // Resolve the per-application validator and client cert from
        // your DI container, then build a handler the same way
        // CreateDirectHttpClient does.
        ApplicationConfiguration config = sp.GetRequiredService<ApplicationConfiguration>();
        ICertificateValidatorEx validator = config.CertificateValidator;
        return new HttpClientHandler
        {
            ClientCertificateOptions = ClientCertificateOption.Manual,
            AllowAutoRedirect = false,
            ServerCertificateCustomValidationCallback = (_, cert, chain, _) =>
            {
                // … delegate to `validator.ValidateAsync(...)` synchronously.
            },
            // … and attach the OPC UA client instance certificate.
        };
    })
    .AddStandardResilienceHandler();
```

Without that explicit primary-handler configuration, OPC UA HTTPS
channels deliberately ignore the named-client pipeline and use the
direct path — preserving OPC UA security at the cost of Polly resilience
on those channels.

### Shared retry budget with `ManagedSession`

When `ManagedSession` is constructed with `WithChannelManager(...)`,
the manager owns *channel-level* retry while
`ManagedSession.ConnectionStateMachine` + `IReconnectPolicy` owns
*session-level* retry on top. The two are sequenced, not concurrent, and
share a single `IRetryBudget` for each outer reconnect cycle:

1. **Channel-level retry runs first.** A keep-alive failure on the
   inner `Session` triggers `IClientChannelManager.ReconnectAsync(channel, ct)`.
   The manager coalesces concurrent triggers from multiple sessions on
   the same channel, retries the transport open according to its
   `IChannelReconnectPolicy`, then notifies attached sessions via
   `OnReconnectAsync`.
2. **Outer state stays `Connected` while the manager handles it.**
   `ManagedSession` subscribes to `IManagedTransportChannel.StateChanged`;
   while the manager is in `TransportReconnecting` /
   `TransportConnectedSessionReactivating`, `ManagedSession` suppresses
   the keep-alive-driven outer state-machine churn.
3. **Outer retry shares its deadline with channel retry.** When the
   channel transitions to `Faulted`, `ManagedSession.StateChanged`
   raises `ConnectionState.Reconnecting`. The outer `IReconnectPolicy`
   schedules `Session.ReconnectAsync(ct)` and passes the same retry
   budget into `IClientChannelManager.ReconnectAsync(channel, budget, ct)`.
   Both layers cap their delays to the remaining time and stop scheduling
   retries when the budget is exhausted.

`ReconnectPolicyOptions.MaxTotalReconnectTime` defaults to five
minutes. Set it to the end-to-end reconnect window your application
expects; channel-manager delays are automatically shrunk to fit inside
that window instead of receiving a fresh budget on every outer attempt.

For example, with `MaxTotalReconnectTime = TimeSpan.FromSeconds(30)`,
a channel-manager reconnect that spends 27 seconds reopening transport
and reactivating participants leaves about three seconds for any outer
`ManagedSession` retry / failover decision. The channel manager clamps
its next scheduled delay to the remaining time and transitions to
`Faulted` once the shared budget is exhausted; the outer state machine
then fails over or closes instead of starting another 30-second channel
retry window. The result is one roughly 30-second reconnect cycle (plus
any already in-flight attempt), not 30 seconds per layer or per outer
attempt.

```csharp
ManagedSession session = await new ManagedSessionBuilder(configuration, telemetry)
    .UseEndpoint(endpoint)
    .WithChannelManager(channelManager)
    .WithReconnectPolicy(p => p with
    {
        MaxTotalReconnectTime = TimeSpan.FromSeconds(30)
    })
    .ConnectAsync(ct);
```

For migration notes on the budget-aware APIs, see
[the migration guide](MigrationGuide.md#shared-reconnect-budget-for-managedsession-and-the-channel-manager).

### Diagnostics surface contract — what tags and EventSource fields carry

The channel manager emits diagnostics through three independent
channels: `System.Diagnostics.Activity` tags (distributed tracing),
the `Opc.Ua.ChannelManager` `EventSource` (ETW / `dotnet-trace`), and
`System.Diagnostics.Metrics` instruments. Tag and field values surfaced
through any of these are restricted to **OPC UA-protocol-level fields
only**:

- `StatusCode` (numeric / symbolic), e.g. `BadSecureChannelClosed`
- `ServiceResult.SymbolicId`
- `ServiceResult.LocalizedText.Text` — the operator-facing message

Notably, the following are **never** sent to Activity tags or to
`EventSource` events:

- The full `ServiceResult.ToString()` serialization
- `ServiceResult.AdditionalInfo` — typically carries inner-exception
  messages, file paths, internal IDs
- `Exception.StackTrace`, `Exception.Message`, or any other inner .NET
  exception detail
- `Inner` recursion into `ServiceResult.InnerResult`

This keeps internal client/server diagnostics out of distributed
tracing backends where operators with telemetry-read access (but no
production-debug access) should not be able to read internal failure
detail. Full failure context — including stack traces and
`AdditionalInfo` — flows only through the local `ILogger.LogDebug`
path on the manager, where it stays under the host's log-access
controls.

The metric tag set is also bounded for routine operation:

| Instrument | Tag keys |
|---|---|
| `opcua.channel.open` / `opcua.channel.close` | `endpoint`, `reverse` (+ `reason` on close) |
| `opcua.channel.active` / `opcua.channel.refcount` / `opcua.channel.participants` | `endpoint` |
| `opcua.channel.reconnect.attempts` / `opcua.channel.reconnect.duration` | `endpoint`, `outcome` |
| `opcua.channel.gate.wait` | `endpoint` |
| `opcua.channel.participant.timeout.count` / `opcua.channel.participant.recreate.count` | `endpoint`, `participant` (+ `success` on recreate) |

`outcome` is one of `success`, `transient-failure`, `policy-exhausted`,
`fatal-channel`. `reason` is one of `lease-released`,
`manager-disposed`, `faulted`. `endpoint` cardinality is bounded by the
number of distinct OPC UA endpoint URLs the application connects to.

> The `participant` tag carries the **kind prefix** of the
> participant identifier (e.g. `"Session"`, `"Client"`), not the
> per-instance suffix. This keeps cardinality bounded by the small set
> of participant kinds rather than growing with every session /
> reconnect-storm participant ever created. The full per-instance
> `IReconnectParticipant.Id` is preserved on Activity tags and
> EventSource events so individual sessions remain correlatable in
> distributed traces. Custom participants that don't use the
> "kind-`-`-instance" naming convention contribute their full id to
> the tag, so prefer the prefix-then-suffix shape for new participant
> types.

### DI registration

`builder.AddClient(...)` registers `IClientChannelManager` as a
singleton resolved from `OpcUaClientOptions.Configuration`. The
`ManagedSessionBuilder` produced by `AddClient` automatically wires
the DI-resolved channel manager into every new `ManagedSession`. To
override, call `WithChannelManager(...)` on the builder explicitly.

```csharp
services.AddOpcUa()
    .AddClient(opt =>
    {
        opt.Configuration = config;
        opt.Session = new ManagedSessionOptions
        {
            Endpoint = endpoint,
            SessionName = "MyApp",
        };
    });

// Single channel manager + multiple sessions sharing channels per endpoint:
var sp = services.BuildServiceProvider();
var managedFactory = sp.GetRequiredService<Func<CancellationToken, Task<ManagedSession>>>();
ManagedSession s1 = await managedFactory(ct);
ManagedSession s2 = await managedFactory(ct); // shares s1's underlying channel
```

### Migrating from `AttachChannel` / `DetachChannel`

`IClientBase.AttachChannel(ITransportChannel)` and `DetachChannel()`
are marked `[Obsolete]` but remain functional. Migration path:

```csharp
// Old: manually managed channel
ITransportChannel ch = await UaChannelBase.CreateUaBinaryChannelAsync(...);
var session = new Session(ch, config, endpoint);
await session.OpenAsync(...);

// New: channel manager owns the channel; sessions share it
var manager = new ClientChannelManager(config, telemetry);
Session session = await Session.CreateAsync(manager, config, endpoint, ...);
```

For reverse-connect:

```csharp
ITransportWaitingConnection conn = await reverseConnectManager
    .WaitForConnectionAsync(serverUrl, serverUri, ct);
IManagedTransportChannel ch = await manager.GetAsync(participant, conn, ct);
```

### Testing the channel manager

The channel manager is covered by a layered stress and chaos test suite in
[`Tests/Opc.Ua.Stress.Tests/`](../Tests/Opc.Ua.Stress.Tests/):

- **L1 Contract** — fast deterministic fake-based tests for coalescing, participant result aggregation, retry
  budgets, hung participants, recreate dispatch, lease lifecycle, gate + bypass, key equivalence, certificate
  rotation, and leak accuracy. These run in every PR.
- **L2 Integration** — live in-process server tests for server outage recovery, live certificate rotation,
  participant timeout, session recreate dispatch, and failover lease swap. These run in every PR.
- **L3 ChaosTCP** — TCP proxy chaos tests for transparent reconnect under load, subscription survival,
  accept-but-stall, and mixed drop / block-accept failures. These run nightly.
- **L4 Soak** — long-running randomized soak, combinatorial matrix, and memory-stability runs. These are manual or
  nightly only.

```bash
# Contract + Integration (default PR CI):
dotnet test Tests/Opc.Ua.Stress.Tests --filter "Category=Contract|Category=Integration"

# ChaosTCP (nightly):
dotnet test Tests/Opc.Ua.Stress.Tests --filter "Category=ChaosTCP" --TestRunParameters.Parameter(Seed=<n>)

# Soak (manual):
dotnet test Tests/Opc.Ua.Stress.Tests --filter "Category=Soak"
```

Every chaos test prints its seed at the start of the run. Failed chaos runs can be reproduced by passing the same
seed back to the test host with `--TestRunParameters.Parameter(Seed=<n>)`.

## 5. Subscription engines

The `ISubscriptionEngine` abstraction
(`Libraries/Opc.Ua.Client/Session/ISubscriptionEngine.cs`) decouples the
publish pipeline from `Session`. Each `Session` owns exactly one engine
chosen at construction; the engine manages the publish workers, ack
queues, keep-alive timers, and dispatch to subscriptions.

### `ClassicSubscriptionEngine`

The historical engine. Owns:

- The `Subscription` and `MonitoredItem` classes in
  `Libraries/Opc.Ua.Client/Subscription/Classic`.
- A fire-and-forget publish loop driven from
  `Session.BeginPublish` / `OnPublishComplete`.
- The `Subscriptions` collection on `ISession` and `ManagedSession`.

This is the default when no `SubscriptionEngineFactory` is supplied to
`Session` directly. It is also what `ManagedSession` exposes via the
classic `Subscriptions` property regardless of which engine is active —
an internal `SubscriptionBridge` forwards classic subscriptions onto the
V2 engine when one is in use, so existing classic-API code keeps
working.

### `DefaultSubscriptionEngine` (V2)

The new engine wraps the options-based `SubscriptionManager`
(`Libraries/Opc.Ua.Client/Subscription/SubscriptionManager.cs`) inside
the `ISubscriptionEngine` contract. It owns:

- The `Subscriptions.Subscription` / `Subscriptions.MonitoredItems.MonitoredItem`
  records consumed via `IOptionsMonitor<T>`.
- A worker-pool publish loop with adaptive worker count.
- The full subscription-service-set lifecycle: `CreateSubscription`,
  `ModifySubscription`, `SetPublishingMode`, `Republish`, plus
  `CreateMonitoredItems`, `ModifyMonitoredItems`, `DeleteMonitoredItems`,
  `SetMonitoringMode`, `Call`, `SetTriggering` — all routed back through
  the `Session`'s service-set client methods.
- An `ISubscriptionNotificationHandler` callback contract for
  `OnDataChangeNotificationAsync` / `OnEventDataNotificationAsync` /
  `OnKeepAliveNotificationAsync`.

`DefaultSubscriptionEngine.SubscriptionManager` exposes the
`ISubscriptionManager` API; on `ManagedSession` it surfaces directly as
`SubscriptionManager`.

```csharp
// V2 engine is the default for ManagedSession
ManagedSession session = await new ManagedSessionBuilder(configuration, telemetry)
    .UseEndpoint(endpoint)
    .ConnectAsync(ct);

var handler = new MyNotificationHandler(); // : ISubscriptionNotificationHandler
ISubscription subscription = session.AddSubscription(handler,
    new Opc.Ua.Client.Subscriptions.SubscriptionOptions
    {
        PublishingInterval = TimeSpan.FromMilliseconds(500),
        KeepAliveCount = 10,
        LifetimeCount = 100
    });

subscription.TryAddMonitoredItem(
    "ServerStatus_CurrentTime",
    VariableIds.Server_ServerStatus_CurrentTime,
    o => o with
    {
        SamplingInterval = TimeSpan.FromMilliseconds(250),
        QueueSize = 10
    },
    out IMonitoredItem _);
```

### Unbounded monitored items (default)

The V2 `ISubscription` returned from `Add(...)` transparently splits
monitored items across multiple server-side partition subscriptions
when the server's per-subscription cap would be exceeded. Single-
partition workloads pay zero overhead because the composite
collection short-circuits to the primary partition. Pin items into
the same partition with `MonitoredItemOptions.Affinity` so per-
subscription features like `SetTriggering` keep working across the
group; opt out of partitioning entirely with
`SubscriptionOptions.DisableUnboundedItemMode = true`. Full
developer guide: [Subscriptions § Unbounded monitored items](Subscriptions.md#unbounded-monitored-items).

### V2 notification pooling (opt-in)

The V2 subscription engine supports activator-level pooling of
notification payload instances to reduce GC pressure on
high-throughput publish loops. Pooling is **opt-in** and disabled by
default. Enable it via `ManagedSessionBuilder.WithPoolNotifications()`,
the `ManagedSessionOptions.PoolNotifications` init property, or by
setting `ISubscriptionManager.PoolNotifications = true` directly on
the V2 manager:

```csharp
ManagedSession session = await new ManagedSessionBuilder(configuration, telemetry)
    .UseEndpoint(endpoint)
    .WithPoolNotifications()        // opt in
    .ConnectAsync(ct);
```

How it works:

- Types that opt in to pooling implement `IPooledEncodeable`
  (a subinterface of `IEncodeable`) and provide a `Reuse()` method
  that resets fields and returns the instance to its activator's
  pool. The activator derives from `PooledEncodeableType<T>` instead
  of `EncodeableType<T>` and owns a bounded, lock-free pool.
- When `PoolNotifications` is `true`, the V2 subscription
  dispatcher walks each `DataChangeNotification` /
  `EventNotificationList` in a `finally` block after the handler
  await completes, calling `Reuse()` on every payload item that
  implements `IPooledEncodeable`. Types that do not implement the
  interface are skipped silently — the walk is universally safe.
- The walk runs on the subscription's single-reader
  `ProcessMessageAsync` loop, so it never races the channel decode
  that produced the payload.

#### Handler contract — retain by copy

When `PoolNotifications` is enabled, handlers **must not** retain a
reference to a `DataChangeNotification` / `EventNotificationList` /
`MonitoredItemNotification` / `EventFieldList` past the await of
the dispatch call. The pool may re-rent those instances to the next
publish immediately after `Reuse()` runs. Handlers that need to
keep values must **copy** them out before returning:

```csharp
public ValueTask OnDataChangeNotificationAsync(
    ISubscription subscription, uint sequenceNumber,
    DateTime publishTime,
    ReadOnlyMemory<DataValueChange> notification,
    PublishState publishStateMask, IReadOnlyList<string> stringTable)
{
    foreach (DataValueChange change in notification.Span)
    {
        // OK: DataValueChange is a struct, captured by value.
        // Value (DataValue) and DiagnosticInfo are not themselves
        // pooled in this design, so storing them past the call is
        // safe.
        m_history.Add(new MyHistoryEntry(
            change.MonitoredItem?.NodeId,
            change.Value,           // safe to retain
            change.DiagnosticInfo));
    }

    return default;
}
```

The `DataValueChange` and `EventNotification` projection structs
are designed not to surface a reference to a pooled instance —
they project the inner `DataValue` / `ArrayOf<Variant>` directly,
which are not themselves pooled. Handlers can safely copy
`DataValueChange` / `EventNotification` by value and continue to
use them after the await.

What is **not** safe under pooled mode:

- Retaining the outer `DataChangeNotification` /
  `EventNotificationList` reference past the await.
- Retaining individual `MonitoredItemNotification` /
  `EventFieldList` references past the await.
- Calling `Reuse()` on a retained reference after the framework
  has already done so — `Reuse()` is idempotent against accidental
  double-call (it uses a per-instance sentinel), but the pool may
  have already re-rented the instance, in which case the late
  `Reuse()` steals it from its current consumer.

The dispatcher is the only framework-initiated `Reuse()` caller.
Handler code that follows the retain-by-copy rule never reaches
the unsafe pattern.

### Server-side request/response pooling

On the server side, decoded `IServiceRequest` and `IServiceResponse`
objects are automatically returned to their activator pools after the
service handler completes and the response is encoded to the wire.
This is **unconditional** — no opt-in flag is required — because the
server framework owns the full lifecycle of both objects:

- The request is decoded by the channel, consumed by the service
  handler, and never exposed to application code by reference.
- The response is constructed by the handler, encoded by the channel's
  `WriteSymmetricMessage` / `BinaryEncoder.EncodeMessage` path, and
  then has no further consumers.

The reuse calls are placed in `finally` blocks in
`TcpTransportListener.OnRequestReceivedAsync` (UA-TCP transport) and
`EndpointBase.InvokeServiceAsync` (HTTPS transport), ensuring both
objects are released regardless of success or failure.

Server-side node managers and service handlers do not need any code
changes to benefit from this pooling — it is transparent at the
channel/transport layer.

### Choosing an engine

| Scenario | Engine |
|---|---|
| Existing code using `session.AddSubscription(new Subscription(...))` and the classic event-driven API | `ClassicSubscriptionEngine` (the default for raw `Session`) |
| New code, options-based subscriptions, DI / Microsoft.Extensions.Options friendly | `DefaultSubscriptionEngine` (the default for `ManagedSession`) |
| Migrating a managed session but you still need a classic subscription you cannot rewrite | `ManagedSession` with `UseSubscriptionEngine(ClassicSubscriptionEngineFactory.Instance)`; `SubscriptionManager` then throws `InvalidOperationException` and you keep using `Subscriptions` |

The two engines can co-exist on a `ManagedSession`:
`ManagedSession.Subscriptions` (classic API) keeps working alongside
`ManagedSession.SubscriptionManager` (V2 API) when the V2 engine is
active. Internally, classic subscriptions are bridged onto the V2
publish pipeline.

## 6. The `INodeCache` surface

`Session.NodeCache` (and `ManagedSession.NodeCache`) returns
`INodeCache`, the unified client-side cache contract. As of 2.0 it is
the single contract — the previous `ILruNodeCache` parallel interface
has been merged into it and removed.

The interface deliberately exposes two complementary lookup families:

- **`Find*` / `Fetch*`** — take an `ExpandedNodeId`, return a
  nullable result, may fetch from the server. `Find*` consults the
  cache first; `Fetch*` always re-reads from the server and updates
  the cache.
- **`Get*`** — take a local `NodeId`, return a non-nullable result
  (throws when the node cannot be resolved). LRU-style direct hit;
  cheaper for in-process callers that already hold a local
  `NodeId` and a known namespace index.

Both families coexist on `INodeCache` because the lifecycle and error
semantics differ. All async methods return `ValueTask` /
`ValueTask<T>`; only `void Clear()` is synchronous (pure local-state
mutation).

For migration details see
[2.0 migration guide — Node States and INodeCache](migrate/2.0.x/node-states.md#inodecache-changes).

## 7. Putting it all together

Pick the entry point that best matches your call site:

- **Already have a `Session` and an existing reconnect helper.** Keep
  `Session` + `SessionReconnectHandler`. They are first-class APIs in
  2.0 and not deprecated. You may opt into the V2 subscription engine
  by passing
  `SubscriptionEngineFactory = DefaultSubscriptionEngineFactory.Instance`
  to `DefaultSessionFactory`.
- **New service / ASP.NET Core / generic-host application.** Use
  `services.AddOpcUa().AddClient(...)` and resolve
  `Func<CancellationToken, Task<ManagedSession>>` from DI.
- **New code without DI.** Use `ManagedSessionBuilder.ConnectAsync(...)`.
- **Drop-in replacement for `DefaultSessionFactory` that wants managed
  reconnect.** Switch to `ManagedSessionFactory` — the public surface
  (`ISessionFactory`) is identical.
- **Need to share a single underlying `Session` across two reconnect
  drivers, or wrap a `ManagedSession` with `SessionReconnectHandler`.**
  Don't. The reconnect drivers race, and `SessionReconnectHandler` will
  throw `NotSupportedException` to enforce this.

## See also

- [2.0 migration guide — ManagedSession and Automatic Reconnection](migrate/2.0.x/sessions-subscriptions.md#managedsession-and-automatic-reconnection)
- [2.0 migration guide — Sessions, GDS Client, and Subscriptions](migrate/2.0.x/sessions-subscriptions.md) — V2 subscription engine, fluent builder, and DI integration are covered alongside ManagedSession.
- [TransferSubscription](TransferSubscription.md) — server-driven session-handoff support.
- [Observability](Observability.md) — telemetry plumbed through `ITelemetryContext` on every factory and session type.
- [Reverse Connect](ReverseConnect.md) — works with both `Session` and `ManagedSession`.
- [Packet Capture, Dissection, and Replay](PacketCapture.md) — `Opc.Ua.Bindings.Pcap` composes with `IClientChannelManager` via the `ITransportBindingRegistry` resolved from the host's `IServiceProvider`; channel sharing, transparent reconnect, and faulted-entry swap all flow through to one continuous capture session per `ManagedChannelKey`.
