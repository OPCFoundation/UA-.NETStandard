# Performance Benchmarks: 2.0 vs 1.5.378

This document compares the **2.0** stack against the previous **1.5.378** release on
.NET 10, explains **what improved and why**, **what is still slower and why**, and the
**future work** planned to close the remaining gaps.

All ratios below are **2.0 ÷ 1.5.378**. **< 1.0 means 2.0 is faster / allocates
less; > 1.0 means 2.0 is slower / allocates more.**

It is intended as living documentation: when a perf-sensitive change lands, update
the relevant section and re-run the affected benchmark class (see
[Running the benchmarks](#running-the-benchmarks)).

## Why 2.0 differs from 1.5.378

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

## What was measured (2.0 vs 1.5.378, 409 matched benchmarks)

- **Allocation geomean: 0.93×** — 2.0 allocates **less** than 1.5.378 overall.
- **Time geomean: ~1.07×** — 2.0 is slightly slower on aggregate, concentrated in
  two areas only: the **binary encoder** and **session establishment**. Everything
  else is at parity or faster.

| Area | time | alloc |
|---|--:|--:|
| Client read / browse (`ClientTest`, `RequestHeaderTest`) | **0.67–0.79×** | **0.5–0.65×** |
| JSON encode, in-memory (`JsonEncoderTests`) | **0.97×** | **0.60×** |
| Binary decode (`BinaryDecoderBenchmarks`) | 1.16× | **0.64×** |
| JSON encode, external stream (`JsonEncoderBenchmarks`) | 1.33× | 1.29×² |
| Binary encode (`BinaryEncoderBenchmarks`) | ~1.6× | ~1.04× core¹ |
| Session establishment (`SecurityPolicyBenchmarks`) | still slower³ | — |

¹ The plain `MemoryStream` binary-encode variant allocates ~1.04× vs 1.5.378 (core
encoder near parity); the `ArraySegmentStream` / `RecyclableMemoryStream` wrapper
variants are ~1.33–1.63× due to those stream types' own buffer management, not the
encoder.
² External-stream JSON allocation improved from ~1.44× to ~1.29× once the
`Utf8JsonWriter` is pooled/reused (below); the residual is inherent UTF-8 transcoding.
³ Session establishment remains the largest single regression vs 1.5.378; this is
detailed below.

---

## What improved in 2.0 vs 1.5.378 (and why)

### Lower allocation almost everywhere

Aggregate allocation across the 409 matched benchmarks is **0.93×**. The value-type
redesign means most messages, node ids, values, and byte strings no longer allocate
individual heap objects. This is the headline win of 2.0 and shows up across client,
server, and encode paths.

### Client read / browse: ~20–33% faster, ~half the allocation

- `ClientTest.BrowseFullAddressSpaceBenchmarkAsync`: **~30% faster** across every
  security policy (e.g. ~315 ms → ~213 ms).
- `RequestHeaderTest.ReadValues…`: **~20–25% faster**, **~0.5×** allocation.

The async pipeline plus value-type request/response handling cut both CPU and
garbage on the most common client operation (reading/browsing the address space).

### In-memory JSON encoding: faster *and* ~40% leaner

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

### Binary decoding: 36% less garbage, CPU close to parity

Binary decode allocates **0.64×** vs 1.5.378 (the value-type `DataValue` removes the
per-field heap object) while staying close on CPU at **1.16×**. Decoding is on the hot
receive path of every client and server, so the allocation win applies broadly.

> **Target-framework note:** the quoted decode allocation (0.64×) is a **.NET 10**
> number. On legacy target frameworks the figure is higher — see
> [Scope: legacy target frameworks](#scope-legacy-target-frameworks).

### Faster low-level primitives

`UtilsIsEqual*` and `HiResClock*` are faster on 2.0 (e.g. byte-array compares
~25% faster), reflecting span-based and modern-API implementations.

---

## What is still slower in 2.0 vs 1.5.378 (and why)

### Binary encoder — ~1.5× CPU (the main remaining regression)

Encoding a `Variant`/`DataValue` walks the 2.0 `readonly struct` accessor chain in
`WriteVariantValue` / `WriteDataValue`. Each property read can copy the large struct
and/or re-run the built-in-type switch. In the micro-benchmark,
`WriteDataValueArray` (a 10-element `DataValue[]`) dominates (~65% of the payload
iteration) because every element pays that struct cost. 1.5.378 used a
reference-type `DataValue`/`Variant` whose accessors were plain field reads, so this
is the direct CPU cost of the allocation-reducing value-type design. Closing this gap
to parity is tracked under [Future work](#future-work).

**Impact:** encode CPU on the send path, absolute cost a few microseconds per
message. The bulk-array fast-path materially helps realistic large-array payloads
(PubSub datasets, historical values); the residual mostly shows up in synthetic
array-heavy micro-benchmarks. Core-path allocation is near parity (~1.04× on the
plain `MemoryStream` variant).

### Binary decoder — 1.16× CPU

Same value-type struct-accessor cost as the encoder, much smaller. Offset by the
0.64× allocation win above.

### JSON encoder, external-stream — improved by `Utf8JsonWriter` pooling

The external-stream JSON variants (`JsonEncoderBenchmarks`, which write to a caller
stream) trailed 1.5.378 because each encoder allocated a fresh `Utf8JsonWriter` and
pays `Utf8JsonWriter` UTF-8 transcoding (vs 1.5.378's `StreamWriter`). 2.0 now
**pools and reuses `Utf8JsonWriter` instances** for the external-stream and
external-`IBufferWriter` constructors: a writer is rented from a small pool (keyed by
`Indented`, capped) and re-targeted with `Utf8JsonWriter.Reset(...)` instead of being
allocated per encoder, then returned on dispose. This removes the per-encoder writer
allocation; the residual is the inherent UTF-8 transcoding. The in-memory path
remains pooled via the `ArrayPool<byte>` buffer described above.

### Session establishment — still slower; dominated by discovery + crypto

`SecurityPolicyBenchmarks` (`'Create and close session'` / `'Session lifecycle with
read'`) remains the largest single regression vs 1.5.378. These are **end-to-end**
client+server benchmarks (channel + crypto handshake + server session setup), **not**
an encoder path.

2.0 also supports **additional security policies** (e.g. the ECC AES-GCM /
ChaCha20-Poly1305 suites) that 1.5.378 does not. Each configured policy adds per-session
cost on the server side, so a benchmark run that includes the new policies is not a
like-for-like comparison. **A benchmark restricted to the security policies common to
both 1.5.378 and 2.0 is needed to measure the true apples-to-apples change**; the
geomeans above are inflated by the extra policies.

An allocation profile of one connect (Basic256Sha256) shows the ~3.2 MB/op is
dominated by **per-connect endpoint discovery** (`ConnectAsync` runs `GetEndpointsAsync`
every iteration) plus strings / `Char[]` / XML / `Byte[]` materialization:

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
> benchmark tracks endpoint discovery in isolation. Reducing the discovery
> materialization itself (strings / `UserTokenPolicy` / XML) remains future work on
> the discovery path.

### `JsonEncoderTests.ServiceMessageContext` — ~2× on a tiny absolute

~122 ns vs ~63 ns (~0.94× alloc). Namespace/node-id-heavy message-context encoding;
the absolute cost is tens of nanoseconds and is partially absorbed by the `NodeId`
work above. Low priority.

---

## Future work

1. **Binary encoder — drive to parity or better.** The remaining encoder gap is the
   per-element readonly-struct accessor cost in `WriteVariantValue` /
   `WriteDataValueArray`. Target 1.5.378 parity (or better) by specialising the scalar
   built-in-type writes and using `System.Buffers.Binary.BinaryPrimitives` /
   vectorised (SIMD) writes for bulk numeric and array payloads, rather than the
   per-element accessor chain.
2. **Session establishment — apples-to-apples + discovery materialization.** Add a
   benchmark restricted to the security policies common to both 1.5.378 and 2.0 to
   measure the true like-for-like change, and reduce the
   endpoint/user-token/app-description materialization and XML reader/writer
   allocation in `GetEndpointsAsync` on the connect path (optionally caching
   `ConfiguredEndpoint` so repeat connects skip discovery).

## Scope: legacy target frameworks

2.0 perf optimization targets the modern runtime (.NET 10). The **.NET Framework
(net48)** and **netstandard2.0** target frameworks are **not** a performance
optimization target for 2.0: where a fast path is gated on `NET6_0_OR_GREATER` (for
example the `stackalloc` / `ArrayPool<byte>` `BinaryDecoder.ReadString` path), the
legacy frameworks keep the simpler allocating fallback. Allocation/throughput figures
in this document are .NET 10 numbers and legacy-TFM behaviour may be higher; that gap
will not be closed for 2.0.

## Running the benchmarks

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

## Environment and caveats

- Runtime: **.NET 10.0** (`--runtimes net10.0`), Release config.
- Host: BenchmarkDotNet v0.15.x, Windows 11 on a **shared Hyper-V VM**, Intel Xeon
  Platinum 8473C, 8 physical / 16 logical cores, .NET SDK 10.0.30x.
- **Virtualization caveat:** BenchmarkDotNet warns that a shared/virtualized host
  affects measurements. Treat absolute numbers as indicative and **sub-10% deltas as
  noise**; focus on the direction and magnitude of large deltas.
- The 1.5.378 baseline is a full-job run; the per-class encoder refresh on 2.0 is a
  faster, higher-variance ShortRun, so treat those ratios as directional. Non-encoder
  classes carry run-to-run variance.
