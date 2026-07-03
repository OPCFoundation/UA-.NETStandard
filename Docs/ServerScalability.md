# Server session scalability — boundaries and how to move beyond them

This document explains what limits the reference server's **concurrent-session** scale on a single node, where the boundaries are in the code, and a prioritized roadmap for moving beyond them. It is the architectural companion to the measured numbers in [Performance Benchmarks — Server session scalability](Benchmarks.md#server-session-scalability).

## TL;DR

- On a 6-core developer machine the server establishes **~1500 concurrent sessions cleanly** and delivers 100 % of steady-state notifications; beyond that it hits a wall at **~2000**.
- **That wall is an *establishment* (connect-storm) ceiling, not a steady-state one.** Once sessions are up, the async publish path keeps delivering; the bottleneck is the CPU-bound secure-channel handshake burst plus a positive-feedback retry loop.
- The single most effective lever to raise and *stabilize* the ceiling is **explicit admission control / rate limiting** at connection accept and session establishment, so the server sheds load with a fast deterministic rejection instead of aborting mid-handshake and inviting client retries.
- Beyond a single node's core count the RSA handshake cost is fundamental: scale **out** using the distributed mirroring already in the stack, rather than only up.

## The request lifecycle (where each stage can bottleneck)

A client session is brought up in four stages, each with its own scaling boundary:

1. **TCP accept** — the OS accepts the socket and the listener creates a channel object.
2. **Secure channel** — `OpenSecureChannel` performs the asymmetric (RSA) handshake: decrypt + signature-verify with the server certificate's private key, generate nonces.
3. **CreateSession** — admission checks (session/channel limits), server-nonce generation and signing, per-session diagnostics node creation.
4. **ActivateSession** — verify the client signature and the user identity token, activate the session.

After activation the session enters **steady state**: it keeps one long-polled `Publish` request outstanding and receives notifications as the subscription's monitored items change.

## Why 2000 collapses: the connect-storm feedback loop

The failure at 2000 is not a hard limit that returns a clean error — it is a *positive-feedback collapse*:

> overload → a handshake times out or is aborted → the client **retries** → the retry creates a **new** server session while the previous one is orphaned → more load → more aborts.

In a 2000-session run on a 6-core box the server created **~4432 sessions** for the 2000 target (2.2× — the duplicates are retries) and abandoned **~31,779 subscriptions**. The extra sessions are pure waste that competes for the same CPU as the useful work, so the server never converges on the full set within the connect budget. Fixing the collapse is therefore less about a single hard limit and more about **breaking the feedback loop** with backpressure.

## Establishment boundaries (the current ceiling)

### B1 — The listener socket backlog is hard-coded to 10

`TcpTransportListener` calls `Listen(kSocketBacklog)` with `kSocketBacklog = 10` (`Stack/Opc.Ua.Core/Stack/Tcp/TcpTransportListener.cs`, `kSocketBacklog` constant near line 261, applied in the listener start paths). A burst of 2000 simultaneous connects instantly overflows a backlog of 10, so the OS drops or resets connections before the accept loop ever sees them — which the client experiences as a transport failure and retries. This is the first wall and is not configurable today.

### B2 — Every transient handshake error becomes `BadTcpInternalError`, which triggers a client retry

The secure-channel receive path maps *any* exception during the Hello or `OpenSecureChannel` processing to `BadTcpInternalError`:

- `TcpServerChannel.ProcessHelloMessage` and `ProcessOpenSecureChannelRequest` (`Stack/Opc.Ua.Core/Stack/Tcp/TcpServerChannel.cs`, faults at lines 589, 895).
- The base receive/write loop in `UaSCBinaryChannel` (`.../UaSCBinaryChannel.cs`, faults at lines 581, 760, 840, 881).

Under load a transient decrypt/parse/IO hiccup aborts the channel, the client retries, and the retry creates a duplicate server session. This catch-all is the amplifier that turns a 2000 target into 4432 creates. Post-#3930 (caller-owned certificate handles) the previously dominant private-key handle race is largely gone — a 2000 run now shows a *single* `BadTcpInternalError` rather than tens of thousands — so the remaining aborts are increasingly timeout/backpressure driven rather than crypto-handle driven.

### B3 — Session-diagnostics creation is globally serialized and forces an O(N) rescan per create

Creating a session adds a per-session diagnostics node under the **global address-space semaphore** and then forces a full diagnostics rescan:

- `DiagnosticsNodeManager.CreateSessionDiagnosticsAsync` runs under `m_modifyAddressSpaceSemaphoreSlim` and sets `m_forceDiagnosticsScan = true` (`Libraries/Opc.Ua.Server/Diagnostics/DiagnosticsNodeManager.cs`, around lines 903–1014).
- `DoScan` (same file, from line 1820) rebuilds the full `SessionDiagnosticsDataType[N]` and `SessionSecurityDiagnosticsDataType[N]` arrays over **all** sessions (lines 1843–1875).
- The `m_forceDiagnosticsScan` flag *defeats* the 1-second scan throttle that would otherwise coalesce updates (checks at lines 1710–1715 and 1736–1742).

