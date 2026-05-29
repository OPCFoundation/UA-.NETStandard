# V2 Subscription Notification Pooling — Benchmark Results

Microbenchmark results for the `IPooledEncodeable` + `PooledEncodeableType<T>`
activator pooling feature added in support of `ManagedSessionBuilder.WithPoolNotifications()`.

## Source

`Tests/Opc.Ua.Client.Tests/Subscription/PooledNotificationBenchmarks.cs`

## Methodology

- BenchmarkDotNet v0.15.8, `[MemoryDiagnoser]`, `InProcessEmitToolchain` (avoids
  the cold-rebuild timeout that the default toolchain hits on the test
  assembly's transitive reference graph).
- `ShortRun` job: 3 warmup iterations, 3 measured iterations, single launch.
- Inner loop is `Iterations = 1000` allocate/use cycles per benchmark
  invocation — measured values are aggregate over the inner loop.
- Pre-warm in `[GlobalSetup]` populates all four type pools to steady-state
  before the first measured iteration so the pool-hit fast path is exercised
  rather than the cold-pool `new T()` fallback.

## Environment

| Item | Value |
|---|---|
| OS | Windows 11 (10.0.26200.8390 / 25H2) |
| CPU | Intel Xeon W-2235 @ 3.80 GHz, 6 physical / 12 logical cores |
| SDK | .NET SDK 10.0.300 |
| Host runtime | .NET 10.0.8 (RyuJIT, x86-64-v4) |
| Config | Release, `-c Release -f net10.0` |

## Results

Results measured after the `DataValue` readonly-struct change. `DataValue`
is now a `readonly struct` that lives inline in
`MonitoredItemNotification.Value`, eliminating one heap allocation per
notification item compared to the previous class-based `DataValue`.

| Method | Mean | Allocated/op | Allocation ratio | Gen0/1000 ops |
|---|---:|---:|---:|---:|
| `new MonitoredItemNotification()` *(baseline)* | 34.874 µs | 104,408 B | 1.000 | 24.17 |
| `MonitoredItemNotificationActivator + Reuse` | **23.323 µs** | **408 B** | **0.004** | **0.092** |
| `new DataChangeNotification()` | 66.759 µs | 216,344 B | 2.072 | 50.05 |
| `DataChangeNotificationActivator + Reuse` | **54.812 µs** | **32,344 B** | **0.310** | **7.45** |
| `new EventFieldList()` | 8.967 µs | 48,408 B | 0.464 | 11.22 |
| `EventFieldListActivator + Reuse` | **22.280 µs** | **408 B** | **0.004** | **0.092** |

### Comparison: DataValue as class vs readonly struct

| Metric (MonitoredItemNotification, 1000 ops) | DataValue = class | DataValue = readonly struct | Change |
|---|---:|---:|---|
| Baseline allocated/op | 128,408 B | 104,408 B | **−24 KB** (−19%) — `DataValue` heap object eliminated |
| Pooled allocated/op | 408 B | 408 B | unchanged — pool already recycled the whole notification |
| Baseline Gen0/1000 ops | 29.75 | 24.17 | **−19%** — fewer heap objects to collect |
| Pooled Gen0/1000 ops | 0.092 | 0.092 | unchanged |
| Pooled allocation reduction | 315× | 256× | ratio lower because baseline is now cheaper |

## Interpretation

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
  (array pooling is out of scope for this phase).

For `EventFieldList`:

- The baseline `new` path is already cheap (~9 µs) because the empty
  `EventFields` `ArrayOf<Variant>` is a zero-allocation default. The
  pooled path adds `Interlocked.CompareExchange` + pool round-trip
  overhead (~22 µs). Under realistic dispatch where each `EventFieldList`
  carries a non-empty `EventFields` array, the allocation savings
  dominate and the pooled path wins.

### How the two optimizations stack

The `DataValue` readonly-struct change and activator pooling are
**complementary**:

- **Struct `DataValue`** helps all paths — Read, Browse, Call, and
  unpooled publish. Every `DataValue` that was previously a separate
  heap allocation is now inline in its parent (−24 B per notification
  item, −19% baseline allocation).
- **Activator pooling** helps the publish path specifically — it
  recycles the notification wrapper objects (`MonitoredItemNotification`,
  `DataChangeNotification`, `EventFieldList`, `EventNotificationList`)
  that the struct change does not address.
- Combined: the pooled publish path allocates **256× less** than a
  baseline that is itself **19% cheaper** than before.

## Reproducing

```pwsh
# Build release
dotnet build Tests/Opc.Ua.Client.Tests -c Release -f net10.0

# Run BDN harness
cd Tests/Opc.Ua.Client.Tests/bin/Release/net10.0
dotnet Opc.Ua.Client.Tests.dll --filter "*PooledNotificationBenchmarks*"
```

Artifacts (markdown, csv, html) are written to
`Tests/Opc.Ua.Client.Tests/bin/Release/net10.0/BenchmarkDotNet.Artifacts/results/`.

## Out of scope

The following payloads are not pooled in this phase and remain
attributable to the residual allocation in the pooled
`DataChangeNotification` numbers above:

- `Variant` value payload (arbitrary user data inside `DataValue.Value`)
- Dispatch backing arrays (`DataValueChange[]`, `EventNotification[]`,
  `MonitoredItemNotification[]`)

`DataValue` is now a readonly struct and no longer allocates on the
heap. It is not an `IEncodeable` and does not participate in the
activator pool system.
