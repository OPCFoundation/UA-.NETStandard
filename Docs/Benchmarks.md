# Performance Benchmarks

This document covers the performance of the **2.0** stack: how to run the
BenchmarkDotNet harnesses, the **2.0 vs 1.5.378** comparison (what improved, what is
still slower, and why, plus planned future work), the **pooled encodeable**
(subscription-notification pooling) micro-benchmarks, and **server session scalability**
(concurrent-session capacity vs 1.5.378).

## How to run

The benchmarks are BenchmarkDotNet harnesses hosted inside the test projects (a
`BenchmarkSwitcher` in `Tests/Common/Main.cs`). Run a single class from its test
project directory:

```powershell
cd Tests/Opc.Ua.Core.Encoders.Tests
dotnet run -c Release -f net10.0 -- --filter "*BinaryEncoderBenchmarks*" --runtimes net10.0
# add --job short for a faster (higher-variance) directional run
```

Encoder/decoder benchmark classes live in `Opc.Ua.Core.Encoders.Tests`
(`BinaryEncoderBenchmarks`, `BinaryDecoderBenchmarks`, `JsonEncoderBenchmarks`,
`JsonEncoderTests`). The end-to-end session benchmarks (`SecurityPolicyBenchmarks`)
live in `Tests/Opc.Ua.Sessions.Tests` and spin up a real in-process client + server
across every security policy.

> Tip: the combined `--filter *` build is flaky on this repo — run **per class**.
> A per-class build may need one or two retries (`dotnet build-server shutdown`
> between attempts).

## 2.0 vs 1.5.378

This section compares the **2.0** stack against the previous **1.5.378** release on
.NET 10, explains **what improved and why**, **what is still slower and why**, and the
**future work** planned to close the remaining gaps.

All ratios below are **2.0 ÷ 1.5.378**. **< 1.0 means 2.0 is faster / allocates
less; > 1.0 means 2.0 is slower / allocates more.**

