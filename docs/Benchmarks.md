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
  - [Reference numbers](#reference-numbers)
  - [Reproducing the reference numbers](#reproducing-the-reference-numbers)
  - [Publish and event delivery](#publish-and-event-delivery)
  - [Recommended baseline](#recommended-baseline)
  - [Measuring](#measuring)
  - [Open measurements](#open-measurements)
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

### Reference numbers

| Run record | Value |
|---|---|
| Date, commit | 2026-10-18, `master` 014f99a53 (`2.0.2-preview`), Release build of `perf/ServerLoadHarness` |
| Host | AMD Ryzen AI 9 HX 370: 4 Zen 5 and 8 Zen 5c cores, SMT, 24 logical processors; 64 GB RAM |
| OS, power policy | Windows 11 Home (build 26300), *Balanced* power plan |
| SDK, runtime | .NET SDK 10.0.401, .NET 10 runtime (10.0.12 installed when this table was written) |
| Server | Harness child in benchmark-contract mode: `AsyncCustomNodeManager`, Server GC with DATAS (runtime default), `MaxRequestThreadCount` 200, `MaxQueuedRequestCount` clients × in flight + 3 |
| Client, placement | 10 native open62541 Read worker processes on the same machine, not pinned; the server pinned only where the table says so |
| Workload | Single-value Reads of `ns=1;i=1001..1100`, 100 in flight per worker unless noted, no security unless noted |
| Duration | Steady state: 100,000 warm-up and 300,000 measured Reads per worker |
| Repeats | Single runs per row; ranges were not recorded (see [Open measurements](#open-measurements)) |

These are single-run observations from a shared machine. Compare CPU and
allocated bytes per Read alongside throughput; repeated paired runs are still
needed to quantify the spread.

| Server | Processors | Throughput | Server CPU / Read | Allocated / Read |
|---|---|--:|--:|--:|
| UA-.NETStandard | 4 logical, pinned (`0xF`) | — | 8.8 µs | 2.6 KB |
| open62541 (single-threaded) | — | 117k req/s | 8.1 µs | — |
| UA-.NETStandard | 24 logical | 574k req/s | 27.5 µs | — |
| UA-.NETStandard, 10 × 1000 in flight | 24 logical | 518k req/s | — | — |
| UA-.NETStandard, Basic256Sha256 SignAndEncrypt | 24 logical | 447k req/s | — | — |

In these runs, restricting the .NET server to 4 logical processors gave a CPU
cost per Read close to the single-threaded open62541 result. On all 24 logical
processors, the recorded throughput was about five times higher, while CPU per
Read was about three times the pinned .NET result. Compare core types and SMT
placement to investigate that scaling cost. See
[Hybrid CPUs and processor affinity](ServerScalability.md#hybrid-cpus-and-processor-affinity).

Short runs include JIT warm-up: with the benchmark's default
parameters (10,000 warm-up and 100,000 measured calls per client) the same
server shows about 60 µs per Read on 24 logical processors, and 3-second runs
on 4 pinned processors 28–31 µs. See
[JIT warm-up](ServerScalability.md#jit-warm-up).

### Reproducing the reference numbers

Build the harness and the native workers as described in
[perf/README.md](../perf/README.md#external-benchmark-workers), then run one
command per row from `perf/ServerLoadHarness/bin/Release/net10.0`
(`<workers>` is the build directory of the benchmark workers). Each run prints a
`RESULT` block; `--out <file>` appends it to a file, which is the measurement
artifact to keep with the published numbers.

```bash
# UA-.NETStandard, 4 logical processors pinned
SERVER_DOTNET_PROCESSOR_COUNT=4 ./ServerLoadHarness.exe run --scenario native-read --native-client <workers>/client.exe --server-affinity 0xF --iterations 300000 --warmup-calls 100000 --out reference.txt
# open62541 server (single-threaded)
./ServerLoadHarness.exe run --scenario native-read --native-client <workers>/client.exe --server-exe <workers>/server.exe --server-args "--port {port}" --iterations 300000 --warmup-calls 100000 --out reference.txt
# UA-.NETStandard, all 24 logical processors
./ServerLoadHarness.exe run --scenario native-read --native-client <workers>/client.exe --iterations 300000 --warmup-calls 100000 --out reference.txt
# 10 x 1000 in flight
./ServerLoadHarness.exe run --scenario native-read --native-client <workers>/client.exe --inflight 1000 --iterations 300000 --warmup-calls 100000 --out reference.txt
# Basic256Sha256 SignAndEncrypt
./ServerLoadHarness.exe run --scenario native-read --native-client <workers>/client.exe --security encrypt --iterations 300000 --warmup-calls 100000 --out reference.txt
```

Throughput is `client: req/s`, CPU and allocated bytes per Read are
`per-op: cpuUs` and `allocB`, and the GC counts and pause time of the window are
on the `server:` line.

### Publish and event delivery

The 2.0.2 line changed the server paths that deliver notifications. The
results below were measured in the pull requests that made the changes, with
paired, alternating A/B rounds of `perf/ServerLoadHarness` against the
previous `master` on the 24-logical-processor machine above, Server GC,
Basic256Sha256 SignAndEncrypt, and medians (#4829) or means (#4837) of the
rounds. They show the size of each change, not the capacity of a deployment.

**Publish cadence (#4829).** The publish timer now runs on a fixed cadence and
checks the remaining session queues in parallel once a pass has used a quarter
of the publishing resolution. `sub` scenario, publishing interval 100 ms,
writes every 150 ms:

| Sessions × subscriptions × items | Rounds | Delivered (received/expected) | Server CPU / notification |
|---|--:|--:|--:|
| 50 × 15 × 600 | 3 | 67.7 % → 97.8 % | 5.5 → 4.4 µs |
| 10 × 15 × 600 | 3 | 100.4 % → 99.9 % | 5.5 → 5.1 µs |
| 2 × 1 × 100 | 3 | 100 % → 100 % | 48.5 → 46.1 µs |

At 50 sessions the allocation per notification rose from about 54 to 96 B
and the GC pause in the 15-second window from 20–50 to about 60 ms, because
the faster sweep exceeds the notification pool's bound; the PR describes the
cause.

**Event delivery (#4837).** One notifier thread delivers each event to every
event monitored item, so the per-item cost bounds the event rate. `events`
scenario, 50 sessions × 15 subscriptions on the Server object, publishing
interval 100 ms, events generated as fast as possible, 2 rounds per build,
recorded with `SERVER_DOTNET_TC_QuickJitForLoops=0` and
`SERVER_DOTNET_TC_OnStackReplacement=0`:

| Build | Generated events/s | Event → notification p50 | Allocated / generated event | CPU / generated event |
|---|--:|--:|--:|--:|
| before | 768 | 69.8 ms | 567 KB | 2.97 ms |
| #4837 | 1001 | 61.3 ms | 272 KB | 2.66 ms |

**Request scheduling.** The Read results above (10 × 100 and 10 × 1000 in
flight) predate the worker wake-up changes in #4833. They are not measurements
of that implementation. The settings and their current behaviour are described in
[Request workers and the request queue](ServerScalability.md#request-workers-and-the-request-queue).

### Recommended baseline

Measured on the machine above; check it against your own deployment:

- **Server GC on** (`ServerGarbageCollection`): the reported comparison showed
  about twice the request rate of the workstation GC under deep pipelines.
  Keep DATAS at its default unless
  measured pause-time improvements justify the memory cost of a fixed heap count.
- **`MaxQueuedRequestCount` at least the pipelining of all clients**, so that
  established sessions do not receive `BadServerTooBusy` while CPU is idle.
- **Default scheduling** (no affinity). Restricting the server to the
  performance cores of a hybrid CPU reached the same throughput at about half
  the CPU in one measurement; adopt it only after measuring your workload.
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
only from paired, alternating A/B rounds with `perf/ab.sh` (5 rounds, medians
with ranges), discarding rounds during which other test hosts or load
harnesses ran on the machine. Record every published number with the
[run record](../perf/README.md#run-record) of the harness: commit or package
version, SDK and runtime, OS, CPU topology, affinity, power policy, memory,
security, client and server placement, workload and duration.

### Open measurements

These are scheduled in
[#4842](https://github.com/OPCFoundation/UA-.NETStandard/issues/4842) and not
yet measured on the current `master`; until then the guidance above marks the
corresponding settings as options to measure:

- Workstation GC, Server GC with DATAS, and Server GC without DATAS, side by
  side on one commit (throughput, CPU and allocation per operation, GC count
  and pause time, working set).
- Default scheduling against performance-core affinity on a hybrid CPU, with
  repeated paired rounds and ranges, on Windows and Linux.
- The reference Read table above with 5 paired rounds and ranges.
- Publish (`sub`) and event (`events`) delivery on the merged 2.0.2 `master`:
  delivered share, notification latency, CPU and allocation per notification
  and GC pauses alongside the rate.
- Session establishment and steady-state session scaling on the current
  stack.

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
  `MinRequestThreadCount` prewarms the pool to avoid thread-pool cold start.
  See [Request workers and the request queue](ServerScalability.md#request-workers-and-the-request-queue)
  for the current behaviour of these settings and of `MaxQueuedRequestCount`.

  Set `DecoupleHeldPublishRequests = false` to restore the legacy behavior.
  Held Publishes then occupy workers. A server handling N sessions needs a
  pool well above N to prevent other services from returning
  `BadRequestTimeout`.
* `MaxFailedAuthenticationAttempts` (default 5; `0` disables) is the brute-force lockout threshold, keyed per client certificate. A single-certificate client that opens many sessions can trip it on transient handshake failures, after which further sessions are rejected with `BadUserAccessDenied`; disable or raise it for such clients.
* Session establishment is CPU-bound (RSA handshakes) but parallelizes across cores; throttle/stagger concurrent connects and scale cores for bulk connection throughput.

> A full macro benchmark that produces a capability matrix (sessions × subscriptions × items against CPU/memory) across configurations is planned as follow-up work.
