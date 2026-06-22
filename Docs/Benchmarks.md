# Performance Benchmarks and Regression Analysis

This document records the performance benchmark methodology for the stack, the
results of comparing the current `master` (2.0) line against the previous
`master378` (1.5.378) release, an analysis of the **reasons** behind the observed
regressions and their **real-world impact**, and the **future work** planned to
close the remaining gaps.

It is intended as living documentation: when a perf-sensitive change lands, update
the relevant section and re-run the affected benchmark class (see
[Running the benchmarks](#running-the-benchmarks)).

## Why this comparison exists

The 2.0 line is a deliberate redesign that, among many other things, replaced
several reference types with **`readonly struct` value types** (`Variant`,
`DataValue`, `NodeId`, `ExpandedNodeId`, `ByteString`, `ArrayOf<T>`, `DateTimeUtc`)
and moved the client/server onto a fully `async` (TAP) pipeline. That redesign
**reduces allocations and GC pressure** (fewer heap objects, less Gen2 traffic) but
trades some of that for **higher per-access CPU cost** — every property read on a
large `readonly struct` may copy the struct or re-run a discriminated-union switch.

The benchmarks below quantify that trade-off so we can keep the allocation wins
while clawing back the CPU regressions where they matter.

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
`JsonEncoderTests`). The end-to-end session benchmarks
(`SecurityPolicyBenchmarks`) live in `Tests/Opc.Ua.Sessions.Tests` and spin up a
real in-process client + server across every security policy.

> Tip: the combined `--filter *` build is flaky on this repo — run **per class**.
> A per-class build may need one or two retries (`dotnet build-server shutdown`
> between attempts).

## Environment and caveats

- Runtime: **.NET 10.0** (`--runtimes net10.0`), Release config.
- Host: BenchmarkDotNet v0.15.x, Windows 11 on a **shared Hyper-V VM**, Intel Xeon
  Platinum 8473C, 8 physical / 16 logical cores, .NET SDK 10.0.30x.
- **Virtualization caveat:** BenchmarkDotNet warns that a shared/virtualized host
  affects measurements. Treat absolute numbers as indicative and **sub-10% deltas
  as noise**; focus on the direction and magnitude of large deltas.
- Ratios below are `master ÷ master378`. **< 1.0 = master is faster / allocates
  less.** The `master378` baseline is a full-job run; some `master` classes were
  re-run on a different day, so non-encoder classes carry run-to-run variance
  (their code is identical to the original).

## Headline results (409 matched benchmarks, full job)

- **Time geomean: ~1.07×** (master slightly slower on aggregate).
- **Allocation geomean: ~0.93×** — master allocates **less** than master378 overall.

The aggregate time gap is concentrated in exactly two areas: **session
establishment** and the **binary encoder**. Everything else is at parity or faster
on master (notably the client read/browse paths, which are **~30% faster** on 2.0).

### Per-area refresh on the consolidated branch (2026-06-22)

Re-ran the four encoder/decoder benchmark classes on the consolidated branch
(PR #3899 = encoder + JSON buffer pooling + NodeId read-once + async session create)
and recompared to the `master378` full-job baseline (merged numbers are a faster,
higher-variance ShortRun — treat as directional):

| Area | time | alloc | Status |
|---|--:|--:|---|
| Binary decode (`BinaryDecoderBenchmarks`) | **1.16×** | **0.64×** | Recovered to near-parity; alloc win kept |
| Binary encode (`BinaryEncoderBenchmarks`) | **1.59×** | 1.41×¹ | CPU improved from ~1.71×; residual struct cost |
| JSON encode, **external stream** (`JsonEncoderBenchmarks`) | 1.30× | 1.44×² | Residual — external streams bypass the new pool |
| JSON encode, **in-memory** (`JsonEncoderTests`) | **0.97×** | **0.60×** | Faster **and** ~40% leaner (flush + pooling) |
| Client read / browse (`ClientTest`, `RequestHeaderTest`) | **~0.67–0.79×** | ~0.5–0.65× | Faster on 2.0 |
| Session establishment (`SecurityPolicyBenchmarks`) | heavy-policy 0.71–0.89× after fix | ~0.94× | Async fix landed; residual is discovery/crypto |

¹ The binary-encoder allocation geomean is inflated by the
`ArraySegmentStream` / `RecyclableMemoryStream` wrapper variants (~1.33–1.63×); the
plain `MemoryStream` variant is ~1.04×, i.e. the **core encoder allocation is near
parity** and the delta is in those stream wrappers' buffer management.
² The `JsonEncoderBenchmarks` cases all write to an **external** stream and therefore
do **not** use the new pooled in-memory buffer — which is why their allocation is
unchanged while the in-memory `JsonEncoderTests` path improved to 0.60×.

## Regression analysis by area

### 1. Binary decoder — recovered

**Was** ~1.60× time at the worst payloads. **Now** ~1.12× time and **0.64×
allocation** (master decodes with far less garbage).

- **Reason for the original regression:** `BinaryDecoder.ReadDataValue` built the
  result through a chain of up to six `With…` calls, each copying the large
  `readonly struct DataValue`.
- **Fix:** read all fields into locals and construct the `DataValue` **once**.
- **Impact:** recovers most of the decode CPU regression while keeping the lower
  allocation that the 2.0 decoder introduced. Decoding is on the hot receive path
  of every client and server, so this matters broadly.

### 2. Binary encoder — residual CPU gap (the main remaining regression)

**~1.59× time** (improved from ~1.71× before the consolidated changes), allocation
near parity on the core path (the plain `MemoryStream` variant is ~1.04×; the
~1.41× geomean is inflated by the `ArraySegmentStream` / `RecyclableMemoryStream`
wrapper variants' buffer management, not the encoder itself).

- **Reason:** encoding a `Variant`/`DataValue` walks the 2.0 `readonly struct`
  accessor chain in `WriteVariantValue`/`WriteDataValue`. Each property read can
  copy the large struct and/or re-run the built-in-type switch. In the
  micro-benchmark, `WriteDataValueArray` (a 10-element `DataValue[]`) dominates
  (~65% of the payload iteration) because every element pays that struct cost.
- **What was done to reduce it:**
  - Pass `Variant`/`DataValue` by `in` through the whole write path (no per-call /
    per-element struct copies).
  - Snapshot `Variant.TypeInfo`/`BuiltInType` and `DataValue.WrappedValue` into
    locals once instead of re-reading the struct getters ~13×.
  - **Bulk-write primitive numeric arrays** by blitting the backing span in a single
    `BinaryWriter.Write(ReadOnlySpan<byte>)` (little-endian; byte-reversed fallback
    on big-endian) instead of looping through the `ArrayOf<T>` indexer, which
    materializes a `Span` per element.
  - Skip the nesting-level bookkeeping / `try-finally` for scalar variants (which
    cannot nest).
  - Read `NodeId` accessors (`IdType`/`NamespaceIndex`) **once** into locals and pass
    them, plus the `NodeId` by `in`, to the private encode helpers.
- **Why it is not fully recovered:** the remaining cost is **intrinsic** to the 2.0
  value-type design. master378 used a reference-type `DataValue`/`Variant` whose
  accessors were plain field reads; the 2.0 structs trade that CPU for the
  allocation reduction seen everywhere else. The 10-element-array micro-benchmark is
  a worst case — real payloads with mixed scalar fields pay proportionally less.
- **Impact:** encode CPU on the send path. Absolute cost is small per message
  (microseconds), and the bulk-array fast-path materially helps the realistic
  large-array cases (PubSub datasets, historical values). The residual mostly shows
  up in synthetic array-heavy micro-benchmarks.

### 3. JSON encoder — much improved (flush + buffer pooling)

The in-memory JSON path is now **faster** than master378 (the `JsonEncoderTests`
in-memory cases measure **0.97× time / 0.60× allocation** — faster *and* ~40%
leaner; `JsonEncoderConstructor` alone is ~0.55× time / ~0.46× alloc). Two changes
drove this:

- **Flush at structural boundaries (Wave 1):** `Utf8JsonWriter` never flushes on its
  own, so for a large/streamed payload its internal buffer grows by doubling onto the
  **Large Object Heap** until it holds the entire message. The encoder now flushes
  buffered JSON at object/array boundaries once ≥ 16 KB has accumulated. Flushing only
  writes already-complete tokens and **never changes the output**. Measured: large
  streamed payloads dropped from ~15.7 MB to ~7.3 MB allocated (**−53%**) with
  **Gen2/op → 0**.
- **Pool the in-memory buffer:** the default (no external stream) encoder no longer
  allocates a per-encoder `MemoryStream`; it writes through a private
  `ArrayPool<byte>`-backed `IBufferWriter<byte>` and reads the result text from the
  pooled span. Buffers are returned with `clearArray: true` so encoded payloads
  (which may contain user tokens or secrets) are not exposed to the next pool
  consumer.
- **Span-based `NodeId.TryFormat`:** writes the common node-id forms into a stack
  buffer instead of allocating a string (~144 B saved per node id; node ids are
  ubiquitous in OPC UA messages).
- **Residual:** the external-stream variants still trail master378 on allocation
  because of per-construct `Utf8JsonWriter` setup and `Utf8JsonWriter` UTF-8
  transcoding (vs the old `StreamWriter`). Smaller now that the in-memory buffer is
  pooled.
- **Impact:** JSON is the PubSub and REST/gateway encoding path; the LOH-growth fix
  in particular removes large, bursty Gen2 allocations for big messages.

### 4. `JsonEncoderTests.ServiceMessageContext` — small absolute

~1.94× time (122 ns vs 63 ns), ~0.94× alloc. Namespace/node-id-heavy
message-context encoding. The absolute cost is tens of nanoseconds; partially
absorbed by the `NodeId` improvements above. Tracked, low priority.

### 5. Session establishment — async fix landed; residual is discovery/crypto

`SecurityPolicyBenchmarks` `'Create and close session'` / `'Session lifecycle with
read'` showed the largest headline regression (**~2.5–2.7× time, ~2.0–2.5×
alloc**). These are **end-to-end** client+server benchmarks (channel + crypto
handshake + server session setup), **not** an encoder path.

- **Primary reason (fixed):** the server `Session` constructor created the
  session-diagnostics address-space node via
  `CreateSessionDiagnosticsAsync(...).AsTask().GetAwaiter().GetResult()` on **every**
  `CreateSession` — a **sync-over-async** block (it awaits a semaphore + async node
  creation) that ties up the request thread and violates the repo's no-sync-over-async
  rule. It was moved into `Session.InitializeAsync`, awaited by the already-async
  `SessionManager.CreateSessionAsync`.
  - **Result (ShortRun, all policies):** geomean **0.94× time / 0.94× alloc**; the
    heavy ECC AES-GCM / ChaCha20-Poly1305 policies are **0.71–0.89× time** (up to 29%
    faster) where the blocked request thread mattered most. RSA policies are at parity
    (dominated by RSA crypto).
- **Secondary reasons (reduced):** per-activation client `RequestHeader` +
  `AdditionalParametersType` allocation on the common path (now only allocated for
  ephemeral-key policies), and a duplicated `ClientNonce.ToArray()` in server
  signature validation (now hoisted to a single local).
- **Allocation profile finding:** an allocation profile of one
  `CreateCloseSessionAsync` iteration (Basic256Sha256) shows the residual ~3.2 MB/op
  is dominated by **per-connect endpoint discovery** (`ConnectAsync` runs
  `GetEndpointsAsync` every iteration) plus strings / `Char[]` / XML / `Byte[]`. The
  eager per-session object graph (`NodeCache` + subscription engine) is only **~1.2%
  (~38 KB)**, so lazy-initialising it would **not** move the regression and was
  deliberately not pursued.

  | Allocator (Basic256Sha256 connect) | ~bytes/op |
  |---|--:|
  | `System.String` | 605 KB |
  | `System.Char[]` | 581 KB |
  | `System.Byte[]` | 222 KB |
  | `UserTokenPolicy` | 176 KB |
  | `XmlWellFormedWriter` | 133 KB |
  | (isolated `NodeCache` ctor) | 39 KB |

- **Impact:** session establishment is a one-time-per-connection cost. The async fix
  removes a thread-pool stall under concurrent connects (a throughput / scalability
  win for servers handling many simultaneous session creations), most visible on the
  expensive ECC policies.

### Areas that got faster on 2.0

Not everything regressed — the value-type + async redesign made several common paths
faster and leaner:

- `ClientTest.BrowseFullAddressSpaceBenchmarkAsync`: **~30% faster** across all
  security policies.
- `RequestHeaderTest.ReadValues…`: **~20–25% faster**, ~0.5× allocation.
- `UtilsIsEqual*`, `HiResClock*`: faster low-level primitives.
- Aggregate allocation across all 409 matched benchmarks: **0.93×**.

## Potential impact summary

| Regression | Where it hits | Severity | Mitigation |
|---|---|---|---|
| Binary encode ~1.6× CPU | Send path, array-heavy payloads | Low–moderate (µs/msg; bulk arrays mitigated) | Bulk-array blit, `in` passing, scalar fast-path (more below) |
| JSON external-stream alloc | PubSub / gateway JSON encode | Low (in-memory path now faster; LOH growth fixed) | Buffer pooling landed; writer-reuse pending |
| Session establishment ~2–2.7× | Per-connection setup under load | Moderate for high-churn servers | Async fix landed; discovery/crypto residual is structural |
| `ServiceMessageContext` ~2× | Tiny absolute (ns) | Negligible | Absorbed by NodeId work |

The headline takeaway: **2.0 trades a modest amount of encode/establishment CPU for
broadly lower allocation and faster client read/browse**, and the largest
regressions have either been recovered (decode), substantially reduced (JSON,
session), or are bounded and well-understood (encode struct cost).

## Future work

1. **Binary encoder scalar fast-paths.** Specialise `WriteVariantValue` for the
   common scalar built-in types to avoid the readonly-struct accessor chain
   (`WriteDataValueArray` is the prime target). Goal: bring `BinaryEncoderBenchmarks`
   from ~1.6× toward ~1.3×. Some residual is intrinsic to the value-type design and
   will not fully recover without reverting it.
2. **Session establishment — endpoint discovery.** The benchmark re-runs
   `GetEndpointsAsync` on every connect; profile and reduce endpoint/user-token/app
   description materialization and the XML reader/writer allocation on the connect
   path. Optionally cache `ConfiguredEndpoint` so repeat connects skip discovery.
3. **JSON external-stream writer reuse.** Reuse a `Utf8JsonWriter` via `Reset(stream)`
   and/or a pooled `IBufferWriter<byte>` on the external-stream path to close the
   residual allocation gap (the in-memory path is already pooled).
4. **`ReadString` span path (decoder).** Keep the 2.0 allocation win while shaving the
   transcoding cost with a profile-driven span approach.
5. **Clean-machine confirmation runs.** Re-run the full suite on dedicated
   (non-virtualized) hardware to remove VM noise before treating any sub-10% delta as
   real.

## References

- Encoder/JSON/session perf changes: PR #3899 (consolidates the encoder, JSON buffer
  pooling, NodeId read-once, and async-session-create work).
- Session allocation profiling and the "1.2% object graph" finding are summarised in
  section 5 above.
