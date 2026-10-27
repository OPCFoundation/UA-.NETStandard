# Performance Benchmarks

This document explains how to run the **2.0** stack's BenchmarkDotNet
harnesses and summarizes its performance against **1.5.378**. It covers
improvements, remaining slowdowns, and planned future work. It also reports
the pooled encodeable micro-benchmarks, server request throughput measured with
the out-of-process [server load harness](../perf/README.md), and server
session scalability.

> **Current and historical results.** [Server request throughput](#server-request-throughput)
> records dated results from the 2.0.2 development line
> (`2.0.2-preview`, `master`). The other sections are historical records:
> they were measured on earlier 2.0 builds and other hardware, before the
> 2.0.2 request-path, publish and event-delivery changes, and are kept as
> measured, not re-run. Do not combine numbers from different sections, and do
> not extrapolate a deployment's capacity from one machine.

## Contents

- [How to run](#how-to-run)
- [2.0 vs 1.5.378](#20-vs-15378)
  - [Why 2.0 differs from 1.5.378](#why-20-differs-from-15378)
  - [What was measured (2.0 vs 1.5.378, 409 matched benchmarks)](#what-was-measured-20-vs-15378-409-matched-benchmarks)
  - [What improved in 2.0 vs 1.5.378 (and why)](#what-improved-in-20-vs-15378-and-why)
  - [What is still slower in 2.0 vs 1.5.378 (and why)](#what-is-still-slower-in-20-vs-15378-and-why)
  - [Future work](#future-work)
  - [Environment and caveats](#environment-and-caveats)
- [Pooled encodeable](#pooled-encodeable)
  - [Source](#source)
  - [Methodology](#methodology)
  - [Environment](#environment)
  - [Results](#results)
  - [Interpretation](#interpretation)
  - [Reproducing](#reproducing)
  - [Out of scope](#out-of-scope)
- [Server request throughput](#server-request-throughput)
  - [Run record](#run-record)
  - [Reference numbers](#reference-numbers)
  - [Reproducing the results](#reproducing-the-results)
  - [Garbage collector modes](#garbage-collector-modes)
  - [Processor placement](#processor-placement)
  - [Publish and event delivery](#publish-and-event-delivery)
  - [Recommended baseline](#recommended-baseline)
  - [Measuring](#measuring)
  - [Open measurements](#open-measurements)
  - [Historical results (2026-10-18)](#historical-results-2026-10-18)
- [Server session scalability](#server-session-scalability)
  - [Observed scaling](#observed-scaling)
  - [Subscription transport buffer pooling](#subscription-transport-buffer-pooling)
  - [Sizing and configuration](#sizing-and-configuration)

## How to run

The benchmarks are BenchmarkDotNet harnesses hosted inside the test projects (a
`BenchmarkSwitcher` in `tests/Common/Main.cs`). Run a single class from its test
project directory:

```powershell
cd tests/Opc.Ua.Core.Encoders.Tests
dotnet run -c Release -f net10.0 -- --filter "*BinaryEncoderBenchmarks*" --runtimes net10.0
# add --job short for a faster (higher-variance) directional run
```

Encoder/decoder benchmark classes live in `Opc.Ua.Core.Encoders.Tests`
(`BinaryEncoderBenchmarks`, `BinaryDecoderBenchmarks`, `JsonEncoderBenchmarks`,
`JsonEncoderTests`). The end-to-end session benchmarks (`SecurityPolicyBenchmarks`)
live in `tests/Opc.Ua.Sessions.Tests` and spin up a real in-process client + server
across every security policy.

> Tip: the combined `--filter *` build is flaky on this repo — run **per class**.
> A per-class build may need one or two retries (`dotnet build-server shutdown`
> between attempts).

## 2.0 vs 1.5.378

*Historical record: BenchmarkDotNet measurements of earlier 2.0 builds on the
Xeon host described under [Environment and caveats](#environment-and-caveats).*

This section compares the **2.0** stack with **1.5.378** on .NET 10. It
explains what improved, what remains slower, and what future work could
close the gaps.

All ratios below are **2.0 ÷ 1.5.378**. **< 1.0 means 2.0 is faster / allocates
less; > 1.0 means 2.0 is slower / allocates more.**

Keep this section current as performance-sensitive changes land. Update the
relevant results and rerun the affected benchmark class (see
[How to run](#how-to-run)).

### Why 2.0 differs from 1.5.378

The 2.0 redesign replaced several reference types with **`readonly struct`
value types**: `Variant`, `DataValue`, `NodeId`, `ExpandedNodeId`,
`ByteString`, `ArrayOf<T>`, and `DateTimeUtc`. It also moved client and server
operations to a fully `async` (TAP) pipeline.

The redesign **reduces allocations and GC pressure** by creating fewer heap
objects and less Gen2 traffic. It can also increase per-access CPU cost.
Reading a property from a large `readonly struct` may copy the struct or rerun
a discriminated-union switch.

The benchmarks below quantify this trade-off. Version 2.0 allocates less and
runs faster on most client paths, but some value-type encoding and decoding
paths use more CPU.

### What was measured (2.0 vs 1.5.378, 409 matched benchmarks)

- **Allocation geomean: 0.93×** — 2.0 allocates **less** than 1.5.378 overall.
- **Time geomean: ~1.07×** — 2.0 is slightly slower on aggregate, concentrated in
  two areas only: the **binary encoder** and **session establishment**. Everything
  else is at parity or faster.

> The two aggregate geomeans above come from the **baseline full-job**
> 2.0-versus-1.5.378 sweep of 409 matched benchmarks. Several areas have since
> improved, including binary decode allocation and in-memory and external-stream
> JSON (see the per-area rows below). The team did not rerun the full sweep on
> the final tree. The per-area table shows current results, while the aggregate
> remains the documented baseline. These improvements favor 2.0 further.

| Area | time | alloc |
|---|--:|--:|
| Client read / browse (`ClientTest`, `RequestHeaderTest`) | **0.67–0.79×** | **0.5–0.65×** |
| JSON encode, in-memory (`JsonEncoderTests`) | **0.97×** | **0.60×** |
| Binary decode (`BinaryDecoderBenchmarks`) | ~1.14× | **0.69×** |
| JSON encode, external stream (`JsonEncoderBenchmarks`) | 1.33× | 1.29×² |
| Binary encode (`BinaryEncoderBenchmarks`) | still slower¹ | ~parity¹ |
| Session establishment (`SecurityPolicyBenchmarks`) | still slower³ | — |

¹ The encoder remains slower than 1.5.378 on **time**, consistent with the
intrinsic cost of value-type structs (see below). The codec work did not change
**allocation**. A `GC.GetAllocatedBytesForCurrentThread` A/B test on the
benchmark payload measured the same allocation before and after the
`BinaryPrimitives` change: 76,808 B/op for `ArraySegmentStream`.

BenchmarkDotNet's `Allocated` values for `ArrayPool`/`BufferManager`-backed
streams are an accounting artifact. Pool rentals and returns can be attributed
differently across independent runs, so these values are not reliable per-op
figures.
² External-stream JSON allocation improved from ~1.44× to ~1.29× once the
`Utf8JsonWriter` is pooled/reused (below); the residual is inherent UTF-8 transcoding.
³ Session establishment remains the largest single regression vs 1.5.378; this is
detailed below.

---

### What improved in 2.0 vs 1.5.378 (and why)

#### Lower allocation almost everywhere

Aggregate allocation across the 409 matched benchmarks is **0.93×**. The value-type
redesign means most messages, node ids, values, and byte strings no longer allocate
individual heap objects across client, server, and encode paths.

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

The external-stream JSON variants in `JsonEncoderBenchmarks` write to a
caller-provided stream. They trailed 1.5.378 because each encoder allocated a
new `Utf8JsonWriter` and paid for UTF-8 transcoding (1.5.378 used
`StreamWriter`).

Version 2.0 now **pools and reuses `Utf8JsonWriter` instances** for external
stream and `IBufferWriter` constructors. The pool is small, keyed by
`Indented`, and capped. Each encoder rents a writer, retargets it with
`Utf8JsonWriter.Reset(...)`, and returns it on disposal. This removes the
per-encoder writer allocation; UTF-8 transcoding remains. The in-memory path
continues to use the `ArrayPool<byte>` buffer described above.

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

An allocation profile of one **full connect, including discovery**
(`Basic256Sha256`) shows about **3.2 MB/op**. **Per-connect endpoint
discovery** through `GetEndpointsAsync` accounts for most of the allocation.
The profile attributes it to:

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

> **Benchmark fidelity:** real applications cache discovered endpoints.
> `SecurityPolicyBenchmarks.CreateCloseSessionAsync` and
> `SessionLifecycleWithReadAsync` pass cached `Endpoints` to `ConnectAsync`.
> These benchmarks create, activate, and close sessions without rerunning
> `GetEndpointsAsync` on every iteration. `DiscoverEndpointsAsync` measures
> endpoint discovery separately.
> `DiscoveryClient.PatchEndpointUrls` skips redundant endpoint-URL rewrites
> and reduces allocation by about 22 KB/op. Most remaining discovery cost
> comes from materializing endpoints, user-token policies, and application
> descriptions; that work remains future work.

#### `JsonEncoderTests.ServiceMessageContext` — ~2× on a tiny absolute

~122 ns vs ~63 ns (~0.94× alloc). Namespace/node-id-heavy message-context encoding;
the absolute cost is tens of nanoseconds and is partially absorbed by the `NodeId`
work above. Low priority.

---

### Future work

1. **Binary encoder — drive time to 1.5.378 parity.** Scalar writes use
   `BinaryPrimitives`, and numeric arrays bulk-blit; allocation is at parity.
   The remaining gap is **time**. A micro-profile points to the `Variant`
   value-type access path. Inside `WriteVariantValue`, the `builtInType`
   switch already knows the type, but `Variant.GetXxx()` calls
   `TryGetValue`/`TryGetScalar`, which reread `TypeInfo` and check the scalar
   and type before returning the union value. Scalar writes take about
   60–160 ns/op, compared with 5–7 ns/op for a raw primitive write.
   `StatusCode`, `LocalizedText`, `Guid`, and `NodeId` are slowest.

   Internal, already-validated union accessors might let the encoder skip
   these checks. An initial attempt showed no clear measured gain. Revisit
   this only if profiling demonstrates an improvement over the intrinsic
   value-type cost.
2. **Session establishment — discovery allocations.** The
   `SecurityPolicySessionCommonWithV15378Benchmarks` benchmark compares only
   security policies supported by both 1.5.378 and 2.0. Further work could
   reduce allocations as `GetEndpointsAsync` builds endpoint, user-token, and
   application-description data and processes XML. A low-risk reduction has
   already landed, but most remaining cost comes from building the endpoint
   description. Caching `ConfiguredEndpoint` could also let repeat connections
   skip discovery.

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
- **Pooled-stream allocation columns are unreliable.** For `ArrayPool`- and
  `BufferManager`-backed streams such as `ArraySegmentStream`, BenchmarkDotNet
  can attribute pool rentals and returns differently across independent runs.
  Do not compare these columns as per-op figures. Use a
  `GC.GetAllocatedBytesForCurrentThread` A/B test instead, as in the
  binary-encoder comparison above.
- **Legacy target frameworks:** 2.0 perf optimization targets the modern runtime (.NET 10).
  The **.NET Framework (net48)** and **netstandard2.0** target frameworks are **not** a
  performance optimization target for 2.0: where a fast path is gated on `NET6_0_OR_GREATER`
  (for example the `stackalloc` / `ArrayPool<byte>` `BinaryDecoder.ReadString` path), the
  legacy frameworks keep the simpler allocating fallback. Allocation/throughput figures in
  this document are .NET 10 numbers and legacy-TFM behaviour may be higher; that gap will not
  be closed for 2.0.

## Pooled encodeable

*Historical record: measured on an earlier 2.0 build (environment below).*

Microbenchmark results for the `IPooledEncodeable` + `PooledEncodeableType<T>`
activator pooling feature added in support of `ManagedSessionBuilder.WithPoolNotifications()`.

### Source

`tests/Opc.Ua.Subscriptions.Tests/PooledNotificationBenchmarks.cs`

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

- Mean time per 1,000 allocate/use iterations drops from 34.874 µs to
  23.323 µs — a roughly **33% time reduction** in this synthetic benchmark.
- Allocations per 1000 ops drop from 104 KB to 408 B — **~256× reduction**.
- Gen-0 collections per 1000 ops drop from 24.17 to 0.09 — **~263× reduction**.

For `DataChangeNotification` (container + inner items + backing array):

- Pooling both the container and the inner `MonitoredItemNotification`
  items brings allocation down to **31% of baseline**. The residual
  32 KB is the `MonitoredItemNotification[]` backing array itself
  (array pooling is out of scope).

For `EventFieldList`:

- In the historical empty-`EventFields` benchmark, pooling lowers allocations
  but increases mean time from 8.967 µs to 22.280 µs per 1,000 iterations.
  The empty `EventFields` default allocates no backing storage, leaving little
  allocation work to offset the `Interlocked.CompareExchange` and pool
  round-trip overhead. Populated events were not measured.

### Reproducing

```pwsh
# Build release
dotnet build tests/Opc.Ua.Subscriptions.Tests -c Release -f net10.0

# Run BDN harness
cd tests/Opc.Ua.Subscriptions.Tests/bin/Release/net10.0
dotnet Opc.Ua.Subscriptions.Tests.dll --filter "*PooledNotificationBenchmarks*"
```

Artifacts (markdown, csv, html) are written to
`tests/Opc.Ua.Subscriptions.Tests/bin/Release/net10.0/BenchmarkDotNet.Artifacts/results/`.

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

## Server request throughput

The [server load harness](../perf/README.md) runs the server in a child
process, so its CPU time, allocations and GC counters exclude the load
generator. Its `native-read` scenario drives the server with the native
open62541 Read workers of
[o6-automation/opcua-benchmarks](https://github.com/o6-automation/opcua-benchmarks),
so the .NET server and other OPC UA stacks are measured with the same client.
The managed scenarios (`read`, `sub`, `events`) use the harness itself as the
client.

### Run record

All tables in this section except the [historical results](#historical-results-2026-10-18)
share this run record.

| Run record | Value |
|---|---|
| Date, commit | 2026-10-21, `master` f6b27025a (`2.0.2-preview`), Release build of `perf/ServerLoadHarness` |
| Host | AMD Ryzen AI 9 HX 370: 4 Zen 5 cores (logical processors 0–7, one core complex) and 8 Zen 5c cores (8–23, the other complex), SMT, 24 logical processors; 64 GB RAM |
| OS, power policy | Windows 11 Home 10.0.26300, *Balanced* power plan, mains power |
| SDK, runtime | .NET SDK 10.0.402, .NET runtime 10.0.13 |
| Server | Harness child; `MaxRequestThreadCount` 200. `native-read`: benchmark-contract mode (`AsyncCustomNodeManager`, `MaxQueuedRequestCount` clients × in flight + 3). Managed scenarios: reference server node set, `MaxQueuedRequestCount` 2000 |
| GC | Per table; where not stated, Server GC with DATAS (the .NET 10 default for Server GC). The harness client keeps workstation GC |
| Placement | Client and server on the same machine, not pinned unless the table says so |
| Repeats | 5 rounds per row, variants alternated and reversed every other round (`perf/variants.sh`); median with the range (min–max) of the 5 rounds |
| Artifacts | `RESULT` blocks per round, produced by the commands under each table |

The machine was a developer workstation, not a dedicated host. Rounds that
overlapped another test host or load harness were discarded and repeated, but
other background work (builds, editors) was not excluded; it shows as the
low end of some throughput ranges, while CPU and allocation per operation stay
narrow. Read the ranges before comparing rows, and treat throughput as the
capacity of this machine only.

### Reference numbers

`native-read`: 10 native Read worker processes with 100 single-value Reads of
`ns=1;i=1001..1100` in flight each, no security unless noted, steady state
(100,000 warm-up and 300,000 measured Reads per worker).

| Server | Processors | Throughput (req/s) | Server CPU / Read | Allocated / Read |
|---|---|--:|--:|--:|
| UA-.NETStandard | 4 logical (`0xF`, two Zen 5 cores) | 440k [395k–467k] | 8.9 µs [8.4–9.9] | 2.3 KB |
| open62541 (single-threaded) | — | 137k [105k–141k] | 7.3 µs [7.1–9.4] | — |
| UA-.NETStandard | 24 logical | 600k [573k–666k] | 22.9 µs [21.7–24.3] | 2.4 KB |
| UA-.NETStandard, 10 × 1000 in flight | 24 logical | 664k [505k–672k] | 20.8 µs [19.7–24.8] | 2.4 KB |
| UA-.NETStandard, Basic256Sha256 SignAndEncrypt | 24 logical | 507k [415k–524k] | 24.9 µs [24.0–25.7] | 2.4 KB |

Restricted to 4 logical processors, the .NET server used about 1.2 times the
CPU per Read of the single-threaded open62541 server and ran the Reads on
four processors in parallel. On all 24 logical processors throughput rose by
about a third while CPU per Read rose 2.6 times: the server spans both core
complexes and both core types; see [Processor placement](#processor-placement).

Short runs include JIT warm-up: with the benchmark's default
parameters (10,000 warm-up and 100,000 measured calls per client) the same
server showed about 60 µs per Read on 24 logical processors in earlier
measurements. See [JIT warm-up](ServerScalability.md#jit-warm-up).

### Reproducing the results

Build the harness and the native workers as described in
[perf/README.md](../perf/README.md#external-benchmark-workers). Every table in
this section has a variant file and a scenario file in
[`perf/benchmarks`](../perf/benchmarks); `perf/variants.sh` runs them (5 rounds
by default) and `perf/variants-summary.ps1` prints the medians and ranges.
`WORKERS` is the build directory of the benchmark workers:

```bash
cd perf
WORKERS=<workers> VARIANTS=benchmarks/read-variants.txt SCENARIOS=benchmarks/read-scenarios.txt OUT=read.txt ./variants.sh
pwsh -File variants-summary.ps1 read.txt
```

| Table | Variants | Scenarios |
|---|---|---|
| [Reference numbers](#reference-numbers) | `read-variants.txt` | `read-scenarios.txt` |
| [Garbage collector modes](#garbage-collector-modes) | `gc-variants.txt` | `gc-scenarios.txt` |
| [Processor placement](#processor-placement) | `affinity-variants.txt` | `affinity-scenarios.txt` |
| [Publish and event delivery](#publish-and-event-delivery) | `delivery-variants.txt` | `delivery-scenarios.txt` |

Throughput is `client: req/s` (`notif/s`, `events/s`), CPU and allocated bytes
per operation are `per-op: cpuUs` and `allocB` (`per-op2` per written value or
generated event), and the GC counts, pause time and working set of the window
are on the `server:` line. See
[Reading the result block](../perf/README.md#reading-the-result-block).

### Garbage collector modes

The server child under three runtime configurations of the same build; the
harness client is unchanged. `SERVER_DOTNET_gcServer=0` selects workstation
(concurrent) GC, `SERVER_DOTNET_gcServer=1` Server GC with DATAS, and adding
`SERVER_DOTNET_GCDynamicAdaptationMode=0` Server GC with a fixed heap count
(one heap per logical processor, 24). Pause is the total GC pause time of the
server in the measured window; working set is at the end of the window.

| Workload, window | GC | Throughput | Latency p50 / p99 | Server CPU / op | GC pause | Working set |
|---|---|--:|--:|--:|--:|--:|
| `native-read` 10 × 100 (300,000 Reads per worker) | Workstation | 385k req/s [378k–405k] | — | 24.2 µs | 2320 ms | 164 MB |
| | Server, DATAS | 661k req/s [606k–671k] | — | 22.5 µs | 214 ms | 186 MB |
| | Server, fixed heaps | 640k req/s [613k–671k] | — | 24.3 µs | 30 ms | 511 MB |
| `read`, 20 sessions × 100 values (15 s) | Workstation | 45.6k req/s [45.3k–45.7k] | 0.27 / 1.81 ms | 231 µs | 4885 ms | 209 MB |
| | Server, DATAS | 66.1k req/s [64.6k–66.7k] | 0.26 / 1.15 ms | 222 µs | 602 ms | 325 MB |
| | Server, fixed heaps | 68.3k req/s [66.8k–68.7k] | 0.26 / 1.15 ms | 214 µs | 132 ms | 616 MB |
| `events`, 50 sessions × 15 subscriptions, encrypted (15 s) | Workstation | 44 events/s [43–45] | 206 / 323 ms | 53 ms / event | 6923 ms | 315 MB |
| | Server, DATAS | 83 events/s [81–86] | 153 / 207 ms | 37 ms / event | 519 ms | 803 MB |
| | Server, fixed heaps | 84 events/s [83–86] | 152 / 199 ms | 35 ms / event | 65 ms | 2041 MB |

The `events` rows report events generated per second (each is delivered to
750 event monitored items; 99.8–100 % were received in every round), the
event-to-notification latency and the server CPU per generated event. The
`sub` scenario of `gc-scenarios.txt` (50 × 15 × 600) is not tabulated: it is
limited by the regression described under
[Publish and event delivery](#publish-and-event-delivery) and delivered
about 26 % in all three modes.

In these runs:

- Workstation GC lowered throughput by 31–47 % against Server GC and paused
  the server for 30–46 % of the window.
- Server GC with a fixed heap count cut the pause time by a factor of 4.6–8
  against DATAS, at 1.9–2.8 times the working set. Throughput and CPU per
  operation stayed within the ranges of DATAS.
- Allocated bytes per operation did not depend on the GC mode.

### Processor placement

`native-read`, Server GC with DATAS. Logical processors 0–7 are the Zen 5
(performance) cores of one core complex, 8–23 the Zen 5c cores of the other.
The server variants set `--server-affinity` and a matching
`SERVER_DOTNET_PROCESSOR_COUNT`; the native client workers run unpinned unless
`--client-affinity` is set.

| Placement | No security: throughput | CPU / Read | Busy cores | SignAndEncrypt: throughput | CPU / Read | Busy cores |
|---|--:|--:|--:|--:|--:|--:|
| Default (no affinity) | 637k [578k–657k] | 22.9 µs | 14.3 | 499k [284k–510k] | 24.5 µs | 12.2 |
| Server on Zen 5 (`0xFF`, 8) | 593k [241k–668k] | 11.7 µs | 7.0 | 500k [495k–519k] | 14.6 µs | 7.3 |
| Server on Zen 5, clients on Zen 5c | 630k [602k–676k] | 12.0 µs | 7.6 | 471k [254k–525k] | 14.0 µs | 6.6 |
| Server on Zen 5c (`0xFFFF00`, 16), clients on Zen 5 | 730k [344k–752k] | 20.0 µs | 14.4 | 479k [236k–493k] | 22.2 µs | 10.7 |

CPU per Read stayed within 8 % of the median in every round except one Zen 5c round (16.9 µs). In
these runs, the server on the eight Zen 5 logical processors had a median
throughput within 7 % of the default placement (and the same with
SignAndEncrypt) at about half the CPU per Read and half the busy cores. The
Zen 5c complex, with clients kept off it, gave the highest median throughput
without security, at a CPU per Read between the two. The low ends of several
ranges (241k–344k) are single rounds disturbed by other work on the machine.

These results describe one processor, one workload and Windows; Linux and
Intel performance/efficiency-core processors were not measured. Commands for
setting the affinity are in
[Hybrid CPUs and processor affinity](ServerScalability.md#hybrid-cpus-and-processor-affinity).

### Publish and event delivery

The 2.0.2 line changed the server paths that deliver notifications: the
publish cadence (#4829), event delivery (#4837, #4845, #4859) and data change
notification allocation (#4830). `delivery-scenarios.txt`, Server GC with
DATAS, Basic256Sha256 SignAndEncrypt, publishing interval 100 ms, queue size 1
(`sub`) and 10000 (`events`), 15-second window after 10 s of warm-up:

| Workload | Rate | Delivered | Latency p50 / p99 | Server CPU / notification | Allocated / notification | GC pause |
|---|--:|--:|--:|--:|--:|--:|
| `sub` 10 sessions × 15 subscriptions × 600 items, all written every 150 ms | 600k notif/s [594k–605k] | 100 % [98.1–101] | 104 / 147 ms | 10.8 µs [10.4–11.3] | 803 B [468–1018] | 205 ms |
| `sub` 2 × 1 × 100, written every 150 ms | 1344 notif/s [1332–1345] | 101 % [100–101] | 61 / 97 ms | 43 µs [34–69] | 5.7 KB | 5 ms |
| `events` 10 sessions × 15 subscriptions, 1000 events/s requested | 598 events/s generated [593–610], 89.8k notif/s | 100 % [99.7–100] | 61 / 134 ms | 32 µs [30–33] | 6.6 KB | 605 ms |

Latency is from the write (or the event report) to the notification at the
client. A `sub` delivery above 100 % means some items reported more than one
value per write interval. In the `events` row the server generated 598 of the
requested 1000 events per second, at about 4.8 ms of server CPU per event
delivered to 150 monitored items; the reporting thread did not reach the
requested rate.

**Regression in many-subscription data change delivery.** The 50 sessions ×
15 subscriptions × 600 items shape of #4829 (`sub` in `gc-scenarios.txt`) is not
tabulated: on this `master` it delivered about 26 % of the expected
notifications in every GC mode, with writes taking 0.4–1.2 s and the server
reporting about a million lock contentions in the window. With the same
harness arguments (`--client classic`, Server GC for client and server), the
#4829 merge commit 37a2a831f delivered 93 % at 3.9 µs of server CPU per
notification. A `git bisect` over this shape found #4757 to be the first
commit with the lower delivery (0edc3b55a: 98 %, 2.8 µs per notification;
65a67dd2f: 6 %, 94 µs). The 10-session row above may be affected as well:
its CPU per notification is about twice the value reported in #4829.

Results of the individual pull requests, measured as paired A/B rounds
against the previous `master` when each was merged, are recorded in
#4829 (publish cadence: 50 × 15 × 600 delivered 67.7 % → 97.8 %),
#4837 (event delivery: 768 → 1001 generated events/s at 50 × 15) and #4859
(event queueing split). They show the size of each change on its commit,
not the current behaviour.

### Recommended baseline

Measured on the machine and workloads above; check it against your own
deployment:

- **Server GC on** (`ServerGarbageCollection`). It raised throughput by
  45–89 % over workstation GC in every workload above and cut the pause time
  by a factor of 8–13.
- **DATAS at its default.** Disabling it (`GCDynamicAdaptationMode=0`) cut GC
  pauses 4.6–8 times at 1.9–2.8 times the working set, without a throughput
  difference beyond the ranges. Choose it for latency-sensitive servers that
  can afford the memory; `GCHeapCount` caps the heaps.
- **`MaxQueuedRequestCount` at least the pipelining of all clients**, so that
  established sessions do not receive `BadServerTooBusy` while CPU is idle.
- **Default scheduling** (no affinity) unless CPU time matters more than peak
  throughput. On this hybrid CPU, the server restricted to the performance
  core complex reached a similar median throughput at about half the CPU; on
  a host shared with other processes this frees the remaining cores. Measure
  the placement on the deployment host, since the gain comes from this
  processor's core types and core complexes. See
  [Hybrid CPUs and processor affinity](ServerScalability.md#hybrid-cpus-and-processor-affinity).
- **Compare priority boost on and off on small Windows hosts.** In the
  one-logical-processor measurement, disabling it cut the 99.9th-percentile
  Read latency from 32–56 ms to about 3 ms; see
  [Small Windows machines and thread priority boost](ServerScalability.md#small-windows-machines-and-thread-priority-boost).
- **Measure at steady state**, after the JIT warm-up.

The baseline changes when the workload changes its bottleneck: node managers
that block (raise `MaxRequestThreadCount`), many subscriptions or event
monitored items (watch delivery, latency and GC pauses, not only
throughput), memory limits (DATAS on, `GCHeapCount`), or a host shared with
other processes (affinity). See
[Runtime and host settings](ServerScalability.md#runtime-and-host-settings) for
each setting and how to verify that it is in effect.

### Measuring

Follow the [measurement method](../perf/README.md#measurement-method) of the
harness: allocated bytes per operation as the primary metric, CPU per
operation with the server pinned (`--server-affinity` and
`SERVER_DOTNET_PROCESSOR_COUNT`) to one core type, the native scenarios at
[steady state](../perf/README.md#steady-state-parameters), and throughput
only from paired, alternating rounds: `perf/ab.sh` for two builds,
`perf/variants.sh` for configurations of one build (5 rounds, medians
with ranges), discarding rounds during which other test hosts or load
harnesses ran on the machine. Record every published number with the
[run record](../perf/README.md#run-record) of the harness.

### Open measurements

Tracked in
[#4842](https://github.com/OPCFoundation/UA-.NETStandard/issues/4842):

- Linux, and Intel performance/efficiency-core processors (GC modes and
  processor placement).
- A dedicated benchmark host without other background work, to narrow the
  throughput ranges.
- Session establishment and steady-state session scaling on the current
  stack (the [session scalability](#server-session-scalability) results are
  historical).

### Historical results (2026-10-18)

Single runs on the same machine, `master` 014f99a53, .NET SDK 10.0.401 and
runtime 10.0.12, before the worker wake-up (#4833) and event delivery changes;
superseded by the [reference numbers](#reference-numbers) above:

| Server | Processors | Throughput | Server CPU / Read | Allocated / Read |
|---|---|--:|--:|--:|
| UA-.NETStandard | 4 logical, pinned (`0xF`) | — | 8.8 µs | 2.6 KB |
| open62541 (single-threaded) | — | 117k req/s | 8.1 µs | — |
| UA-.NETStandard | 24 logical | 574k req/s | 27.5 µs | — |
| UA-.NETStandard, 10 × 1000 in flight | 24 logical | 518k req/s | — | — |
| UA-.NETStandard, Basic256Sha256 SignAndEncrypt | 24 logical | 447k req/s | — | — |

## Server session scalability

*Historical record: measured on an earlier 2.0 build, before the 2.0.2 publish
cadence change (#4829); the host is described under
[Observed scaling](#observed-scaling).*

The `[Explicit]` macro test
`ServerManySessionsLoadTestAsync(int sessionCount)` in
`tests/Opc.Ua.Sessions.Tests/LoadTest.cs` exercises the reference server with
many concurrent sessions. Each session opens a secure channel and creates a
slow-publishing subscription (1000 ms) with one monitored item on a shared
value node. A separate writer session changes that value periodically. The
test checks that every session receives value-change notifications during a
steady-state window. It uses `Basic256Sha256` (sign and encrypt) and requires
all sessions to connect and receive notifications.

The test offers these session counts:

- 500 (baseline)
- 1000, 1500, 2000, 2500
- 4000, 5000, 8000, 10000 (stress case)

Select a case by name, for example `ServerManySessionsLoadTestAsync(2000)`.

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

On comparable hardware, **1.5.378** capped a single node at about **2000
concurrent sessions**. Each held long-poll `Publish` pinned a request
processing worker, so the worker pool—not the CPU—became the bottleneck as
sessions increased.

Version **2.0** decouples held Publishes from the worker budget by default
(`ServerConfiguration.DecoupleHeldPublishRequests`). A small worker pool can
therefore serve many parked Publishes. The limit shifts to session
establishment, where CPU-bound RSA handshakes support about **4000** sessions
on the six-core machine below.

These measurements used an **Intel Xeon W-2235 (6 physical cores / 12 logical
threads), 64 GB RAM, 64-bit Windows**. The client and in-process reference
server shared a developer machine under light background load. Both ends
competed for the same cores, so `Basic256Sha256` RSA handshakes on the client
and server dominated establishment time. Throughput declined as concurrency
increased.

A per-cycle sweep parallelized across cores delivered steady-state Publishes.
For every count that established successfully, all sessions received 100% of
their notifications within the steady-state window (0 drops).

| Concurrent sessions | Sessions establish | Average sessions/sec created | All sessions receive notifications |
| --- | --- | --- | --- |
| 500 | Yes | 26 | Yes |
| 1000 | Yes | 15 | Yes |
| 1500 | Yes | 11 | Yes |
| 2000 | Yes | 14 | Yes |
| 2500 | Yes | 13 | Yes |
| 4000 | Yes | 11 | Yes |
| 10000 | No (*) | — | — |

(*) These figures depend on hardware and load. They are directional and may
improve on dedicated hardware with separate client and server machines.

Enable `ServerConfiguration.DecoupleHeldPublishRequests` (the default) and
set `MaxRequestThreadCount` to active session-establishment concurrency
(about 200). On this six-core machine, that setup established every session
and delivered 100% of notifications up to **~4000** concurrent sessions.
At 10000 sessions, repeated
handshake aborts and retries caused a secure-channel connect storm, with tens
of thousands of channel attempts. The server did not establish all sessions
within the connect budget. On this machine, session establishment—not
notification delivery—limits capacity; more cores can raise the limit.

If sessions connect but do not receive notifications, the last table column
shows `No (N drops)`. `N` is the number of sessions that missed notifications
during the window. See [Server Session Scalability](ServerScalability.md) for
the code-referenced analysis and controls for graceful degradation under load.

### Subscription transport buffer pooling

`BufferManager` previously stored a one-byte ownership cookie after the usable transport data in all builds. Configuring `TransportQuotas.MaxBufferSize` to an exact power of two such as 65,536 could therefore call `ArrayPool<byte>.Rent(65,537)`, selecting the 131,072-byte bucket. The cookie is now enabled in Debug builds (and explicit `TRACK_MEMORY` diagnostic builds) but removed from normal Release builds; the secure-channel normalization still keeps data plus any active diagnostic cookie in the same bucket.

Channels now normalize their advertised send and receive limits to the largest protocol-safe value that keeps the data plus cookie in the same `ArrayPool` bucket (while preserving the OPC UA 8,192-byte minimum), and both TCP transport creation paths receive that normalized limit. A configured value of either 65,535 or 65,536 consequently uses 65,536-byte arrays instead of 131,072-byte arrays.

The explicit net10.0 harness
`SubscriptionArrayPoolDiagnosticsLoadTests`
(`tests/Opc.Ua.Sessions.Tests/SubscriptionArrayPoolDiagnosticsLoadTests.cs`)
measures real subscription traffic in three cases: 255 and 257 session
Publish queues (just below and above the parallel Publish threshold), plus a
slow consumer.

Across both configured buffer sizes:

- Every scenario used only the 65,536-byte bucket.
- Rent and return counts balanced after quiescence.
- Outstanding buffers returned to zero; peak outstanding buffers ranged from
  528 to 536.
- No Gen 2 collections occurred during measurement.

The 500-session regression baseline also completed for `opc.tcp`, `https`,
and `opc.https`. All 500 sessions received notifications.

The net10.0 in-process short `BufferManagerBenchmarks` run completed 256 `BufferManager.TakeBuffer`/`ReturnBuffer` pairs in 56.18 us versus 52.52 us for direct `ArrayPool<byte>.Shared` use, with no managed allocation reported for either pooled path. These short-run timings are directional; the subscription harness is the authoritative ownership and bucket-size check.

### Sizing and configuration

* `MaxSessionCount` (default 100) caps the concurrent open sessions; size `MaxChannelCount` (one channel per session) and `MaxSubscriptionCount` above the target session count.
* `MaxRequestThreadCount` caps concurrent request processing. With
  `ServerConfiguration.DecoupleHeldPublishRequests` enabled (the default), a
  held long-poll `Publish` releases its worker while it waits. Set
  `MaxRequestThreadCount` for the number of sessions being established at
  once, not the total number of open sessions. In these measurements,
  100–200 workers served thousands of sessions. A very large pool
  oversubscribes cores during connection bursts and slows establishment.
  `MinRequestThreadCount` workers are started with the server; the
  server does not change the thread pool's limits.
  See [Request workers and the request queue](ServerScalability.md#request-workers-and-the-request-queue)
  for the current behaviour of these settings and of `MaxQueuedRequestCount`.

  Set `DecoupleHeldPublishRequests = false` to restore the legacy behavior.
  Held Publishes then occupy workers. A server handling N sessions needs a
  pool well above N to prevent other services from returning
  `BadRequestTimeout`.
* `MaxFailedAuthenticationAttempts` (default 5; `0` disables) is the brute-force lockout threshold, keyed per client certificate. A single-certificate client that opens many sessions can trip it on transient handshake failures, after which further sessions are rejected with `BadUserAccessDenied`; disable or raise it for such clients.
* Session establishment is CPU-bound (RSA handshakes) but parallelizes across cores; throttle/stagger concurrent connects and scale cores for bulk connection throughput.

> A full macro benchmark that produces a capability matrix (sessions × subscriptions × items against CPU/memory) across configurations is planned as follow-up work.
