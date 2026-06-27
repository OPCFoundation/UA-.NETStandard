# Server session scalability and the 500-session load test

This document describes the 500-session server load test, the rationale for raising
the default session limit to **500**, and the bottlenecks found while profiling the
server under that load.

## Default session limit

`ServerConfiguration.MaxSessionCount` now defaults to **500** (previously 100). The
limit is the maximum number of concurrently *open* sessions a server accepts before
rejecting `CreateSession` with `BadTooManySessions`. The default applies whenever the
value is not set explicitly in the configuration XML
(`Stack/Opc.Ua.Core/Schema/ApplicationConfiguration.cs`).

Deployments that need a different ceiling can still override it:

* XML: `<ServerConfiguration><MaxSessionCount>...</MaxSessionCount></ServerConfiguration>`
* Fluent builder: `.SetMaxSessionCount(500)`
* Options/DI: `OpcUaServerOptions.MaxSessionCount`

> Each session also requires a secure channel, so size `MaxChannelCount`
> (default 1000) accordingly, and `MaxSubscriptionCount` for the subscriptions
> those sessions create.

## The load test

`Tests/Opc.Ua.Sessions.Tests/LoadTest.cs` contains
`ServerManySessionsLoadTestAsync`, which:

1. Creates **500** sessions (the default limit), each over a `Basic256Sha256`
   secure channel.
2. Adds **one slow-publishing subscription** per session (1000 ms publishing
   interval) with a single monitored item on a shared value node.
3. Drives value changes from a separate writer session for 60 s.
4. Asserts that **every** session receives notifications under sustained load.

The test is `[Explicit]` (in the `LoadTest` category) because it is resource
intensive and intended for capable load-test hardware / dedicated CI runners
rather than the normal unit-test pass.

### Tuning knobs

* `OPCUA_LOADTEST_SESSION_COUNT` — override the session count (default `500`) for
  quick validation on constrained machines.
* `OPCUA_LOADTEST_DURATION_SECONDS` — override the steady-state duration (default `60`).

Concurrent connection establishment is capped at `Environment.ProcessorCount * 4`
secure-channel handshakes, with bounded retries on transient connect timeouts, so
the full session set can be established without a self-inflicted connect storm.

## Bottlenecks found while profiling

The findings below were observed while bringing the load test up to 500 sessions.
They are ordered by impact.

### 1. Connection establishment dominates and is CPU bound

Establishing the sessions, **not** the steady-state publishing, is the dominant
cost. Every `CreateSession`/`ActivateSession` over a signed-and-encrypted policy
performs RSA asymmetric crypto (signature creation/verification, nonce handling).
On a small (4 vCPU) host these handshakes serialize on the CPU: when many
handshakes are attempted at once they thrash the cores and individual requests
exceed the client `OperationTimeout`, surfacing as `BadRequestTimeout`
(`0x80850000`) during connect.

Mitigations applied in the test (and recommended for clients connecting many
sessions):

* **Throttle concurrent handshakes** to roughly the available core count rather
  than firing all connects at once.
* **Reuse one resolved endpoint** instead of running `GetEndpoints` discovery per
  session — per-session discovery added a full extra round trip (and channel) for
  every session.
* **Retry transient connect timeouts** instead of failing the session outright.

The practical takeaway is that 500 sessions is comfortably supported, but bulk
connection throughput scales with CPU/core count; provision cores accordingly and
stagger client reconnect storms.

### 2. Request-thread saturation under a connect storm

The server services requests from a bounded worker pool
(`MaxRequestThreadCount`, default 100; `MinRequestThreadCount`, default 10).
While hundreds of CPU-heavy handshakes are in flight, ordinary requests —
including the long-lived `Publish` requests already queued by established
sessions — can wait long enough to time out on the client. The effect is
transient (it clears once the connect burst drains) but it shows that connect
bursts and steady-state publishing compete for the same worker pool.

### 3. Diagnostics/logging overhead amplifies load

Verbose (Debug-level) logging of every `Publish` round trip measurably slows the
server and client under load. The load-test fixture therefore runs at
`Warning` level. For production servers expecting many sessions, keep the
operational log level at `Information` or higher and avoid per-request Debug
logging on hot paths.

### 4. Per-session fixed overhead

Each session carries fixed overhead: a secure channel, a session diagnostics node
in the address space (when `DiagnosticsEnabled`), continuation-point budgets, and
its subscription/publish bookkeeping. At 500 sessions this is modest but linear;
servers that do not need server-side session diagnostics can disable them to
reduce per-session address-space churn.

## Summary

| Area | Observation | Recommendation |
| --- | --- | --- |
| Connect throughput | RSA handshakes are CPU bound and serialize under contention | Scale cores; throttle/stagger connects; reuse endpoint discovery |
| Request worker pool | Connect bursts can starve steady-state `Publish` requests | Size `MaxRequestThreadCount`; avoid connect storms |
| Logging | Debug-level per-request logging slows hot paths | Run at `Information`+ in production |
| Per-session overhead | Linear, modest at 500 sessions | Disable unneeded session diagnostics |

The default `MaxSessionCount` of 500 reflects that the reference server supports
500 sessions; the limiting factor in practice is connection-establishment
throughput, which scales with available CPU cores.
