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

The load is configured through environment variables so it can be dialed down for quick validation on constrained machines or up for dedicated load hardware:

* `OPCUA_LOADTEST_SESSION_COUNT` — number of reader sessions to establish (default `500`).
* `OPCUA_LOADTEST_DURATION_SECONDS` — steady-state measurement duration after all sessions are connected (default `60`).
* `OPCUA_LOADTEST_CONNECT_CONCURRENCY` — maximum number of secure-channel handshakes performed in parallel (default `max(2, ProcessorCount / 4)`).
* `OPCUA_LOADTEST_CONNECT_TIMEOUT_SECONDS` — deadline for establishing the full session set (default `max(120, SessionCount)`).

Connecting the sessions uses its own deadline (`OPCUA_LOADTEST_CONNECT_TIMEOUT_SECONDS`) that is completely independent of the steady-state duration, so a slow connect phase can never shorten — or cancel — the measurement window. (An earlier revision of the test shared a single 60 s deadline between connecting and measuring; on slower hardware that deadline fired mid-connect and surfaced as an unhandled `TaskCanceledException` instead of a meaningful assertion.) Concurrent handshakes are bounded by `OPCUA_LOADTEST_CONNECT_CONCURRENCY`, with bounded retries on transient connect failures, all handled locally so cancellation can never fault the connect aggregation.

The connect-concurrency default is deliberately gentle. Every handshake is RSA-bound and, because all sessions share one client certificate, a large simultaneous-connect burst can both oversubscribe the CPU and race the shared certificate's private-key handle (see the bottlenecks below). Raise `OPCUA_LOADTEST_CONNECT_CONCURRENCY` on dedicated, idle load hardware to connect faster.

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

### 2. The server brute-force lockout vs. a single shared client certificate

The session manager has a brute-force protection that locks a client out after a number of consecutive failed authentication attempts. The failure counter is keyed by the client's application instance certificate thumbprint (or application URI), and — by design, to stop an attacker resetting the counter with interleaved anonymous logins — a successful *anonymous* activation does **not** clear it.

A load test (or any aggregator) that opens hundreds of sessions from a **single** client certificate is therefore fragile: once a handful of handshakes fail transiently under the connect burst, the shared certificate accumulates enough failures to be locked out, and every remaining session is then rejected with `BadUserAccessDenied` (`0x801F0000`) until the lockout window expires.

To support this legitimate pattern the lockout is now configurable via `ServerConfiguration.MaxFailedAuthenticationAttempts` (default `5`, preserving the previous behaviour; `0` or less disables it). The load-test fixture sets it to `0`, and high-volume single-certificate deployments should size it for their expected transient-failure rate (or disable it and rely on certificate trust). It is exposed on the fluent builder as `.SetMaxFailedAuthenticationAttempts(...)`.

### 3. Shared client-certificate concurrency under parallel connect

All of the sessions in the test share one client (application instance) certificate. Each `OpenSecureChannel` handshake signs with that certificate's private key, so a large number of *simultaneous* connects drive concurrent private-key operations against the same underlying `X509Certificate2` while the certificate's reference-counted handle is also being acquired and released on the connect/retry paths. Under that contention the handshake can fail with `BadTcpInternalError` whose inner exception is `CryptographicException: m_safeCertContext is an invalid handle`, and the failures in turn drive retries that amplify the storm.

The load test mitigates this by keeping the default connect concurrency low (see the tuning knobs above); a gentle connect exhibits none of these failures. Clients that must open many sessions quickly from one certificate should likewise throttle their concurrent handshakes. Hardening the shared-certificate handle for fully parallel signing is tracked as a separate follow-up.

### 4. Request-thread saturation under a connect storm

The server services requests from a bounded worker pool
(`MaxRequestThreadCount`, default 100; `MinRequestThreadCount`, default 10).
While hundreds of CPU-heavy handshakes are in flight, ordinary requests —
including the long-lived `Publish` requests already queued by established
sessions — can wait long enough to time out on the client. The effect is
transient (it clears once the connect burst drains) but it shows that connect
bursts and steady-state publishing compete for the same worker pool.

### 5. Diagnostics/logging overhead amplifies load

Verbose (Debug-level) logging of every `Publish` round trip measurably slows the
server and client under load. The load-test fixture therefore runs at
`Warning` level. For production servers expecting many sessions, keep the
operational log level at `Information` or higher and avoid per-request Debug
logging on hot paths.

A related amplifier was a client-side hot loop: when a session's channel dropped, the V2 `SubscriptionManager` publish workers re-issued `Publish` immediately, each failing instantly with `BadNotConnected` and logging a full stack trace every iteration — millions of log lines (gigabytes) within a single run. The publish workers now classify transport-down statuses (`BadNotConnected`, `BadConnectionClosed`, `BadSecureChannelClosed`, `BadSessionIdInvalid`, `BadSecureChannelIdInvalid`), throttle with a bounded exponential backoff, and log such a transition only once instead of per iteration. The server side similarly logs an abandoned/closed-channel race (`BadSecureChannelClosed`) at Debug rather than Error-with-stack-trace.

### 6. Per-session fixed overhead

Each session carries fixed overhead: a secure channel, a session diagnostics node
in the address space (when `DiagnosticsEnabled`), continuation-point budgets, and
its subscription/publish bookkeeping. At 500 sessions this is modest but linear;
servers that do not need server-side session diagnostics can disable them to
reduce per-session address-space churn.

## Summary

| Area | Observation | Recommendation |
| --- | --- | --- |
| Connect throughput | RSA handshakes are CPU bound and serialize under contention | Scale cores; throttle/stagger connects; reuse endpoint discovery |
| Auth lockout | Single shared client cert is locked out after transient handshake failures (anonymous success does not clear the counter) | Configure `MaxFailedAuthenticationAttempts` (or disable) for high-volume single-cert clients |
| Shared certificate | Concurrent `OpenSecureChannel` signing on one shared cert can hit an invalid-handle race | Throttle concurrent handshakes per certificate |
| Client publish workers | A dropped channel could hot-loop `Publish`/`BadNotConnected` and flood logs | Classify transport-down statuses; throttle with backoff; log once |
| Request worker pool | Connect bursts can starve steady-state `Publish` requests | Size `MaxRequestThreadCount`; avoid connect storms |
| Logging | Debug-level per-request logging slows hot paths | Run at `Information`+ in production |
| Per-session overhead | Linear, modest at 500 sessions | Disable unneeded session diagnostics |

The default `MaxSessionCount` of 500 reflects that the reference server supports
500 sessions; the limiting factor in practice is connection-establishment
throughput, which scales with available CPU cores.
