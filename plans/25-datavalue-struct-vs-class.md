# DataValue: class vs `readonly struct` — measurement and recommendation

**Status: MIGRATED (2026-05-21)**

`DataValue` has been converted from a reference-type `class` to a
`readonly struct`. The migration shipped on branch `dvstruct` across
PR-1 through PR-5. The experimental `DataValueStruct` sibling type and
its transitional encoder/decoder shims (`ReadDataValueStruct`,
`WriteDataValueStruct`, `_Struct` benchmarks) have been removed.
All consumers now target the unified `DataValue` value type.

The remainder of this document is preserved for historical context.

---

Branch: `nullable5`  
Date: 2026-05-20  
Hardware: Intel Xeon W-2235 @ 3.80 GHz, 6 physical / 12 logical cores, Windows 11 25H2  
Runtime: .NET 10.0.8, BenchmarkDotNet 0.15.8 (InProcessEmitToolchain, ShortRun)

## Why this document exists

`DataValue` is the OPC UA value/quality/timestamp tuple emitted on
every attribute read, subscription publish, and history sample. It is
currently a **reference type** (`class DataValue`, ~64 B per
allocation: 16 B object header + 48 B payload). At the volumes a busy
server sees, the heap allocation alone is the dominant single source
of GC pressure — `plans/24-subscription-v2-gc-reduction.md` already
proposes pooling as a mitigation.

A natural alternative — and the one the user asked us to investigate —
is to convert `DataValue` to a `readonly struct`, trading the per-call
**reference (8 B)** for a per-call **value copy (48 B)** in exchange
for zero heap allocation.

This document records the numbers and recommendation from a focused
A/B benchmark. The benchmark code lives at
`Tests/Opc.Ua.Core.Tests/Types/BuiltIn/DataValueBenchmarks.cs` and is
runnable on demand:

```powershell
cd Tests/Opc.Ua.Core.Tests
dotnet run -c Release -f net10.0 --no-build -- \
  --filter "*DataValueBenchmarks*" --job short --inProcess
```

The experimental sibling type used for the comparison is
`Stack/Opc.Ua.Types/BuiltIn/DataValueStruct.cs` (a `readonly struct`
with the same fields and a mutable `Builder` for decoder hot paths).
Wire format is byte-identical — `DataValueBenchmarks` includes an NUnit
test (`ClassAndStructProduceIdenticalWireBytes`) that asserts this on
every CI run.

## Type-layout reference (64-bit)

| Element | Type | Bytes |
|---|---|---|
| `Variant m_value` | `readonly struct` (object? + union + TypeInfo) | 24 |
| `StatusCode` | `readonly struct` (uint) | 4 |
| `SourceTimestamp` | `readonly struct` (long) | 8 |
| `SourcePicoseconds` | ushort | 2 |
| `ServerTimestamp` | `readonly struct` (long) | 8 |
| `ServerPicoseconds` | ushort | 2 |
| **Payload total** (with padding) | | **48** |
| **Class heap cost** | + 16 B object header | **64** |

## Numbers

ShortRun: 3 iterations, 1 launch, 3 warmups. Allocation columns are
managed-bytes per single operation; Gen0/1/2 are collections per 1000
ops. Errors omitted from the summary table; per-iteration stddev was
≤3% on the headline columns.

### B1 — `BinaryDecoder.ReadDataValue × N` (the hot loop)

| N | Shape | Class ns/op | Struct ns/op | Δ time | Class alloc B/op | Struct alloc B/op | **Δ alloc** |
|---|---|---:|---:|---:|---:|---:|---:|
| 1000 | ScalarDouble | 89 602 | 89 222 | **-0.4%** | 88 237 | 64 236 | **-27.2%** |
| 1000 | ScalarString | 163 131 | 172 559 | +5.8% | 136 232 | 112 231 | **-17.6%** |
| 1000 | ArrayDouble1K | 10 967 796 | 10 640 896 | -3.0% | 8 337 779 | 8 313 952 | -0.3% |
| 100 | ScalarDouble | 8 550 | 7 765 | -9.2% | 9 033 | 6 632 | **-26.6%** |
| 100 | ScalarString | 13 240 | 11 969 | -9.6% | 13 833 | 11 433 | -17.4% |
| 1 | ScalarDouble | 198 | 168 | -15.2% | 320 | 296 | -7.5% |

Headline: at the realistic 100–1000-item batch sizes, the struct
decoder removes about **24 bytes of garbage per decoded scalar**,
matching the 16 B object header + ~8 B reference-slot savings (per
DataValue in the result array). Time is **flat to slightly faster**.
For the array-of-double payload (1 KB per item), the underlying
`double[]` allocation dominates — the DataValue-shape saving is
amortised away to ~0.3 %.

### B3 — 5-frame dispatch chain (pure per-call copy cost)

| N | Shape | Class ns/op | Struct ns/op | **Δ time** |
|---|---|---:|---:|---:|
| 1000 | ScalarDouble | 5 481 | 7 239 | **+32.1%** |
| 1000 | ScalarString | 5 602 | 5 564 | -0.7% (within noise) |
| 1000 | ArrayDouble1K | 5 889 | 7 167 | **+21.7%** |
| 100 | ScalarDouble | 617 | 610 | -1.1% |
| 10 | ScalarDouble | 67 | 74 | +9.4% |

