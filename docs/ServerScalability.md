# Server session scalability

Use this guide to size a reference server, limit overload, and configure its runtime and host. The [session-scaling measurements](Benchmarks.md#server-session-scalability) record the tested workloads and hardware.

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
  - [Small Windows machines and thread priority boost](#small-windows-machines-and-thread-priority-boost)
  - [JIT warm-up](#jit-warm-up)
  - [Verifying the effective configuration](#verifying-the-effective-configuration)
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

**Session establishment can be CPU-bound.** The historical `Basic256Sha256` load test established about **4,000 sessions** on a six-core Xeon W-2235 with client and server sharing the machine; RSA handshakes dominated establishment time. See the [measured session-scaling results](Benchmarks.md#server-session-scalability) for the configuration and failed 10,000-session case.

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

A server allocates per request on many threads at once. In the [measured GC comparison](Benchmarks.md#garbage-collector-modes) (2026-10-21, 24 logical processors, 5 rounds per mode), Workstation GC paused the server for 30–46 % of the measurement window. In a 20-session Read load (100 values per request), Server GC on the same build increased the request rate from 45.6k to 66.1k Reads per second and cut the 99th-percentile latency from 1.8 ms to 1.15 ms; the working set rose from about 210 MB to 325 MB. With the native Read workers the rate rose from 385k to 661k Reads per second, and the event rate of 50 sessions × 15 event subscriptions from 44 to 83 events per second.

Use Server GC for servers under load. Enable it in the server's project file:

```xml
<PropertyGroup>
  <ServerGarbageCollection>true</ServerGarbageCollection>
</PropertyGroup>
```

or set `DOTNET_gcServer=1` in the environment. The console reference server enables it.

### Dynamic GC heap count (DATAS)

From .NET 9, Server GC by default adapts the number of GC heaps to the load (DATAS, *dynamic adaptation to application sizes*): a process starts with one heap and adds heaps after a few collections when the GC cost exceeds its target, so an idle server stays small. On .NET 8, DATAS is opt-in with `DOTNET_GCDynamicAdaptationMode=1`. While the heap count changes, and while one heap serves many allocating threads, the collection rate and the pause pattern change with it.

Disabling DATAS removes heap-count adaptation from the measurement: Server GC starts with a fixed heap count, normally one per available logical processor unless configured otherwise. Compare pause times and memory use before choosing it for a latency-sensitive deployment.

```xml
<PropertyGroup>
  <ServerGarbageCollection>true</ServerGarbageCollection>
  <GarbageCollectionAdaptationMode>0</GarbageCollectionAdaptationMode>
</PropertyGroup>
```

or `DOTNET_GCDynamicAdaptationMode=0`. The trade-off is memory: every heap has its own gen0 budget, so the working set is larger, also when the server is idle. `DOTNET_GCHeapCount` limits the number of heaps when that is too much. In the [measured comparison](Benchmarks.md#garbage-collector-modes) on 24 logical processors, disabling DATAS cut the GC pause time of the window 4.6–8 times at 1.9–2.8 times the working set (for example 803 MB to 2 GB with 750 event monitored items); throughput and CPU per operation stayed within the ranges measured with DATAS.

### Hybrid CPUs and processor affinity

On hybrid processors (Intel performance and efficiency cores, AMD Zen 5 and Zen 5c cores), work can run on either core type. In the reported Ryzen AI 9 HX 370 measurement, a Read served on a Zen 5c core took 1.37 times the CPU time of one served on a Zen 5 core.

Restricting the server process to the performance cores can therefore cost less CPU for the same work. In the [measured placement comparison](Benchmarks.md#processor-placement) (2026-10-21, 10 clients with 100 Reads each in flight, 5 rounds) the server restricted to the eight Zen 5 logical processors reached a median throughput within 7 % of the default placement at about half the CPU per Read (11.7 against 22.9 µs; with SignAndEncrypt 14.6 against 24.5 µs at the same throughput). This depends on the workload, the processor and the other processes on the machine, so treat it as an option to measure, not a rule.

Thread hand-offs between core complexes (CCX) added cost in the tested configuration. On this Ryzen AI 9 HX 370, CCX0 held the 4 Zen 5 cores (logical processors 0–7) and CCX1 the 8 Zen 5c cores (logical processors 8–23). An ETW analysis on 2026-10-20 (`master` 014f99a53, the external open62541 benchmark client, 10 clients with 100 single-value Reads each in flight, steady state) recorded:

| Server affinity | Logical processors | Server CPU / Read | Throughput |
|---|---|--:|--:|
| `0xFF` | 8, Zen 5 (CCX0) | 15.1 µs | 507k req/s |
| `0xFFFF00` | 16, Zen 5c (CCX1), clients on Zen 5 | 19.6 µs | 740k req/s |
| `0xFFFF` | 16, across both complexes | 24.5 µs | 586k req/s |
| none | 24, across both complexes | 27.5 µs | 550k req/s |

Within one complex, CPU per Read differed roughly by the core-type ratio. Using both complexes increased CPU per Read without consistently increasing throughput. The trace attributed most of that cost to cross-complex hand-offs: from the receive loop to a queue worker, from that worker to the writer, during kernel loopback sends, and when GC threads joined a collection. Compared with the server pinned to 4 logical processors of one complex, on all 24 the loopback send rose from 0.7 to 4.1 µs per request.

If measurements favor one complex and it can handle the load, start the server with that complex's affinity mask and match the runtime's processor count to it, as shown below. Identify the topology on the deployment host before choosing a mask; these processor numbers belong to the tested machine. Intel performance/efficiency-core configurations were not measured here.

Set the affinity when the process starts, so the runtime sizes the thread pool and the Server GC heaps for the processors in the mask (`Environment.ProcessorCount` honours the affinity the process starts with):

```bat
rem Windows: hexadecimal mask of the logical processors, here 0 to 7
start /affinity FF MyServer.exe
```

```bash
# Linux
taskset -c 0-7 ./MyServer
```

If a launcher changes affinity after startup, for example through `Process.ProcessorAffinity`, set `DOTNET_PROCESSOR_COUNT` in the child's environment **before launching it** to match the intended mask. Changing that variable after startup does not resize the running runtime. The variable controls the runtime's processor count, not OS affinity; apply the mask separately. Identify the logical processors belonging to each core type with tools such as Sysinternals Coreinfo on Windows or `lscpu --extended` on Linux.

### Small Windows machines and thread priority boost

On a Windows host with one or two processors, such as a small virtual machine, a container limited to one or two CPUs, or a pinned process, Read latency can depend more on thread scheduling inside the server than on the work itself. An ETW wait analysis on 2026-10-20 (`master` a10f9e252, server restricted to one logical processor with `DOTNET_PROCESSOR_COUNT=1`, Server GC, 20 sessions reading with one request in flight each) recorded 99.9th-percentile latencies of 32–56 ms for single-value Reads and 117–260 ms for Reads of 100 values; single Reads took up to about 700 ms. The request path does not sleep, delay or back off. The waits came from scheduling:

- Windows temporarily raises the priority of a thread released from a wait (*dynamic priority boost*). A thread woken by an I/O completion or a semaphore therefore preempted the thread that was processing a request.
- Idle thread-pool workers spin before they block and use processor time that the request needed.
- The thread pool's starvation detection then added workers (45–54 threads instead of about 5), which increased the contention.
- The waits fell on multiples of the 15.6 ms Windows clock interval.

Disabling priority boost reduced tail latency in this setup. Two rounds per configuration gave:

| Values per Read | Priority boost | p99.9 latency | Throughput |
|---|---|--:|--:|
| 1 | on (Windows default) | 32–56 ms | 27k–42k req/s |
| 1 | off | 3.0–3.3 ms | 61k–73k req/s |
| 100 | on (Windows default) | 117–260 ms | 2.8k–6.9k req/s |
| 100 | off | 11–26 ms | 5.4k–10.7k req/s |

With the boost off, the maximum single-value latency was 16–34 ms. Also setting `DOTNET_ThreadPool_UnfairSemaphoreSpinLimit=0`, so that idle thread-pool workers block without spinning, lowered the single-value p99.9 latency to 2.0–2.3 ms; on its own, with the boost on, it barely helped. The .NET 10 thread pool reads this variable (runtime configuration `System.Threading.ThreadPool.UnfairSemaphoreSpinLimit`), but it is not one of the documented [threading settings](https://learn.microsoft.com/dotnet/core/runtime-config/threading) and can change between releases. On 24 logical processors, turning the boost off made no measurable difference to CPU per Read (26.0 against 25.2 µs over 6 paired rounds); latency was not measured there.

Priority boost is a process-wide setting, so the host application decides; the stack does not change it. To evaluate disabling it, set it at startup before the server starts:

```csharp
using System.Diagnostics;

if (OperatingSystem.IsWindows())
{
    using Process process = Process.GetCurrentProcess();
    process.PriorityBoostEnabled = false;
}
```

`Process.PriorityBoostEnabled` calls the Win32 `SetProcessPriorityBoost` function and applies to the existing and future threads of the process. On other platforms the setter has no effect; on .NET Framework, which runs only on Windows, omit the `OperatingSystem.IsWindows()` check. These measurements cover Windows only.

### JIT warm-up

With tiered compilation enabled, .NET initially uses quick-JIT or ReadyToRun code and optimizes frequently called methods in the background. Short measurements can include that transition. With the external open62541 benchmark client of [o6-automation/opcua-benchmarks](https://github.com/o6-automation/opcua-benchmarks) (10 clients with 100 single-value Reads each in flight, server restricted to 4 logical processors), the reported server cost was about 28–31 µs of CPU time per Read in 3-second runs and 8.8 µs at steady state. The single-threaded open62541 result was 8.1 µs on the same machine; with Basic256Sha256 SignAndEncrypt, the recorded costs were 8.8 µs and 12.2 µs respectively.

The benchmark's default parameters (10,000 warm-up and 100,000 measured calls per client) are too short for a .NET server on many cores: on 24 logical processors the measured window still includes JIT warm-up (about 60 µs per Read against 28 µs at steady state). For measurements and for deployments where the first minutes matter:

- Warm up before measuring: run the load until the per-request cost stops falling (for the benchmark above, 100,000 warm-up calls per client), then measure.
- Publish with ReadyToRun (`<PublishReadyToRun>true</PublishReadyToRun>`) so that start-up code begins precompiled instead of at tier 0; tiered compilation still re-optimizes the hot methods.
- `TieredPGO` (on by default since .NET 8) and `TieredCompilation` can be set in the project file (`<TieredPGO>`, `<TieredCompilation>`) or as `DOTNET_TieredPGO` and `DOTNET_TieredCompilation`. Disabling tiered compilation uses an optimizing JIT for methods without precompiled code and disables dynamic PGO. It removes tier promotion, not all startup costs; measure both settings at steady state before changing them.

### Verifying the effective configuration

A setting in the project file reaches the runtime through `<app>.runtimeconfig.json` next to the executable (`configProperties`, for example `"System.GC.Server": true`); a `DOTNET_` environment variable takes precedence over it. To check what a running server uses, log it at start-up:

```csharp
using System.Runtime;

IReadOnlyDictionary<string, object> gc = GC.GetConfigurationVariables();
logger.LogInformation(
    "ServerGC={ServerGC} DATAS={Datas} HeapCount={HeapCount} ProcessorCount={ProcessorCount} Runtime={Runtime}",
    GCSettings.IsServerGC,
    gc["GCDynamicAdaptationMode"],
    gc["HeapCount"],
    Environment.ProcessorCount,
    System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription);
```

`GCDynamicAdaptationMode` is `1` while DATAS is on; with DATAS, `HeapCount` is the upper bound of heaps, not the number in use. `Environment.ProcessorCount` shows the processor count the runtime sized itself for, which reflects the start-up affinity and `DOTNET_PROCESSOR_COUNT`. From outside the process, `dotnet-counters monitor -p <pid>` shows the collection counts, the time spent in GC and the heap size of the running server.

To compare GC settings for a deployment, run the same load against each configuration (for the server load harness, `SERVER_DOTNET_gcServer=0`/`1` and `SERVER_DOTNET_GCDynamicAdaptationMode=0`) and compare CPU and allocation per request, GC count and pause time, and the working set, not only throughput; see the [measurement method](../perf/README.md#measurement-method).

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
- **Increasing the minimum worker count did not help the one-processor workload.** A `MinRequestThreadCount` of 4, 16 or 200 changed the number of ready threads but not CPU per request or latency; see [Small Windows machines and thread priority boost](#small-windows-machines-and-thread-priority-boost) for the measured scheduling change. With the FairShare, Balanced and TrustedReservations profiles, an idle worker spins for a few microseconds before it parks only when the runtime reports at least 8 processors (at most one spinning worker per 8 processors); on smaller machines it parks at once.
- `BadServerTooBusy` responses to established sessions while the processors are not saturated mean that an admission limit, not the CPU, is the limit. With an isolation profile that is not necessarily the queue count: the retained request bytes, the parked-request stage or a per-owner limit reject requests the same way while queue entries are free. Check the `opcua.server.isolation.rejections` metric (its `stage` and `reason` labels) and `opcua.server.isolation.usage` before raising `MaxQueuedRequestCount`; see [transport behavior and overload signals](ResourceIsolation.md#transport-behavior-and-overload-signals).

## Scaling out

A single node's handshake throughput is bounded by its cores. To scale beyond one node, distribute connections across several server nodes behind a connection-distributing front end. The stack provides distributed mirroring of the address space, sessions, and subscriptions for exactly this high-availability and distributed topology.

## See also

- [Rate Limiting and Admission Control](RateLimiting.md) — the connection and session-establishment limiters, the `BadServerTooBusy` signalling, and the client's adaptive reconnect backoff.
- [Performance Benchmarks](Benchmarks.md#server-session-scalability) — the measured session-scaling table and the `ServerManySessionsLoadTestAsync` macro test.
- [Sessions, Reconnection, and Subscription Engines](Sessions.md) — session and subscription-engine architecture.
- [Subscriptions and Monitored Items Service Set](Subscriptions.md) — the subscription engine.
- [Diagnostics](Diagnostics.md) — server diagnostics nodes and how to control them.
