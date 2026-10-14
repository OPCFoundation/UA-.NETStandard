# Server session scalability

This document describes how the reference server scales to large numbers of concurrent client sessions on a single node, what bounds that scale, the built-in controls for degrading gracefully under load, and how to configure the server and scale out. For measured numbers see [Performance Benchmarks — Server session scalability](Benchmarks.md#server-session-scalability).

## Contents

- [How a session is established](#how-a-session-is-established)
- [What bounds single-node scale](#what-bounds-single-node-scale)
- [Admission control and rate limiting](#admission-control-and-rate-limiting)
- [Held Publishes and the request-thread budget](#held-publishes-and-the-request-thread-budget)
- [Inline request execution](#inline-request-execution)
- [Session diagnostics cost](#session-diagnostics-cost)
- [Garbage collector mode](#garbage-collector-mode)
- [Configuration](#configuration)
- [Scaling out](#scaling-out)
- [See also](#see-also)

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
- **Incomplete-message capacity**: a shared reassembly budget bounds retained chunks across listeners, leaves headroom for activated sessions, and releases abandoned messages at a fixed assembly deadline.
- **HTTPS/Kestrel rate limiting**: the HTTPS binding can attach an ASP.NET Core rate limiter through dependency injection.
- **Client-side adaptive backoff**: the client honors a server's *busy* signal — and any retry-after hint — with bounded exponential backoff, so a well-behaved client ramps its connects instead of hammering.

Session establishment keeps the CPU-bound signature work outside the session-table lock, so concurrent `CreateSession` calls are not serialized behind one signing operation.

## Held Publishes and the request-thread budget

A steady-state session keeps one or more long-poll `Publish` requests
outstanding. Each waits for the next notification. The number depends on the
client's publish-pipelining strategy; the classic subscription engine queues
several per session to smooth delivery.

The operating-system thread is released while a `Publish` waits. By default,
`DecoupleHeldPublishRequests` also releases the request-processing worker when
the request parks. Each parked request releases its worker, so a session with
several outstanding Publishes releases several workers. A small pool can
therefore support many thousands of parked Publishes without scaling
`MaxRequestThreadCount` to the number of sessions or Publishes.

Set `DecoupleHeldPublishRequests` to `false` to restore the legacy behavior.
Each held `Publish` then occupies a worker for the duration of its wait. A
server serving N sessions needs `MaxRequestThreadCount` well above N to avoid
starving other requests.

## Inline request execution

A request that arrives while no other request waits for a worker runs on the
thread that received it, when the isolation provider grants it execution
capacity at once. For UA TCP that thread is the receive loop of its secure
channel. The request runs up to its first asynchronous wait. The responses
of the requests that complete there are sent together once the channel has
worked through the chunks it already read. This saves handing each request
to a worker and its response back, which dominated the cost of small Reads
on a loaded server. In read benchmarks with 10 clients it raised throughput
by about a third to a half and cut CPU time per request by 40–50%.

It does not cost scalability across clients. Every channel has its own
receive loop, so requests of different channels still run in parallel, and a
receive loop that is busy with a request leaves the other channels alone.
Two limits apply to the requests of one channel:

- **Head-of-line blocking.** While a request runs synchronously, the channel
  reads no further request, so a slow request delays the ones the client
  pipelined behind it.
- **Less parallelism within one channel.** The pipelined requests of one
  channel run one after the other until each completes or waits, where
  workers would have run them concurrently.

Three controls keep this in check:

- **NodeManagers declare whether their calls may run inline**
  (`INodeManagerInlineExecution.SupportsInlineExecution`).
  `AsyncCustomNodeManager` and the generated and fluent NodeManagers do.
  `CustomNodeManager2`, whose callbacks are synchronous, does not. With the
  default `InlineRequestExecution` of `Auto`, a request runs inline only when
  every NodeManager it can reach declares support. Reads and Writes reach the
  NodeManagers of their namespaces. Browse, Call, HistoryRead and the
  MonitoredItem services reach all of them.
- **A time budget per channel** (`InlineRequestTimeBudget`, default 5 ms)
  bounds head-of-line blocking. When two requests in a row each hold the
  receive loop longer than the budget, the channel queues its requests for
  the workers for one second. While the overruns persist, the pause doubles
  up to about a minute. A single overrun, as from a garbage collection or the
  thread being descheduled, does not count. The same rule hands consistently
  long requests back to the workers, which run them in parallel again.
- **`InlineRequestExecution` set to `Disabled`** queues every request, as in
  1.5.x.

See [Requests that run on the thread that received them](MigrationGuide.md#requests-that-run-on-the-thread-that-received-them)
for what to check when migrating NodeManagers.

## Session diagnostics cost

Creating a session or subscription registers a diagnostics node and marks the live `SessionDiagnostics` and `SubscriptionDiagnostics` arrays for refresh. The arrays are rebuilt on demand when they are read or monitored, and the rebuild is throttled so that a burst of session creates does not trigger a rebuild per create. Servers that do not need the live session-diagnostics arrays can turn them off with the `DiagnosticsEnabled` server setting to remove the cost entirely.

## Garbage collector mode

A server allocates per request on many threads at once. With the default workstation garbage collector the heap's gen0 budget is a few megabytes, so a loaded server collects hundreds of times per second and every collection suspends all request threads. In a 20-session Read load (100 values per request, 24-core machine) the workstation collector paused the process for about half of the wall-clock time; enabling Server GC on the same build doubled the request rate (21k to 44k Reads per second) and cut the 90th-percentile latency from 2.1 ms to 0.6 ms, at the price of a larger working set (about 430 MB instead of 210 MB).

Enable Server GC in the server's project file:

```xml
<PropertyGroup>
  <ServerGarbageCollection>true</ServerGarbageCollection>
</PropertyGroup>
```

or set `DOTNET_gcServer=1` in the environment. On .NET 8 and later, Server GC adapts its heap count to the load (DATAS), so idle servers stay small. The console reference server enables it.

## Configuration

The following settings size a single node for its hardware (see also the sizing notes in [Benchmarks.md](Benchmarks.md#server-session-scalability) and the admission-control settings in [RateLimiting.md](RateLimiting.md)):

- **`MaxSessionCount`** caps concurrently open sessions. Size **`MaxChannelCount`** (one channel per session) and **`MaxSubscriptionCount`** at or above the target session count.
- **`MaxRequestThreadCount`** caps concurrent request processing. With `DecoupleHeldPublishRequests` enabled it can be sized for the *active* (non-parked) request concurrency rather than the session count. **`MinRequestThreadCount`** pre-warms the pool so a connect burst is not throttled by thread-pool cold-start.
- **`MaxQueuedRequestCount`** is how many decoded requests established sessions can have waiting for a worker. Size it at or above the total pipelining of your clients (for example 10 clients with 1,000 outstanding requests each need 10,000). The reserved queue entries of the Balanced and TrustedReservations isolation profiles are added on top; see [request and caller limits](ResourceIsolation.md#configuring-request-and-caller-limits).
- **`DecoupleHeldPublishRequests`** (default `true`) releases a held `Publish`'s request-processing worker while it waits, so a small worker pool can hold many outstanding long-polls.
- **`InlineRequestExecution`** (default `Auto`) runs a request on the receive loop of its channel when every NodeManager it can reach supports that; `Enabled` ignores the NodeManagers' declarations, `Disabled` queues every request. **`InlineRequestTimeBudget`** (default 5 ms, `0` turns it off) is how long inline requests may hold a channel's receive loop before the channel queues its requests for a while. See [Inline request execution](#inline-request-execution).
- **`MaxMonitoredItemCount`** and **`MaxMonitoredItemsPerSubscription`** (default `0`, no limit) cap the monitored items in the server and in one subscription. `CreateMonitoredItems` answers `Bad_TooManyMonitoredItems` for the items over the limit, and the values are published in `ServerCapabilities.MaxMonitoredItems` and `MaxMonitoredItemsPerSubscription`. Set them on servers reachable by untrusted clients; `MaxMonitoredItemsPerCall` only bounds a single request.
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
