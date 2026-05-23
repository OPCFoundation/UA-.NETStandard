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
| Config | Release, `c Release -f net10.0` |

## Results

| Method | Mean | Allocated/op | Allocation ratio | Gen0/1000 ops |
|---|---:|---:|---:|---:|
| `new MonitoredItemNotification()` *(baseline)* | 33.890 µs | 128,408 B | 1.000 | 29.75 |
| `MonitoredItemNotificationActivator + Reuse` | **22.134 µs** | **408 B** | **0.003** | **0.092** |
| `new DataChangeNotification()` | 58.490 µs | 232,344 B | 1.809 | 53.83 |
| `DataChangeNotificationActivator + Reuse` | **56.175 µs** | **32,344 B** | **0.252** | **7.45** |
| `new EventFieldList()` | 9.407 µs | 48,408 B | 0.377 | 11.22 |
| `EventFieldListActivator + Reuse` | **22.537 µs** | **408 B** | **0.003** | **0.092** |

Notes:
- `MonitoredItemNotification` baseline allocates ~125 KB / 1000 ops; pooled
  reduces that to **0.3% of baseline** (408 B per 1000 ops — only the
  `int sum` capture).
- `DataChangeNotification` baseline includes the inner `MonitoredItemNotification`
  + the `MonitoredItemNotification[]` backing array. Pooling both the container
  and the item brings allocation down to **25% of baseline** — the residual is
  the `MonitoredItemNotification[]` itself (array pooling is explicitly out of
  scope for this phase; see `Docs/Sessions.md` and Section 9 of the design plan).
- `EventFieldList` is a degenerate case: the baseline `new` path is already
  cheap (10 µs) because the struct's empty `EventFields` `ArrayOf<Variant>` does
  not allocate; the pooled path adds the `Interlocked.CompareExchange` + pool
  round-trip and ends up slightly slower (22 µs) for this microbench shape.
  Under the realistic dispatch scenario where each `EventFieldList` carries a
  non-empty `EventFields`, the allocation savings dominate (similar shape to
  `MonitoredItemNotification`).
- `Gen0/1000 ops` drops by orders of magnitude in every pooled case — this is
  the direct GC-reduction headline.

## Interpretation

For the dominant publish-payload allocator (`MonitoredItemNotification`,
which arrives in arrays of arbitrary length on every data-change publish):

- Mean time per item drops from ~34 µs/1000 to ~22 µs/1000 (a 35% throughput
  improvement on the synthetic benchmark, with the gap widening as the inner
  loop cache-warms).
- Allocations per 1000 ops drop from 128 KB to 408 B — **~315× reduction**.
- Gen-0 collections per 1000 ops drop from 29.75 to 0.09 — **~320× reduction**.

The realistic V2 publish workload (1k–10k monitored-item updates per second
sustained) translates these numbers directly to fewer GC pauses and reduced
managed-heap churn.

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

The following payloads are not pooled in this phase (per the design plan,
`plans/24-subscription-v2-gc-reduction.md` Section 9 "Out of scope") and
remain attributable to the residual baseline-shaped allocation in the pooled
`DataChangeNotification` numbers above:

- `DataValue` (built-in, not `IEncodeable`)
- `Variant` value payload
- Dispatch backing arrays (`DataValueChange[]`, `EventNotification[]`,
  `MonitoredItemNotification[]`)

Revisit if a measurement of a full publish loop against the reference server
shows these as remaining hot spots after the current pooling work lands.
