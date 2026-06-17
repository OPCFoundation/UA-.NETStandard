# PubSub benchmarks — net10.0 dry baseline

> **Generated:** Phase 12 commit. Captured by:
>
> ```pwsh
> dotnet run -c Release -p Tests/Opc.Ua.PubSub.Bench -f net10.0 \
>     -- --job dry --filter '*' --inProcess
> ```
>
> `--job dry` = single warm-up + single iteration per benchmark. The mean
> values below are **dry-run only** and are not statistically significant —
> use them only to detect catastrophic regressions (e.g. order-of-magnitude
> allocation jumps). For real numbers run `--job short` or `--job medium`
> (see [`README.md`](../README.md)).
>
> **Host:** BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200), Intel Xeon
> W-2235 @ 3.80 GHz, .NET SDK 10.0.301, Host = .NET 10.0.9 (RyuJIT
> x86-64-v4), `Toolchain=InProcessEmitToolchain`, `Job=Dry`.

## JSON encoder / decoder (`JsonEncodingBenchmarks`)

| Method                       | Mean      | Error | Allocated |
|----------------------------- |----------:|------:|----------:|
| Encode_Verbose_TenFields     |  2.457 ms |    NA |   6.26 KB |
| Encode_Compact_TenFields     |  2.584 ms |    NA |   8.76 KB |
| Encode_Verbose_SingleField   |  2.462 ms |    NA |   4.58 KB |
| Encode_Verbose_HundredFields |  3.477 ms |    NA |  58.69 KB |
| Encode_Verbose_Strings       |  2.948 ms |    NA |  12.38 KB |
| Encode_Verbose_LargeArray    |  3.836 ms |    NA |   9.34 KB |
| Decode_SingleField           | 12.383 ms |    NA |   5.30 KB |
| Decode_TenFields             |  2.659 ms |    NA |  19.68 KB |
| Decode_HundredFields         |  1.997 ms |    NA | 156.74 KB |

## Scheduler tick dispatch (`SchedulerBenchmarks`)

| Method                   | TaskCount | Mean     | Error | Allocated |
|------------------------- |---------- |---------:|------:|----------:|
| RegisterAndDispatchAsync | 1         | 23.55 ms |    NA |   7.11 KB |
| RegisterAndDispatchAsync | 10        | 30.25 ms |    NA |   9.62 KB |
| RegisterAndDispatchAsync | 100       | 20.97 ms |    NA |  40.09 KB |
| RegisterAndDispatchAsync | 1000      | 29.82 ms |    NA | 327.25 KB |

## Security wrap / unwrap (`SecurityBenchmarks`)

AES-128-CTR sign+encrypt round-trip per NetworkMessage.

| Method      | PayloadSize | Mean     | Error | Allocated |
|------------ |------------ |---------:|------:|----------:|
| WrapAsync   | 64          | 2.795 ms |    NA |   7.62 KB |
| UnwrapAsync | 64          | 6.483 ms |    NA |   7.21 KB |
| WrapAsync   | 256         | 2.454 ms |    NA |   7.80 KB |
| UnwrapAsync | 256         | 2.300 ms |    NA |   6.05 KB |
| WrapAsync   | 1024        | 2.590 ms |    NA |   7.95 KB |
| UnwrapAsync | 1024        | 2.473 ms |    NA |   6.70 KB |

## UADP encoder / decoder (`UadpEncodingBenchmarks`)

| Method               | Mean     | Error | Allocated |
|--------------------- |---------:|------:|----------:|
| Encode_SingleField   | 2.229 ms |    NA |   5.37 KB |
| Encode_TenFields     | 2.275 ms |    NA |   7.84 KB |
| Encode_HundredFields | 2.485 ms |    NA |  26.56 KB |
| Encode_Strings       | 2.167 ms |    NA |   7.84 KB |
| Encode_LargeArray    | 3.046 ms |    NA |   8.01 KB |
| Decode_SingleField   | 7.139 ms |    NA |   6.54 KB |
| Decode_TenFields     | 1.984 ms |    NA |   8.56 KB |
| Decode_HundredFields | 1.713 ms |    NA |  37.09 KB |
| Decode_Strings       | 2.997 ms |    NA |  11.98 KB |
| Decode_LargeArray    | 2.435 ms |    NA |   9.53 KB |

## Notes

- The `LargeArray` shape is `Float[256]` rather than `Float[1024]` because
  the current UADP encoder caps the initial encode buffer at 4 KB and only
  catches `ArgumentException` during retry; a `Variant` of
  `Float[1024]` (~4 KB pure payload) overflows the inner
  `BinaryEncoder` with a `NotSupportedException` that bypasses the retry
  loop. This is a pre-existing encoder limitation unrelated to the
  benchmark; track in a follow-up.
- The dry baseline is intentionally tiny (one iteration each). It exists
  to detect *gross* regressions in CI; do not read absolute timings from
  it.
