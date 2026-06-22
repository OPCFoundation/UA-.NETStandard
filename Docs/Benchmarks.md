# Performance Benchmarks: 2.0 vs 1.5.378

This document compares the **2.0** stack (this PR merged — i.e. the future
`master`) against the previous **1.5.378** release on .NET 10, explains **what
improved and why**, **what is still slower and why**, the **real-world impact**,
and the **future work** planned to close the remaining gaps.

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

## Headline (2.0 vs 1.5.378, 409 matched benchmarks)

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
³ Session establishment remains the largest single regression vs 1.5.378; this PR
removed a server-side stall that recovered a large part of it (details below).

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
vs 1.5.378 — i.e. 2.0 both runs faster and allocates ~40% less. Three changes:

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
- **Span-based `NodeId.TryFormat`.** Writes the common node-id forms into a stack
  buffer instead of allocating a string (~144 B per node id; node ids are ubiquitous
  in OPC UA messages).

JSON is the PubSub and REST/gateway encoding path; the LOH-growth fix in particular
removes large, bursty Gen2 allocations for big messages.

### Binary decoding: 36% less garbage, CPU close to parity

Binary decode allocates **0.64×** vs 1.5.378 (value-type `DataValue` removes the
per-field heap object) while staying close on CPU at **1.16×**. 2.0's
`BinaryDecoder.ReadDataValue` constructs the `readonly struct DataValue` **once**
from locals instead of through a chain of up to six `With…` calls that each copied
the large struct. Decoding is on the hot receive path of every client and server,
so the allocation win applies broadly.

> **Target-framework note:** the quoted decode allocation (0.64×) is measured on
> **.NET 10**. `BinaryDecoder.ReadString` only uses the non-allocating
> `stackalloc` / `ArrayPool<byte>` span path on `NET6_0_OR_GREATER`; on
> **.NET Framework (net48)** and **netstandard2.0** it falls back to reading each
> string into a transient `byte[]`. Decode allocation on those legacy target
> frameworks is therefore higher than the .NET 10 figures here.

### Session establishment on the heavy ECC policies: large recovery

Session establishment is still slower than 1.5.378 overall (see below), but this PR
removed the biggest 2.0-specific offender: the server `Session` constructor created
the session-diagnostics address-space node via
`CreateSessionDiagnosticsAsync(...).AsTask().GetAwaiter().GetResult()` on **every**
`CreateSession`. That **sync-over-async** block (it awaits a semaphore + async node
creation) tied up the request thread and violated the repo's no-sync-over-async
rule. Moving it into an awaited `Session.InitializeAsync` recovered **up to ~29% of
the establishment time** on the expensive ECC AES-GCM / ChaCha20-Poly1305 policies
(where the blocked thread mattered most) and removed a thread-pool stall under
concurrent connects — a throughput / scalability win for servers handling many
simultaneous session creations.

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
is the direct CPU cost of the allocation-reducing value-type design.

Mitigations applied (these keep the gap at ~1.5× rather than higher):

- Pass `Variant`/`DataValue` by `in` through the whole write path (no per-call /
  per-element struct copies).
- Snapshot `Variant.TypeInfo`/`BuiltInType` and `DataValue.WrappedValue` into locals
  once instead of re-reading the struct getters ~13×.
- **Single-read `WriteDataValue`:** read each `DataValue` field
  (`StatusCode`/`SourceTimestamp`/`SourcePicoseconds`/`ServerTimestamp`/
  `ServerPicoseconds`) into a local **once** and reuse it for both the encoding-byte
  computation and the field write (previously each was read twice). Measured ~5%
  faster on the `DataValue[]` micro-benchmark (≈1.59× → ≈1.51×), wire output
  unchanged.
- **Bulk-write primitive numeric arrays** by blitting the backing span in a single
  `BinaryWriter.Write(ReadOnlySpan<byte>)` on modern TFMs (little-endian; byte-reversed
  fallback on big-endian) instead of looping the `ArrayOf<T>` indexer, which
  materializes a `Span` per element.