It is intended as living documentation: when a perf-sensitive change lands, update
the relevant section and re-run the affected benchmark class (see
[How to run](#how-to-run)).

### Why 2.0 differs from 1.5.378

2.0 is a deliberate redesign that, among many other things, replaced several
reference types with **`readonly struct` value types** (`Variant`, `DataValue`,
`NodeId`, `ExpandedNodeId`, `ByteString`, `ArrayOf<T>`, `DateTimeUtc`) and moved
the client/server onto a fully `async` (TAP) pipeline. That redesign **reduces
allocations and GC pressure** (fewer heap objects, less Gen2 traffic) but trades
some of that for **higher per-access CPU cost** — every property read on a large
`readonly struct` may copy the struct or re-run a discriminated-union switch.

The benchmarks below quantify that trade-off: 2.0 allocates less and is faster on
most client paths, at the cost of some encode/decode CPU on the value-type hot
paths.

### What was measured (2.0 vs 1.5.378, 409 matched benchmarks)

- **Allocation geomean: 0.93×** — 2.0 allocates **less** than 1.5.378 overall.
- **Time geomean: ~1.07×** — 2.0 is slightly slower on aggregate, concentrated in
  two areas only: the **binary encoder** and **session establishment**. Everything
  else is at parity or faster.

> The two aggregate geomeans above are from the **baseline full-job** 2.0-vs-1.5.378
> sweep (409 matched benchmarks). Several areas have since improved further (binary
> decode allocation, in-memory and external-stream JSON — see the per-area rows below);
> the full sweep was not re-run on the final tree, so the per-area table reflects the
> current state while the aggregate is the documented baseline (the improvements only
> move it further in 2.0's favour).

| Area | time | alloc |
|---|--:|--:|
| Client read / browse (`ClientTest`, `RequestHeaderTest`) | **0.67–0.79×** | **0.5–0.65×** |
| JSON encode, in-memory (`JsonEncoderTests`) | **0.97×** | **0.60×** |
| Binary decode (`BinaryDecoderBenchmarks`) | ~1.14× | **0.69×** |
| JSON encode, external stream (`JsonEncoderBenchmarks`) | 1.33× | 1.29×² |
| Binary encode (`BinaryEncoderBenchmarks`) | still slower¹ | ~parity¹ |
| Session establishment (`SecurityPolicyBenchmarks`) | still slower³ | — |

¹ The encoder still trails 1.5.378 on **time** (intrinsic value-type struct cost — see
below). Its **allocation** is unchanged by the codec work: a ground-truth
`GC.GetAllocatedBytesForCurrentThread` A/B over the benchmark payload shows identical
per-op allocation before/after the `BinaryPrimitives` change (e.g. `ArraySegmentStream`
76,808 B/op in both). The `Allocated` column that BenchmarkDotNet reports for the
`ArrayPool`/`BufferManager`-backed stream variants is a pooled-buffer **accounting
artifact** (pool rentals/returns attributed differently across separate runs) and is not a
reliable per-op figure for those variants.
² External-stream JSON allocation improved from ~1.44× to ~1.29× once the
`Utf8JsonWriter` is pooled/reused (below); the residual is inherent UTF-8 transcoding.
³ Session establishment remains the largest single regression vs 1.5.378; this is
detailed below.

---

### What improved in 2.0 vs 1.5.378 (and why)

#### Lower allocation almost everywhere

Aggregate allocation across the 409 matched benchmarks is **0.93×**. The value-type
redesign means most messages, node ids, values, and byte strings no longer allocate
individual heap objects. This is the headline win of 2.0 and shows up across client,
server, and encode paths.

#### Client read / browse: ~20–33% faster, ~half the allocation

- `ClientTest.BrowseFullAddressSpaceBenchmarkAsync`: **~30% faster** across every
  security policy (e.g. ~315 ms → ~213 ms).
- `RequestHeaderTest.ReadValues…`: **~20–25% faster**, **~0.5×** allocation.

The async pipeline plus value-type request/response handling cut both CPU and
garbage on the most common client operation (reading/browsing the address space).

#### In-memory JSON encoding: faster *and* ~40% leaner

The in-memory JSON path (`JsonEncoderTests`) is **0.97× time / 0.60× allocation**
vs 1.5.378 — i.e. 2.0 both runs faster and allocates ~40% less. Two changes:

- **Flush at structural boundaries.** `Utf8JsonWriter` never flushes on its own, so
  for a large/streamed payload its internal buffer grows by doubling onto the
  **Large Object Heap** until it holds the entire message. 2.0 flushes buffered JSON
  at object/array boundaries once ≥ 16 KB has accumulated. Flushing only writes
  already-complete tokens and **never changes the output**. Large streamed payloads
  allocate ~7.3 MB instead of ~15.7 MB (**−53%**) with **Gen2/op → 0**.
- **Pooled in-memory buffer.** The default (no external stream) encoder writes
  through a private `ArrayPool<byte>`-backed `IBufferWriter<byte>` instead of a
  per-encoder `MemoryStream`, and reads the result text from the pooled span.
  Buffers are returned with `clearArray: true` so encoded payloads (which may
  contain user tokens or secrets) are not exposed to the next pool consumer.

JSON is the PubSub and REST/gateway encoding path; the LOH-growth fix in particular
removes large, bursty Gen2 allocations for big messages.

#### Binary decoding: ~31% less garbage, CPU near parity

Binary decode allocates **~0.69×** vs 1.5.378 (the value-type `DataValue` removes the
per-field heap object) while staying close on CPU at **~1.14×**. The decoder reads
primitives directly from the buffer span via `System.Buffers.Binary.BinaryPrimitives`
and bulk-copies fixed-width numeric arrays (`Int16/UInt16/Int32/UInt32/Int64/UInt64/
Float/Double`) in a single blit (little-endian) rather than element-by-element. Decoding
is on the hot receive path of every client and server, so the allocation win applies
broadly.

> **Target-framework note:** the quoted decode allocation (0.69×) is a **.NET 10**
> number. On legacy target frameworks the figure is higher — see the legacy
> target-frameworks bullet under [Environment and caveats](#environment-and-caveats).

#### Faster low-level primitives

`UtilsIsEqual*` and `HiResClock*` are faster on 2.0 (e.g. byte-array compares
~25% faster), reflecting span-based and modern-API implementations.

---

### What is still slower in 2.0 vs 1.5.378 (and why)

#### Binary encoder — still slower (the main remaining encode regression)

Encoding a `Variant`/`DataValue` walks the 2.0 `readonly struct` accessor chain in
`WriteVariantValue` / `WriteDataValue`. Each property read can copy the large struct
and/or re-run the built-in-type switch. In the micro-benchmark,
`WriteDataValueArray` (a 10-element `DataValue[]`) dominates (~65% of the payload
iteration) because every element pays that struct cost. 1.5.378 used a
reference-type `DataValue`/`Variant` whose accessors were plain field reads, so this
is the direct CPU cost of the allocation-reducing value-type design. Closing this gap
to parity is tracked under [Future work](#future-work).

The encoder writes scalar primitives via `BinaryPrimitives` into the destination span
(`IBufferWriter<byte>`) and bulk-blits primitive numeric arrays. The default
(no-stream) encoder uses a pooled `ArrayPool<byte>` buffer. This change is **allocation-
neutral**: a ground-truth `GC.GetAllocatedBytesForCurrentThread` A/B over the benchmark
payload shows identical per-op allocation before and after it (the `Allocated` column
BenchmarkDotNet reports for the `ArrayPool`/`BufferManager`-backed stream variants is a
pooled-buffer accounting artifact, not a real per-op delta). The remaining encoder gap is
**time**, not allocation.

**Impact:** encode CPU on the send path, absolute cost a few microseconds per
message. The bulk-array fast-path materially helps realistic large-array payloads
(PubSub datasets, historical values); the residual mostly shows up in synthetic
array-heavy micro-benchmarks.

#### Binary decoder — ~1.14× CPU, ~0.69× allocation

Near time parity with a clear allocation win, via the `BinaryPrimitives` span reads and
bulk numeric-array copies described under [what improved](#binary-decoding-31-less-garbage-cpu-near-parity).

#### JSON encoder, external-stream — improved by `Utf8JsonWriter` pooling

The external-stream JSON variants (`JsonEncoderBenchmarks`, which write to a caller
stream) trailed 1.5.378 because each encoder allocated a fresh `Utf8JsonWriter` and
pays `Utf8JsonWriter` UTF-8 transcoding (vs 1.5.378's `StreamWriter`). 2.0 now
**pools and reuses `Utf8JsonWriter` instances** for the external-stream and
external-`IBufferWriter` constructors: a writer is rented from a small pool (keyed by
`Indented`, capped) and re-targeted with `Utf8JsonWriter.Reset(...)` instead of being
allocated per encoder, then returned on dispose. This removes the per-encoder writer
allocation; the residual is the inherent UTF-8 transcoding. The in-memory path
remains pooled via the `ArrayPool<byte>` buffer described above.

#### Session establishment — still slower; dominated by discovery + crypto

`SecurityPolicyBenchmarks` (`'Create and close session'` / `'Session lifecycle with
read'`) remains the largest single regression vs 1.5.378. These are **end-to-end**
client+server benchmarks (channel + crypto handshake + server session setup), **not**
an encoder path.

2.0 also supports **additional security policies** (e.g. the ECC AES-GCM /
ChaCha20-Poly1305 suites) that 1.5.378 does not. Each configured policy adds per-session
cost on the server side, so a benchmark run that includes the new policies is not a
like-for-like comparison and inflates the all-policy geomean. A benchmark restricted to
the security policies common to both 1.5.378 and 2.0 — for the true apples-to-apples
change — **now exists** (`SecurityPolicySessionCommonWithV15378Benchmarks`, covering
Basic128Rsa15, Basic256, Basic256Sha256, Aes128_Sha256_RsaOaep, Aes256_Sha256_RsaPss).

An allocation profile of one **full connect including discovery** (Basic256Sha256) shows
~3.2 MB/op dominated by **per-connect endpoint discovery** (`GetEndpointsAsync`) plus
strings / `Char[]` / XML / `Byte[]` materialization:

| Allocator (Basic256Sha256 connect) | ~bytes/op |
|---|--:|
| `System.String` | 605 KB |
| `System.Char[]` | 581 KB |
| `System.Byte[]` | 222 KB |
| `UserTokenPolicy` | 176 KB |
| `XmlWellFormedWriter` | 133 KB |
| (isolated `NodeCache` ctor) | 39 KB |

The eager per-session object graph (`NodeCache` + subscription engine) is only
**~1.2% (~39 KB)** of the connect, so lazy-initialising it would **not** move the
regression and was deliberately not pursued. Session establishment is a
one-time-per-connection cost, so absolute throughput impact is bounded to connect
churn.

> **Benchmark fidelity:** because real applications cache the discovered endpoints,
> `SecurityPolicyBenchmarks.CreateCloseSessionAsync` /
> `SessionLifecycleWithReadAsync` pass the cached `Endpoints` into `ConnectAsync`, so
> they measure pure session **create / activate / close** rather than re-running
> `GetEndpointsAsync` on every iteration. A separate `DiscoverEndpointsAsync`
> benchmark tracks endpoint discovery in isolation. A first low-risk discovery
> reduction has landed (`DiscoveryClient.PatchEndpointUrls` skips redundant endpoint-URL
> rewrites, ~22 KB/op); the bulk of the remaining discovery cost is intrinsic
> endpoint / user-token / application-description materialization and stays future work.

#### `JsonEncoderTests.ServiceMessageContext` — ~2× on a tiny absolute

~122 ns vs ~63 ns (~0.94× alloc). Namespace/node-id-heavy message-context encoding;
the absolute cost is tens of nanoseconds and is partially absorbed by the `NodeId`
work above. Low priority.

---

### Future work

1. **Binary encoder — drive time to 1.5.378 parity.** Scalar writes already go through
   `BinaryPrimitives` and numeric arrays bulk-blit (allocation is at parity). The remaining
   gap is **time**, and a micro-profile localises it to the `Variant` value-type access path:
   inside `WriteVariantValue` the `builtInType` switch already knows the type, but the
   `Variant.GetXxx()` accessors then call `TryGetValue`/`TryGetScalar`, which re-read
   `TypeInfo` and re-check scalar/type before returning the union value (scalar writes are
   ~60–160 ns/op vs ~5–7 ns/op for the raw primitive write; `StatusCode`/`LocalizedText`/
   `Guid`/`NodeId` are slowest). A potential win is `internal` "already-validated" union
   accessors the encoder switch can call to skip the re-check — but an initial snapshot attempt
   showed no clear measured win, so this is genuinely close to the intrinsic value-type cost and
   should be re-attempted only with a profile-proven gain.
2. **Session establishment — discovery materialization.** An apples-to-apples session
   benchmark restricted to the security policies common to both 1.5.378 and 2.0 exists
   (`SecurityPolicySessionCommonWithV15378Benchmarks`). The remaining work is to further
   reduce the endpoint/user-token/app-description materialization and XML reader/writer
   allocation in `GetEndpointsAsync` on the connect path (a first low-risk reduction has
   landed; the bulk of the cost is intrinsic description materialization), and optionally
   cache `ConfiguredEndpoint` so repeat connects skip discovery.

### Environment and caveats

- Runtime: **.NET 10.0** (`--runtimes net10.0`), Release config.
- Host: BenchmarkDotNet v0.15.x, Windows 11 on a **shared Hyper-V VM**, Intel Xeon
  Platinum 8473C, 8 physical / 16 logical cores, .NET SDK 10.0.30x.
- **Virtualization caveat:** BenchmarkDotNet warns that a shared/virtualized host
  affects measurements. Treat absolute numbers as indicative and **sub-10% deltas as
  noise**; focus on the direction and magnitude of large deltas.
- The 1.5.378 baseline is a full-job run; the per-class encoder refresh on 2.0 is a
  faster, higher-variance ShortRun, so treat those ratios as directional. Non-encoder
  classes carry run-to-run variance.
- **Pooled-stream allocation columns are unreliable:** for the `ArrayPool`/`BufferManager`-
  backed stream variants (`ArraySegmentStream`, etc.), BenchmarkDotNet's `Allocated`
  column attributes pool rentals/returns differently across separate runs, so it is **not**
  a reliable per-op figure and must not be compared across two independent runs. Use a
  ground-truth `GC.GetAllocatedBytesForCurrentThread` A/B for those (as was done to confirm
  the binary-encoder allocation is unchanged by the `BinaryPrimitives` work).
- **Legacy target frameworks:** 2.0 perf optimization targets the modern runtime (.NET 10).
  The **.NET Framework (net48)** and **netstandard2.0** target frameworks are **not** a
  performance optimization target for 2.0: where a fast path is gated on `NET6_0_OR_GREATER`
  (for example the `stackalloc` / `ArrayPool<byte>` `BinaryDecoder.ReadString` path), the
  legacy frameworks keep the simpler allocating fallback. Allocation/throughput figures in
  this document are .NET 10 numbers and legacy-TFM behaviour may be higher; that gap will not
  be closed for 2.0.

## Pooled encodeable

Microbenchmark results for the `IPooledEncodeable` + `PooledEncodeableType<T>`
activator pooling feature added in support of `ManagedSessionBuilder.WithPoolNotifications()`.

### Source

`Tests/Opc.Ua.Client.Tests/Subscription/PooledNotificationBenchmarks.cs`

### Methodology

- BenchmarkDotNet v0.15.8, `[MemoryDiagnoser]`, `InProcessEmitToolchain` (avoids
  the cold-rebuild timeout that the default toolchain hits on the test
  assembly's transitive reference graph).
- `ShortRun` job: 3 warmup iterations, 3 measured iterations, single launch.
- Inner loop is `Iterations = 1000` allocate/use cycles per benchmark
  invocation — measured values are aggregate over the inner loop.
- Pre-warm in `[GlobalSetup]` populates all four type pools to steady-state
  before the first measured iteration so the pool-hit fast path is exercised
  rather than the cold-pool `new T()` fallback.

### Environment

| Item | Value |
|---|---|
| OS | Windows 11 (10.0.26200.8390 / 25H2) |
| CPU | Intel Xeon W-2235 @ 3.80 GHz, 6 physical / 12 logical cores |
| SDK | .NET SDK 10.0.300 |
| Host runtime | .NET 10.0.8 (RyuJIT, x86-64-v4) |
| Config | Release, `-c Release -f net10.0` |

### Results

`DataValue` is a `readonly struct` that lives inline in
`MonitoredItemNotification.Value`, so a notification item does not allocate a
separate `DataValue` heap object.

| Method | Mean | Allocated/op | Allocation ratio | Gen0/1000 ops |
|---|---:|---:|---:|---:|
| `new MonitoredItemNotification()` *(baseline)* | 34.874 µs | 104,408 B | 1.000 | 24.17 |
| `MonitoredItemNotificationActivator + Reuse` | **23.323 µs** | **408 B** | **0.004** | **0.092** |
| `new DataChangeNotification()` | 66.759 µs | 216,344 B | 2.072 | 50.05 |
| `DataChangeNotificationActivator + Reuse` | **54.812 µs** | **32,344 B** | **0.310** | **7.45** |
| `new EventFieldList()` | 8.967 µs | 48,408 B | 0.464 | 11.22 |
| `EventFieldListActivator + Reuse` | **22.280 µs** | **408 B** | **0.004** | **0.092** |

### Interpretation

For the dominant publish-payload allocator (`MonitoredItemNotification`,
which arrives in arrays of arbitrary length on every data-change publish):

- Mean time per item drops from ~35 µs/1000 to ~23 µs/1000 — a **33%
  throughput improvement** on the synthetic benchmark.
- Allocations per 1000 ops drop from 104 KB to 408 B — **~256× reduction**.
- Gen-0 collections per 1000 ops drop from 24.17 to 0.09 — **~263× reduction**.

For `DataChangeNotification` (container + inner items + backing array):

- Pooling both the container and the inner `MonitoredItemNotification`
  items brings allocation down to **31% of baseline**. The residual
  32 KB is the `MonitoredItemNotification[]` backing array itself
  (array pooling is out of scope).

For `EventFieldList`:

- The baseline `new` path is already cheap (~9 µs) because the empty
  `EventFields` `ArrayOf<Variant>` is a zero-allocation default. The
  pooled path adds `Interlocked.CompareExchange` + pool round-trip
  overhead (~22 µs). Under realistic dispatch where each `EventFieldList`
  carries a non-empty `EventFields` array, the allocation savings
  dominate and the pooled path wins.

### Reproducing

```pwsh
# Build release
dotnet build Tests/Opc.Ua.Client.Tests -c Release -f net10.0

# Run BDN harness
cd Tests/Opc.Ua.Client.Tests/bin/Release/net10.0
dotnet Opc.Ua.Client.Tests.dll --filter "*PooledNotificationBenchmarks*"
```

Artifacts (markdown, csv, html) are written to
`Tests/Opc.Ua.Client.Tests/bin/Release/net10.0/BenchmarkDotNet.Artifacts/results/`.

### Out of scope

The following payloads are not pooled and remain
attributable to the residual allocation in the pooled
`DataChangeNotification` numbers above:

- `Variant` value payload (arbitrary user data inside `DataValue.Value`)
- Dispatch backing arrays (`DataValueChange[]`, `EventNotification[]`,
  `MonitoredItemNotification[]`)

`DataValue` is now a readonly struct and no longer allocates on the
heap. It is not an `IEncodeable` and does not participate in the
activator pool system.

## Server session scalability

The `[Explicit]` macro test `ServerManySessionsLoadTestAsync(int sessionCount)` (`Tests/Opc.Ua.Sessions.Tests/LoadTest.cs`) exercises the reference server under many concurrent sessions. Each session opens its own secure channel, creates one slow-publishing subscription (1000 ms) with a single monitored item on a shared value node, and a separate writer session changes that value periodically; every session is expected to receive value-change notifications over a steady-state window. It runs over `Basic256Sha256` (sign & encrypt) and asserts that all sessions connect and all receive notifications. It is parameterized from a `500` baseline up to a `10000` stress case (500, 1000, 1500, 2000, 2500, 4000, 5000, 8000, 10000), selected by name (e.g. `ServerManySessionsLoadTestAsync(2000)`).

> For a deep, code-referenced analysis of *why* a single node tops out here — the establishment vs steady-state boundaries and the built-in controls for degrading gracefully under load — see [Server Session Scalability](ServerScalability.md).

| Tested configuration | Value |
| --- | --- |
| Concurrent sessions | 500 baseline, up to 10000 (selectable case) |
| Secure channels | one per session (`MaxChannelCount`) |
| Subscriptions per session | 1 (1000 ms publishing interval) |
| Monitored items per subscription | 1 (shared value node) |
| Security policy | `Basic256Sha256` (sign & encrypt) |
| Steady-state duration | 60 s |

### Observed scaling

On comparable hardware **1.5.378** capped a single node at roughly **~2000 concurrent sessions**: every held long-poll `Publish` pinned a request-processing worker, so the worker pool - not CPU - became the bottleneck as the session count climbed. **2.0** decouples held Publishes from the worker budget (`ServerConfiguration.DecoupleHeldPublishRequests`, on by default), so a small worker pool serves many thousands of parked Publishes and the ceiling moves out to session establishment (the CPU-bound RSA handshake) - about **~4000** concurrent sessions on the 6-core machine below.

The numbers below were measured on an **Intel Xeon W-2235 (6 physical cores / 12 logical threads), 64 GB RAM, 64-bit Windows**, with the client and the in-process reference server running in the *same* process on a shared developer machine under light background load. Because both ends share the same cores, session establishment - a CPU-bound `Basic256Sha256` RSA handshake performed on both the client and the server - is the dominant cost, and its throughput declines as concurrency rises. Steady-state publish delivery is serviced by a per-cycle sweep parallelized across cores; for every count that established cleanly, all sessions received 100 % of their notifications within the steady-state window (0 drops).

| Concurrent sessions | Sessions establish | Average sessions/sec created | All sessions receive notifications |
| --- | --- | --- | --- |
| 500 | Yes | 26 | Yes |
| 1000 | Yes | 15 | Yes |
| 1500 | Yes | 11 | Yes |
| 2000 | Yes | 14 | Yes |
| 2500 | Yes | 13 | Yes |
| 4000 | Yes | 11 | Yes |
| 10000 | No (*) | — | — |

(*) These figures are hardware- and load-dependent and are directional; they improve on dedicated hardware where the client and server run on separate machines. With `ServerConfiguration.DecoupleHeldPublishRequests` on (the default) and `MaxRequestThreadCount` sized to the active establishment concurrency (~200) rather than to the session count, this 6-core machine cleanly established every session and delivered 100 % of notifications up to **~4000** concurrent sessions. At 10000 it tips into a secure-channel connect storm (repeated handshake aborts/retries and tens of thousands of channel attempts for the target) and does not establish the full set within the connect budget - so on this hardware establishment, not notification delivery, is the ceiling; more cores push it higher. When sessions establish but do not receive notifications the last column instead reads `No (N drops)`, where `N` is the number of sessions that did not receive notifications within the window. See [Server Session Scalability](ServerScalability.md) for the code-referenced analysis and the built-in controls for degrading gracefully under load.

### Sizing and configuration

* `MaxSessionCount` (default 100) caps the concurrent open sessions; size `MaxChannelCount` (one channel per session) and `MaxSubscriptionCount` above the target session count.
* `MaxRequestThreadCount` caps concurrent request processing. With `ServerConfiguration.DecoupleHeldPublishRequests` on (the default), a held long-polled `Publish` releases its worker at the park point instead of occupying it for the whole wait, so the pool no longer has to scale with the session count - size it to the active (non-parked) establishment concurrency (a small pool of ~100–200 workers cleanly served thousands of sessions in measurement). Sizing it to the session count is counterproductive: a very large pool oversubscribes the cores during the connect burst, slowing establishment and lowering the ceiling. `MinRequestThreadCount` pre-warms the pool so a connect burst is not throttled by thread-pool cold-start. (Setting `DecoupleHeldPublishRequests = false` restores the legacy coupling, where a server serving N sessions again needs a pool well above N or held `Publish` requests starve other services with `BadRequestTimeout`.)
* `MaxFailedAuthenticationAttempts` (default 5; `0` disables) is the brute-force lockout threshold, keyed per client certificate. A single-certificate client that opens many sessions can trip it on transient handshake failures, after which further sessions are rejected with `BadUserAccessDenied`; disable or raise it for such clients.
* Session establishment is CPU-bound (RSA handshakes) but parallelizes across cores; throttle/stagger concurrent connects and scale cores for bulk connection throughput.

> A full macro benchmark that produces a capability matrix (sessions × subscriptions × items against CPU/memory) across configurations is planned as follow-up work.
