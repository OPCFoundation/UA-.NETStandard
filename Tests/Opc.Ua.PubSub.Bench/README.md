# Opc.Ua.PubSub.Bench

BenchmarkDotNet suite covering the four hot paths of the Part 14
v1.05.06 PubSub stack: UADP encode/decode, JSON encode/decode,
scheduler tick dispatch, and AES-128-CTR sign+encrypt.

## Quick smoke pass

The dry-run smoke pass takes ~10 seconds, runs every benchmark exactly
once, and emits a summary table that can be diffed for catastrophic
regressions:

```pwsh
dotnet run -c Release -p Tests/Opc.Ua.PubSub.Bench `
  -f net10.0 -- --job dry --filter '*' --inProcess
```

The reference output for the most recent commit is checked in at
[`Baselines/baseline-net10-dry.md`](Baselines/baseline-net10-dry.md).

The `--inProcess` flag forces `InProcessEmitToolchain`. Without it
BenchmarkDotNet generates a satellite project that doesn't honour our
solution's `Directory.Build.props` and `Directory.Build.targets`, so
the source generators don't run (`MODELGEN003`). The in-process
toolchain runs benchmarks in the BDN host process and is the only
toolchain that works without bespoke BDN configuration.

## Real benchmark runs

`--job dry` is **not** statistically valid (one warm-up + one
iteration). For real numbers use one of the longer jobs:

```pwsh
# ~5 minutes total. Single launch, ~3 iterations per benchmark.
dotnet run -c Release -p Tests/Opc.Ua.PubSub.Bench `
  -f net10.0 -- --job short --filter '*' --inProcess

# ~30 minutes total. Multiple launches, ~15 iterations each.
dotnet run -c Release -p Tests/Opc.Ua.PubSub.Bench `
  -f net10.0 -- --job medium --filter '*' --inProcess

# ~3 hours total. The defaults — full statistical pipeline.
dotnet run -c Release -p Tests/Opc.Ua.PubSub.Bench `
  -f net10.0 -- --filter '*' --inProcess
```

Filter to one suite to iterate locally:

```pwsh
dotnet run -c Release -p Tests/Opc.Ua.PubSub.Bench `
  -f net10.0 -- --filter '*UadpEncoding*' --inProcess
```

Output lands under `BenchmarkDotNet.Artifacts/results/` next to the
project. To save outside the repo:

```pwsh
dotnet run -c Release -p Tests/Opc.Ua.PubSub.Bench `
  -f net10.0 -- --filter '*' --inProcess `
  --artifacts $env:USERPROFILE\bench-results
```

## Baselines

`Baselines/` holds the smoke-pass summary tables that this commit was
verified against.

- [`baseline-net10-dry.md`](Baselines/baseline-net10-dry.md) — dry job
  on net10.0.

When a hot-path change is intentional, regenerate the baseline by
re-running the smoke pass and committing the updated table in the same
PR.

## Suites

- `UadpEncodingBenchmarks` — UADP `EncodeAsync` / `TryDecodeAsync`
  across SingleField (UInt32), TenFields (mixed primitives), HundredFields
  (mixed primitives), Strings (10×64 char fields), LargeArray (Float[256]).
- `JsonEncodingBenchmarks` — Same dataset shapes, two encoder modes
  (`Verbose`, `Compact`).
- `SchedulerBenchmarks` — `IPubSubScheduler` register-and-dispatch
  latency across 1, 10, 100, 1000 concurrent schedules.
- `SecurityBenchmarks` — `UadpSecurityWrapper` AES-128-CTR sign+encrypt
  wrap/unwrap across 64, 256, 1024-byte payloads.

All suites use `[MemoryDiagnoser]` so every result table includes per-op
allocation in the `Allocated` column.
