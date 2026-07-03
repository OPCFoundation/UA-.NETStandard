# Server session scalability

This document describes how the reference server scales to large numbers of concurrent client sessions on a single node, what bounds that scale, the built-in controls for degrading gracefully under load, and how to configure the server and scale out. For measured numbers see [Performance Benchmarks — Server session scalability](Benchmarks.md#server-session-scalability).

## How a session is established

A client session is brought up in four stages:

1. **TCP accept** — the operating system accepts the socket and the listener creates a channel object.
2. **Secure channel** — `OpenSecureChannel` performs the asymmetric (RSA) handshake: decrypt and signature-verify with the server certificate's private key, and generate nonces.
3. **CreateSession** — admission checks (session and channel limits), server-nonce generation and signing, and per-session diagnostics node creation.
4. **ActivateSession** — verification of the client signature and the user identity token, and activation of the session.

After activation the session enters **steady state**: it keeps one long-polled `Publish` request outstanding and receives notifications as its monitored items change.

## What bounds single-node scale

**Session establishment is CPU-bound.** The dominant cost of bringing up a session is the asymmetric secure-channel handshake — the RSA decrypt, the certificate-chain validation, and the server- and client-signature operations. Handshakes run per channel on the thread pool and parallelize across cores, so establishment throughput scales with the core count, but a large simultaneous connect burst can saturate every core. On a typical multi-core developer machine the server establishes on the order of a thousand or more concurrent sessions cleanly; beyond that, connect throughput is bounded by handshake CPU. This is the fundamental single-node ceiling.

**Connect storms amplify overload.** If the server is saturated and a handshake is slow enough to time out, a client may retry; the retry can create a fresh server session while the original is left orphaned, adding load rather than relieving it. Unchecked, this is a positive-feedback loop. The server's admission controls break the loop by returning a fast, deterministic *busy* response that a cooperating client honors, instead of failing deep in the handshake and inviting a retry.

**Steady-state delivery is not the bottleneck.** Once sessions are up, the asynchronous publish path continues to deliver notifications. A held `Publish` is asynchronously parked while it waits and, with request decoupling enabled (the default), does not tie up a request-processing worker.

## Admission control and rate limiting

The server can shed excess load deterministically rather than aborting work mid-handshake. The full configuration surface — algorithms, limits, and dependency-injection hooks — is described in [Rate Limiting and Admission Control](RateLimiting.md). In summary, the server provides:

- **Connection-admission rate limiting** at the TCP listener, bounding the rate of new secure-channel handshakes to what the host can absorb.
- **Session-establishment admission**: `CreateSession` and `ActivateSession` requests beyond the configured concurrency are rejected with **`BadServerTooBusy`**, carrying a machine-readable retry-after hint, instead of queuing unboundedly.
- **HTTPS/Kestrel rate limiting**: the HTTPS binding can attach an ASP.NET Core rate limiter through dependency injection.
- **Client-side adaptive backoff**: the client honors a server's *busy* signal — and any retry-after hint — with bounded exponential backoff, so a well-behaved client ramps its connects instead of hammering.

Session establishment keeps the CPU-bound signature work outside the session-table lock, so concurrent `CreateSession` calls are not serialized behind one signing operation.

## Held Publishes and the request-thread budget

A steady-state session keeps one long-poll `Publish` outstanding, waiting for the next notification. The operating-system thread is released while it waits. With **`DecoupleHeldPublishRequests`** enabled (the default), a parked `Publish` also releases its request-processing worker at the point it parks, so a small worker pool can hold many thousands of outstanding Publishes and **`MaxRequestThreadCount`** does not have to scale with the session count.

Setting `DecoupleHeldPublishRequests` to `false` restores the behavior where each held `Publish` occupies a worker for the duration of its wait; a server serving N sessions then needs `MaxRequestThreadCount` well above N to avoid starving other requests.

## Session diagnostics cost

Creating a session or subscription registers a diagnostics node and marks the live `SessionDiagnostics` and `SubscriptionDiagnostics` arrays for refresh. The arrays are rebuilt on demand when they are read or monitored, and the rebuild is throttled so that a burst of session creates does not trigger a rebuild per create. Servers that do not need the live session-diagnostics arrays can turn them off with the `DiagnosticsEnabled` server setting to remove the cost entirely.

## Configuration

The following settings size a single node for its hardware (see also the sizing notes in [Benchmarks.md](Benchmarks.md#server-session-scalability) and the admission-control settings in [RateLimiting.md](RateLimiting.md)):

- **`MaxSessionCount`** caps concurrently open sessions. Size **`MaxChannelCount`** (one channel per session) and **`MaxSubscriptionCount`** at or above the target session count.
- **`MaxRequestThreadCount`** caps concurrent request processing. With `DecoupleHeldPublishRequests` enabled it can be sized for the *active* (non-parked) request concurrency rather than the session count. **`MinRequestThreadCount`** pre-warms the pool so a connect burst is not throttled by thread-pool cold-start.
- **`DecoupleHeldPublishRequests`** (default `true`) releases a held `Publish`'s request-processing worker while it waits, so a small worker pool can hold many outstanding long-polls.
- **`MaxFailedAuthenticationAttempts`** (default 5; `0` disables) is the per-certificate brute-force lockout. A single-certificate client that opens many sessions can trip it on transient handshake failures and then be rejected with `BadUserAccessDenied`; raise or disable it for bulk-connect clients.
- **Stagger client connects.** Because establishment is CPU-bound but parallelizes across cores, throttling and staggering concurrent connects — rather than bursting them all at once — avoids self-inflicting a connect storm and is the most effective client-side measure.

## Scaling out

A single node's handshake throughput is bounded by its cores. To scale beyond one node, distribute connections across several server nodes behind a connection-distributing front end. The stack provides distributed mirroring of the address space, sessions, and subscriptions for exactly this high-availability and distributed topology.

## See also

- [Rate Limiting and Admission Control](RateLimiting.md) — the connection and session-establishment limiters, the `BadServerTooBusy` signalling, and the client's adaptive reconnect backoff.
- [Performance Benchmarks](Benchmarks.md#server-session-scalability) — the measured session-scaling table and the `ServerManySessionsLoadTestAsync` macro test.
- [Sessions, Reconnection, and Subscription Engines](Sessions.md) — session and subscription-engine architecture.
- [Subscriptions and Monitored Items Service Set](Subscriptions.md) — the subscription engine.
- [Diagnostics](Diagnostics.md) — server diagnostics nodes and how to control them.