- Skip nesting-level bookkeeping for scalar built-in types that cannot recurse
  (`DataValue` and `ExtensionObject` still go through the nesting guard).
- Read `NodeId` accessors (`IdType`/`NamespaceIndex`) once into locals and pass them,
  plus the `NodeId` by `in`, to the private encode helpers.

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
an encoder path. After the sync-over-async fix above, an allocation profile of one
connect (Basic256Sha256) shows the residual ~3.2 MB/op is dominated by **per-connect
endpoint discovery** (`ClientFixture.ConnectAsync` runs `GetEndpointsAsync` every
iteration) plus strings / `Char[]` / XML / `Byte[]` materialization:

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
> `SessionLifecycleWithReadAsync` now pass the cached `Endpoints` into `ConnectAsync`,
> so they measure pure session **create / activate / close** rather than re-running
> `GetEndpointsAsync` on every iteration. A separate `DiscoverEndpointsAsync`
> benchmark tracks endpoint discovery in isolation. Reducing the discovery
> materialization itself (strings / `UserTokenPolicy` / XML) remains future work on
> the product discovery path.

### `JsonEncoderTests.ServiceMessageContext` — ~2× on a tiny absolute

~122 ns vs ~63 ns (~0.94× alloc). Namespace/node-id-heavy message-context encoding;
the absolute cost is tens of nanoseconds and is partially absorbed by the `NodeId`
work above. Low priority.

---

## Potential impact summary

| Still slower vs 1.5.378 | Where it hits | Severity | Why / mitigation |
|---|---|---|---|
| Binary encode ~1.6× CPU | Send path, array-heavy payloads | Low–moderate (µs/msg; bulk arrays mitigated) | Intrinsic readonly-struct cost; `in` + bulk-array blit + scalar skip + single-read `WriteDataValue` applied |
| JSON external-stream | PubSub / gateway JSON encode | Low (in-memory path faster; LOH growth fixed; writer now pooled) | `Utf8JsonWriter` reuse landed; residual is inherent transcoding |
| Session establishment | Per-connection setup under load | Moderate for high-churn servers | Discovery + crypto materialization; sync-over-async stall removed; benchmark now isolates establishment |
| `ServiceMessageContext` ~2× | Tiny absolute (ns) | Negligible | Absorbed by NodeId work |

Bottom line: **2.0 allocates less and is faster on the common client read/browse and
in-memory JSON paths; the remaining slowdowns are the binary-encoder value-type CPU
cost and session establishment (discovery + crypto), both bounded and understood.**

## Future work

1. **Binary encoder scalar fast-paths (continued).** The single-read `WriteDataValue`
   landed (~5% faster on the `DataValue[]` micro-benchmark; the full
   `BinaryEncoderBenchmarks` stays ~1.6× within ShortRun noise). Further specialise
   `WriteVariantValue` / `WriteDataValueArray` for the common scalar built-in types to
   avoid the readonly-struct accessor chain. Goal: toward ~1.3×. Some residual is
   intrinsic to the value-type design and will not fully recover without reverting it.
2. **Session establishment — discovery materialization.** The benchmark now isolates
   establishment from discovery; the remaining product work is to reduce the
   endpoint/user-token/app-description materialization and XML reader/writer
   allocation in `GetEndpointsAsync` on the connect path, and optionally cache
   `ConfiguredEndpoint` so repeat connects skip discovery.
3. **Legacy-TFM decode allocation.** Extend the `NET6_0_OR_GREATER`
   `stackalloc` / `ArrayPool<byte>` `ReadString` path to **net48 / netstandard2.0**
   if those targets become allocation-sensitive (see the target-framework note in the
   binary-decoding section). Currently documented rather than changed — the .NET 10
   path is already non-allocating.
4. **Clean-machine confirmation runs.** Re-run the full suite on dedicated
   (non-virtualized) hardware to remove VM noise before treating any sub-10% delta as
   real.

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
- The 1.5.378 baseline is a full-job run; the per-class encoder refresh on 2.0 (with
  this PR) is a faster, higher-variance ShortRun, so treat those ratios as
  directional. Non-encoder classes carry run-to-run variance.