So N concurrent creates each trigger an O(N) rebuild → **~O(N²)** diagnostics work during a bulk connect, performed under a global lock that also serializes address-space mutation. This is gated by `DiagnosticsEnabled`: servers that do not need the live session-diagnostics arrays can disable them to remove this cost entirely.

### B4 — CreateSession holds a global semaphore across admission *and* RSA signing

`SessionManager.CreateSessionAsync` takes `m_semaphoreSlim` and holds it across the `MaxSessionCount` check (→ `BadTooManySessions`), nonce-uniqueness check, token/nonce generation, `Session` construction, and the table reservation (`Libraries/Opc.Ua.Server/Session/SessionManager.cs`, roughly lines 218–320). `StandardServer.CreateSessionAsync` additionally performs the CPU-bound **server-signature RSA** inside its own `m_semaphoreSlim` region (`Libraries/Opc.Ua.Server/Server/StandardServer.cs`, around lines 504–535).

`ActivateSession` already demonstrates the better pattern — it keeps the CPU-bound *client-signature verify* **outside** the semaphore (`SessionManager.cs`, note around lines 443–447) — but `CreateSession` does its crypto inside the lock, so concurrent creates largely serialize behind one signing operation at a time.

### B5 — Establishment crypto is CPU-bound and competes with steady state on the same cores

Each connect costs, cumulatively: an RSA decrypt of the `OpenSecureChannel` request using the server private key (`Stack/Opc.Ua.Core/Stack/Tcp/UaSCBinaryChannel.Rsa.cs`, `GetRSAPrivateKey()` at line 118), a client certificate-chain validation (`StandardServer.cs`, around lines 349–405), the server-nonce signature, and on activate the client-signature verify. Handshakes run per-channel on the thread pool, so they parallelize across cores — but on 6 cores a 2000-connect burst saturates every core. That starves the steady-state publish sweep and the servicing of held `Publish` requests, so keep-alives are missed and subscriptions drift to *expired/abandoned* (the ~31,779 abandonments). **CPU is the fundamental single-node ceiling**, and it is why establishment (not delivery) is the wall on modest hardware.

## Steady-state boundaries (the next wall, once establishment is fixed)

### S1 — Held Publishes are async-parked, but the request-queue accounting couples the worker/thread budget to session count

A common assumption is "one blocked thread per held `Publish`". That is **not** what happens: when no subscription is ready, `SessionPublishQueue.PublishAsync` returns a `TaskCompletionSource`-backed task (`Libraries/Opc.Ua.Server/Subscription/SessionPublishQueue.cs`, lines 114–156) and the worker `await`s it — the OS thread is released, not spinning.

However, the request-queue **accounting** still couples the worker budget to the number of concurrently-held Publishes. In `ServerBase.RequestQueue.WorkerLoopAsync` the active-worker counter is incremented for the *entire* duration of the awaited request, including the whole long-poll wait (`Stack/Opc.Ua.Core/Stack/Server/ServerBase.RequestQueue.cs`, lines 203–214). `ScheduleIncomingRequest` spawns additional worker loops up to `MaxRequestThreadCount` whenever the active count reaches the current worker count (lines 174–186), and the queue constructor raises the process-wide `ThreadPool` maximum to `MaxRequestThreadCount` (lines 84–89).

The practical consequence: to serve N sessions you must size `MaxRequestThreadCount ≥ N` and the process `ThreadPool` ceiling balloons accordingly (the load test sets 10500 for a 10000-session case). A parked long-poll should not count against the active-worker budget — decoupling it would let a small worker pool hold many thousands of outstanding Publishes.

### S2 — Per-session outstanding-Publish cap

Each session's `SessionPublishQueue` rejects additional outstanding requests once the queue reaches `MaxPublishRequestCount`, returning `BadTooManyPublishRequests` (`SessionPublishQueue.cs`, lines 136–155). This is a correct per-session guard, but it must be sized for the client's publish-pipelining depth.

### S3 — The publish sweep is O(subscriptions) per cycle

`SubscriptionManager.PublishSubscriptionsAsync` snapshots the session queues and sweeps them each cycle, using `Parallel.For` above a threshold and a serial loop below it (`Libraries/Opc.Ua.Server/Subscription/SubscriptionManager.cs`, around lines 2066–2101). The work is O(subscriptions) per cycle and parallelizes across cores without a manager-wide lock, so it scales with cores — but it is still per-cycle O(N) and shares cores with establishment crypto (B5).

### S4 — The ABANDONED path is a symptom, not an independent limit

A subscription expires when its lifetime counter reaches the maximum while it is still waiting to publish (`Subscription.cs`, around lines 503–543 and 841–860; cleanup in `SubscriptionManager.cs`, around lines 2103–2133). Mass abandonment during a connect storm is a *downstream effect* of B5/S1 CPU starvation — the publishes are not serviced in time — rather than a boundary to fix on its own.

## Where to linearize: admission control and rate limiting