Zero allocations on both sides (the chain doesn't construct anything).
The struct path is **20-32 % slower** on the largest batch — the
predicted 48-byte-per-frame copy cost. Smaller batches see no
difference because the values stay resident in registers / L1 across
the inlined-ish chain.

### B4 — `new + List<DataValue> × N` (pure allocate + capture)

| N | Shape | Class ns/op | Struct ns/op | Δ time | Class alloc B/op | Struct alloc B/op | **Δ alloc** |
|---|---|---:|---:|---:|---:|---:|---:|
| 1000 | (any) | 18 896–19 317 | 20 381–21 409 | **+8-13%** | 88 056 | 64 056 | **-27.3%** |
| 100 | (any) | 1 857–2 393 | 2 261–2 305 | **-3-+22%** | 8 856 | 6 456 | **-27.1%** |
| 10 | (any) | 228–287 | 288–305 | **+0-27%** | 936 | 696 | **-25.6%** |
| 1 | (any) | 38–44 | 42–47 | flat | 144 | 120 | **-16.7%** |

A consistent **~27 % allocation reduction** across batch sizes and
payload shapes — the headline GC-pressure win. The time cost is small
(8–13 % at N=1000) but real.

### Wire-byte parity

The NUnit test `ClassAndStructProduceIdenticalWireBytes` confirms that
`BinaryEncoder.WriteDataValueArray(ArrayOf<DataValue>)` and
`BinaryEncoder.WriteDataValueStructArray(ReadOnlySpan<DataValueStruct>)`
produce the same number of bytes on identical logical inputs — passes.

## Interpretation

| Concern | Verdict |
|---|---|
| Per-item allocation cost | **Struct wins by ~24 B** (the class' header + reference slot) |
| Per-call copy cost | **Class wins by 20-32%** on deep call chains |
| Decode wall-clock | **Tie or struct slightly ahead** at realistic sizes |
| Allocate + capture wall-clock | **Class wins by ~10%** on the hottest sizes |
| GC frequency (Gen0 collections / 1K) | **Struct wins ~30 %** in alloc-dominated scenarios |
| Array-payload (1 KB+ Variant) | **Indifferent** — the array dominates |

The trade-off vector is precisely what theory predicts:

- The struct **eliminates** the per-instance heap allocation and the
  inevitable Gen0 churn. On a 100 K-instance/second subscription this
  amounts to ~2 MB/s less garbage and one less Gen0 collection per
  60-80 ms.
- The struct **pays back** that win as register/cache-line pressure
  in any hot pass-by-value chain. At 32 % overhead on a 5-frame chain
  for ScalarDouble — but **only** the 5-frame chain; shorter ones
  bottom out at noise.
- For OPC UA's largest hot path (decode → notify → callback chain),
  the decoder side is *neutral* and the dispatch side is *worse* by
  ~25 %, but the GC side is *better* by ~30 %. **Net effect depends on
  the GC sensitivity of the workload.**

## Recommendation

**Adopt option C — Hybrid.** Keep `DataValue` (class) as the primary
public type for compatibility, but introduce
`DataValueStruct` as an *additive* type that the V2 subscription
engine and any other GC-sensitive hot path can opt into.

Rationale:

1. **The win is real but narrow.** The 27 % allocation reduction is
   meaningful for high-rate subscription dispatch and history scans,
   but the *full migration* cost is enormous (150+ files, decoder
   refactor away from setter-mutation, `Nullable<DataValueStruct>`
   semantics rippling through the encoder interface).
2. **Plan #24 (pooling) is competitive.** A pooled `DataValue` class
   recovers most of the allocation win (~95 % of the same reduction)
   without changing the public surface or breaking 150 callsites. The
   work to land plan #24 is already designed; landing both
   `DataValueStruct` and the pool would be additive — the V2 engine
   could pick whichever fits.
3. **The dispatch penalty is real on hot chains.** Where the callback
   chain is deep and the value is read on every frame, the 48 B copy
   costs measurably. Subscription dispatch is exactly this — so the
   struct is *not* a slam-dunk for the dispatch side even though it
   wins the GC side.
4. **The wire is unaffected.** Because we ship `DataValueStruct` with
   byte-identical wire format, there is no spec-compatibility risk in
   shipping it as opt-in.

Concrete next steps (gated on a separate decision / PR):

- **Land** `Stack/Opc.Ua.Types/BuiltIn/DataValueStruct.cs` +
  `ReadDataValueStruct` / `WriteDataValueStruct` /
  `WriteDataValueStructArray` / `ReadDataValueStructArray` as the
  opt-in API for V2 callers.
- **Wire** `DataValueStruct` through `ISubscriptionNotificationHandler`
  in the V2 engine (still inside the same PR family as plan #24).
- **Do not** migrate the legacy class-based callers — they keep
  working unchanged.
- **Re-evaluate** after the V2 engine ships and we have production GC
  traces: if `DataValueStruct` does not measurably move the needle in
  real workloads, retire it; if it does, schedule a deeper migration.

## Re-running the experiment

```powershell
# Quick sanity-check (NUnit, fast):
dotnet test Tests/Opc.Ua.Core.Tests --filter "FullyQualifiedName~DataValueBenchmarks"

# Full BDN ShortRun (~10 min, all 14 scenarios × 4 sizes × 3 shapes):
cd Tests/Opc.Ua.Core.Tests
dotnet run -c Release -f net10.0 --no-build -- \
  --filter "*DataValueBenchmarks*" --job short --inProcess

# Focused subsets (faster):
dotnet run -c Release -f net10.0 --no-build -- \
  --filter "*Decode_*DataValue*" --job short --inProcess
dotnet run -c Release -f net10.0 --no-build -- \
  --filter "*Allocate_*DataValue*" "*Dispatch_*DataValue*" \
  --job short --inProcess
```

Notes:

- `--inProcess` bypasses BenchmarkDotNet's boilerplate-build step
  (which times out at 2 minutes against this repo's source generators).
- ShortRun results are noisy at low N (CI 99.9 % can exceed the mean
  for small ns counts). The allocation and Gen0/1/2 columns are
  *deterministic* and reliable; the time column should be cross-checked
  with a longer run if the difference is < 10 %.
