# Server session scalability

This document describes how the reference server scales to large numbers of concurrent client sessions on a single node, what bounds that scale, the built-in controls for degrading gracefully under load, the runtime and host settings for a server under load, and how to configure the server and scale out. For measured numbers see [Performance Benchmarks — Server session scalability](Benchmarks.md#server-session-scalability).

## Contents

- [How a session is established](#how-a-session-is-established)
- [What bounds single-node scale](#what-bounds-single-node-scale)
- [Admission control and rate limiting](#admission-control-and-rate-limiting)
- [Held Publishes and the request-thread budget](#held-publishes-and-the-request-thread-budget)
- [Session diagnostics cost](#session-diagnostics-cost)
- [Runtime and host settings](#runtime-and-host-settings)
  - [Garbage collector mode](#garbage-collector-mode)
  - [Dynamic GC heap count (DATAS)](#dynamic-gc-heap-count-datas)
  - [Hybrid CPUs and processor affinity](#hybrid-cpus-and-processor-affinity)
  - [JIT warm-up](#jit-warm-up)
- [Configuration](#configuration)
  - [Request workers and the request queue](#request-workers-and-the-request-queue)
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

## Session diagnostics cost

Creating a session or subscription registers a diagnostics node and marks the live `SessionDiagnostics` and `SubscriptionDiagnostics` arrays for refresh. The arrays are rebuilt on demand when they are read or monitored, and the rebuild is throttled so that a burst of session creates does not trigger a rebuild per create. Servers that do not need the live session-diagnostics arrays can turn them off with the `DiagnosticsEnabled` server setting to remove the cost entirely.

## Runtime and host settings

The .NET runtime settings below are read once, at process start. They are set in the server's project file (which writes them to `<app>.runtimeconfig.json`) or as `DOTNET_` environment variables; see the .NET [garbage collector](https://learn.microsoft.com/dotnet/core/runtime-config/garbage-collector) and [compilation](https://learn.microsoft.com/dotnet/core/runtime-config/compilation) configuration references. The measurements quoted here come from one developer machine (AMD Ryzen AI 9 HX 370, 12 cores / 24 logical processors, Windows 11, .NET 10) and the [server load harness](../perf/README.md); measure your own deployment before adopting a setting.

### Garbage collector mode

A server allocates per request on many threads at once. With the default workstation garbage collector the heap's gen0 budget is a few megabytes, so a loaded server collects hundreds of times per second and every collection suspends all request threads. With deep request pipelines the workstation collector paused the process for about half of the wall-clock time. In a 20-session Read load (100 values per request, 24 logical processors) enabling Server GC on the same build doubled the request rate (21k to 44k Reads per second) and cut the 90th-percentile latency from 2.1 ms to 0.6 ms, at the price of a larger working set (about 430 MB instead of 210 MB).

Use Server GC for servers under load. Enable it in the server's project file:

```xml
<PropertyGroup>
  <ServerGarbageCollection>true</ServerGarbageCollection>
</PropertyGroup>
```

or set `DOTNET_gcServer=1` in the environment. The console reference server enables it.

### Dynamic GC heap count (DATAS)

From .NET 9, Server GC by default adapts the number of GC heaps to the load (DATAS, *dynamic adaptation to application sizes*): a process starts with one heap and adds heaps after a few collections when the GC cost exceeds its target, so an idle server stays small. While the heap count changes, and while one heap serves many allocating threads, the collection rate and the pause pattern change with it.

For benchmarks and for latency-sensitive deployments, disabling DATAS gives steadier GC behaviour: Server GC then keeps one heap per logical processor the process may use from the start.

```xml
<PropertyGroup>
  <ServerGarbageCollection>true</ServerGarbageCollection>
  <GarbageCollectionAdaptationMode>0</GarbageCollectionAdaptationMode>
</PropertyGroup>
```

or `DOTNET_GCDynamicAdaptationMode=0`. The trade-off is memory: every heap has its own gen0 budget, so the working set is larger, also when the server is idle. `DOTNET_GCHeapCount` limits the number of heaps when that is too much.

### Hybrid CPUs and processor affinity

On hybrid processors (Intel performance and efficiency cores, AMD Zen 5 and Zen 5c cores) the thread pool and the GC spread a server's work over every core, and the same request costs more CPU time on an efficiency core. On the Ryzen AI 9 HX 370, a Read served on a Zen 5c core took 1.37 times the CPU time of one served on a Zen 5 core.

Restricting the server process to the performance cores can therefore cost less CPU for the same work. In one measurement (2026-10-15, 10 clients with 100 Reads each in flight) the server restricted to the performance cores reached the same throughput as on the whole machine at about half the CPU time. This depends on the workload, the processor and the other processes on the machine, so treat it as an option to measure, not a rule.

Set the affinity when the process starts, so the runtime sizes the thread pool and the Server GC heaps for the processors in the mask (`Environment.ProcessorCount` honours the affinity the process starts with):

```bat
rem Windows: hexadecimal mask of the logical processors, here 0 to 7
start /affinity FF MyServer.exe
```

```bash
# Linux
taskset -c 0-7 ./MyServer
```

When the affinity is changed after the process started, for example through `Process.ProcessorAffinity`, set `DOTNET_PROCESSOR_COUNT` to the number of processors in the mask as well; otherwise the runtime keeps the sizing for all processors of the machine. Which logical processors belong to the performance cores depends on the processor and the operating system: check the core type with a tool such as Sysinternals Coreinfo on Windows or `lscpu --extended` (maximum frequency per CPU) on Linux.

### JIT warm-up

.NET compiles methods first without optimization (tier 0) and recompiles the frequently called ones with full optimization and profile-guided optimization in the background. A server measured for a few seconds after start therefore runs largely tier-0 code. With the external open62541 benchmark client of [o6-automation/opcua-benchmarks](https://github.com/o6-automation/opcua-benchmarks) (10 clients with 100 single-value Reads each in flight, server restricted to 4 logical processors) the server used about 28–31 µs of CPU time per Read in 3-second runs and 8.8 µs at steady state. At steady state the .NET server is close to open62541's single-threaded server on the same machine (8.1 µs), and with Basic256Sha256 SignAndEncrypt it costs less (8.8 µs against 12.2 µs).

The benchmark's default parameters (10,000 warm-up and 100,000 measured calls per client) are too short for a .NET server on many cores: on 24 logical processors the measured window still includes JIT warm-up (about 60 µs per Read against 28 µs at steady state). For measurements and for deployments where the first minutes matter:

- Warm up before measuring: run the load until the per-request cost stops falling (for the benchmark above, 100,000 warm-up calls per client), then measure.
- Publish with ReadyToRun (`<PublishReadyToRun>true</PublishReadyToRun>`) so that start-up code begins precompiled instead of at tier 0; tiered compilation still re-optimizes the hot methods.
- `TieredPGO` (on by default since .NET 8) and `TieredCompilation` can be set in the project file (`<TieredPGO>`, `<TieredCompilation>`) or as `DOTNET_TieredPGO` and `DOTNET_TieredCompilation`. Disabling tiered compilation compiles every method fully optimized at first call, which removes the warm-up phase but loses the profile-guided optimization and makes start-up slower; measure both settings at steady state before changing them.

## Configuration

The following settings size a single node for its hardware (see also the sizing notes in [Benchmarks.md](Benchmarks.md#server-session-scalability) and the admission-control settings in [RateLimiting.md](RateLimiting.md)):

- **`MaxSessionCount`** caps concurrently open sessions. Size **`MaxChannelCount`** (one channel per session) and **`MaxSubscriptionCount`** at or above the target session count.
- **`MaxRequestThreadCount`**, **`MinRequestThreadCount`** and **`MaxQueuedRequestCount`** size the request workers and the request queue; see [Request workers and the request queue](#request-workers-and-the-request-queue).
- **`DecoupleHeldPublishRequests`** (default `true`) releases a held `Publish`'s request-processing worker while it waits, so a small worker pool can hold many outstanding long-polls.
- **`MaxMonitoredItemCount`** and **`MaxMonitoredItemsPerSubscription`** (default `0`, no limit) cap the monitored items in the server and in one subscription. `CreateMonitoredItems` answers `Bad_TooManyMonitoredItems` for the items over the limit, and the values are published in `ServerCapabilities.MaxMonitoredItems` and `MaxMonitoredItemsPerSubscription`. Set them on servers reachable by untrusted clients; `MaxMonitoredItemsPerCall` only bounds a single request.
- **`MaxFailedAuthenticationAttempts`** (default 5; `0` disables) is the per-certificate brute-force lockout. A single-certificate client that opens many sessions can trip it on transient handshake failures and then be rejected with `BadUserAccessDenied`; raise or disable it for bulk-connect clients.
- **Stagger client connects.** Because establishment is CPU-bound but parallelizes across cores, throttling and staggering concurrent connects — rather than bursting them all at once — avoids self-inflicting a connect storm and is the most effective client-side measure.

### Request workers and the request queue

A decoded request waits in the server's request queue until a request worker takes it. Workers are asynchronous loops on the .NET thread pool, not dedicated threads; a worker counts as busy while it executes a request and, with `DecoupleHeldPublishRequests`, is released when a `Publish` parks. With the FairShare, Balanced (default) or TrustedReservations [isolation profile](ResourceIsolation.md) the queue serves the callers in weighted turns; with SharedOnly it is one FIFO.

| Setting | Configuration default | Current behaviour |
|---|---|---|
| `MinRequestThreadCount` | 10 | Workers started with the server (at least 1). The server also raises the thread pool's minimum worker and I/O threads to this value, so up to this many threads are created without the thread pool's injection delay. |
| `MaxRequestThreadCount` | 100 | Upper bound of workers (values below 100, or below `MinRequestThreadCount`, are raised). A worker is added only when a request is queued and every worker is busy; workers are not retired. The thread pool's maximum is raised to at least this value. |
| `MaxQueuedRequestCount` | 200 | Requests of established sessions that can wait for a worker (values below 100 are raised to 100). A request that finds the queue full is answered with `BadServerTooBusy`. |

Guidance:

- **Size `MaxQueuedRequestCount` for the pipelining of all clients**: the sum over the clients of the requests each keeps outstanding. For example, 10 clients with 1,000 outstanding Reads each need 10,000. With the Balanced and TrustedReservations profiles the protected bootstrap, reconnect and control entries (one each by default) and the floors of trusted owners are added on top of the configured value, and held Publishes are counted in a separate stage of the same size; see [request and caller limits](ResourceIsolation.md#configuring-request-and-caller-limits). Releases up to 2.0.1 carved the three protected entries out of the configured value, so there size it at clients × outstanding requests + 3.
- **`MaxRequestThreadCount` bounds concurrent request execution, not threads.** Request handling is asynchronous, so a few busy workers per core are enough for in-memory node managers; the default 100 rarely needs raising. Raise it when node managers block a worker for I/O or locks (synchronous `CustomNodeManager2` code waiting on a device), or when `DecoupleHeldPublishRequests` is off.
- **Keep `MinRequestThreadCount` moderate.** It pre-warms the pool for connect bursts, but because it also sets the thread pool's minimum, a value far above the processor count lets the pool run many more threads than processors under load. On shared or pinned processors, a preempted thread then delays every GC suspension until it runs again.
- `BadServerTooBusy` responses to established sessions while the processors are not saturated mean that an admission limit, not the CPU, is the limit. With an isolation profile that is not necessarily the queue count: the retained request bytes, the parked-request stage or a per-owner limit reject requests the same way while queue entries are free. Check the `opcua.server.isolation.rejections` metric (its `stage` and `reason` labels) and `opcua.server.isolation.usage` before raising `MaxQueuedRequestCount`; see [transport behavior and overload signals](ResourceIsolation.md#transport-behavior-and-overload-signals).

## Scaling out

A single node's handshake throughput is bounded by its cores. To scale beyond one node, distribute connections across several server nodes behind a connection-distributing front end. The stack provides distributed mirroring of the address space, sessions, and subscriptions for exactly this high-availability and distributed topology.

## See also

- [Rate Limiting and Admission Control](RateLimiting.md) — the connection and session-establishment limiters, the `BadServerTooBusy` signalling, and the client's adaptive reconnect backoff.
- [Performance Benchmarks](Benchmarks.md#server-session-scalability) — the measured session-scaling table and the `ServerManySessionsLoadTestAsync` macro test.
- [Sessions, Reconnection, and Subscription Engines](Sessions.md) — session and subscription-engine architecture.
- [Subscriptions and Monitored Items Service Set](Subscriptions.md) — the subscription engine.
- [Diagnostics](Diagnostics.md) — server diagnostics nodes and how to control them.