> **Implemented** — see [Rate Limiting and Admission Control](RateLimiting.md). B1 (configurable backlog, default 512), B2 (a connection rate limiter plus a session-establishment concurrency limiter returning `BadServerTooBusy`), and B4 (CreateSession RSA signing moved out of the establishment lock) are done and on by default with conservative limits, together with the HTTPS/Kestrel `AddHttpsRateLimiter` DI injection, a client-side server-signal-aware adaptive backoff, and a client-wide connect admission gate. A structured retry-after hint in the response header remains a follow-up.

Because the collapse is a positive-feedback loop, the highest-leverage fix is **explicit backpressure** so the server degrades gracefully — a fast, deterministic rejection the client can honor — instead of aborting mid-handshake and inviting a retry that doubles the load:

1. **Connection-admission rate limit at the listener** (token bucket) plus a larger, configurable socket backlog. Bound the rate of new secure-channel handshakes to what the RSA-handshake CPU can absorb; hold or fast-reject the excess with a `BadServerTooBusy`/Retry-After rather than letting it fail deep in the handshake. Addresses B1, B2, B5.
2. **Bounded session-establishment admission**, sized to core count, that returns a *fast deterministic* `BadTooManySessions` / `BadServerTooBusy` (ideally with a Retry-After hint) so a client backs off instead of retrying into duplicate sessions. Addresses B2, B4.
3. **Move crypto out of the establishment locks** so the global semaphore only guards the table reservation, not the RSA signing (mirror what ActivateSession already does). Addresses B4.
4. **Coalesce or throttle the diagnostics rescan** — honor the existing 1-second throttle under bulk connect, make the array update incremental (append one entry) instead of a full O(N) rebuild, and/or allow disabling session diagnostics under load. Addresses B3.
5. **Decouple held Publishes from the worker/thread accounting** so a parked long-poll does not consume a worker slot and the `ThreadPool` ceiling does not scale with session count. Addresses S1.
6. **Client-side backoff / honor Retry-After** so a well-behaved client ramps its connects instead of hammering, which linearizes the whole system end-to-end.

The unifying principle: **linearize the *admission* of expensive work (handshakes, session creation) to the available CPU, and give overflow an immediate, honest "busy" answer** — never a slow, ambiguous abort that a client will retry.

## Roadmap

The mitigations group into three phases of increasing effort:

- **Option A — Quick wins (raise the ceiling on existing hardware).** Make the socket backlog configurable and larger (B1); move the server-nonce RSA signing out of the CreateSession semaphores (B4); coalesce/short-circuit the forced diagnostics rescan and allow disabling session diagnostics under load (B3). Low risk; directly attacks the storm.
- **Option B — Admission control / rate limiting (graceful degradation).** A connection rate-limiter at accept plus bounded session-establishment admission with a fast `BadServerTooBusy` and Retry-After, and an audit of the `BadTcpInternalError` catch-alls to avoid spurious aborts (B2). Turns the collapse into backpressure and lets clients ramp without creating duplicate sessions.
- **Option C — Steady-state scaling (well beyond a few thousand).** Decouple held Publishes from the worker-count accounting (S1) and make the diagnostics update incremental, so a small worker pool holds many thousands of long-polls. Needed to go far past the establishment ceiling once A and B land.

Beyond these, **horizontal scale-out** is the architectural answer: a single node's RSA-handshake throughput is bounded by its cores, so scale out across nodes behind a connection-distributing front. The stack already provides distributed mirroring of the address space, sessions, and subscriptions for exactly this high-availability / distributed topology.

## Configuration knobs available today

Until the above land, the following existing settings let you push a single node as far as its hardware allows (see also the sizing notes in [Benchmarks.md](Benchmarks.md#server-session-scalability)):

- **`MaxSessionCount`** (default 100) caps concurrently open sessions. Size **`MaxChannelCount`** (one channel per session) and **`MaxSubscriptionCount`** above the target session count.
- **`MaxRequestThreadCount`** caps concurrent request processing; because a held `Publish` occupies a worker slot for its wait (S1), a server serving N sessions needs `MaxRequestThreadCount` well above N or held Publishes starve other services with `BadRequestTimeout`. **`MinRequestThreadCount`** pre-warms the pool so a connect burst is not throttled by thread-pool cold-start.
- **`MaxFailedAuthenticationAttempts`** (default 5; `0` disables) is the per-certificate brute-force lockout; a single-certificate client opening many sessions can trip it on transient handshake failures and then be rejected with `BadUserAccessDenied`. Disable or raise it for bulk-connect clients.
- **Stagger the client connects.** Establishment is CPU-bound but parallelizes across cores; throttling and staggering concurrent connects (rather than bursting all at once) avoids self-inflicting the storm and is the single most effective client-side mitigation today.

## See also

- [Performance Benchmarks](Benchmarks.md#server-session-scalability) — the measured 500 / 1000 / 1500 / 2000 scaling table and the `ServerManySessionsLoadTestAsync` macro test.
- [Sessions, Reconnection, and Subscription Engines](Sessions.md) — session and subscription-engine architecture.
- [Subscriptions and Monitored Items Service Set](Subscriptions.md) — the V2 subscription engine.
- [Diagnostics](Diagnostics.md) — server diagnostics nodes (the source of the B3 cost) and how to control them.
