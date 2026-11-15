# Server load floors

`Invoke-LoadFloors.ps1` runs [ServerLoadHarness](../README.md) scenarios and checks the results
against the floors and ceilings in `load-floors.json`. It catches server throughput and delivery
regressions that the NUnit `LoadTest` fixture in `tests/Opc.Ua.Sessions.Tests` does not see:
that fixture runs client and server in one process, is `[Explicit]`, and passes as long as the
notifications arrive eventually.

The floors run nightly and on pull requests labelled `stress`, in the `Server load floors` job of
[stress-test.yml](../../.github/workflows/stress-test.yml). They are not a required check.

```bash
dotnet build perf/ServerLoadHarness -c Release -p:CustomTestTarget=net10.0
pwsh perf/regression/Invoke-LoadFloors.ps1            # 3 rounds, about 12 minutes
pwsh perf/regression/Invoke-LoadFloors.ps1 -Rounds 1 -Only write
```

Exit code 0 means every check passed, 1 means a check failed, and 2 means a scenario produced
no result. The summary (Markdown, also written to the GitHub job summary), `results.json` and the
raw harness result blocks are written to `-OutDirectory` (default `TestResults/load-floors`).

## What is measured

The server runs out of process under Server GC (`SERVER_DOTNET_gcServer=1`), and so does the
client (`DOTNET_gcServer=1`), unless the caller sets these variables. All scenarios use
Basic256Sha256 SignAndEncrypt. Each round runs every scenario once, in file order, with 6 s of
warm-up and a 10 s measurement window. A check compares the median over the rounds.

| Scenario | Shape |
|---|---|
| `calibration` | `read`, 8 sessions, 100 values per Read, closed loop. Only used as the denominator of the rate checks. |
| `write` | `write`, 8 sessions, 100 values per Write, closed loop, no monitored items. |
| `lt-sub` | The Sessions `LoadTest` subscribe shape scaled for a 4-core runner: 10 sessions × 15 subscriptions × 100 nodes of the Int16…UInt64 set, publishing 100 ms, one writer every 150 ms, classic client, queue size 2 (about 100,000 notifications/s). |
| `lt-events` | The `LoadTest` events shape: 50 sessions × 15 event subscriptions on the Server object; the server reports BaseEvents as fast as one thread can. |

| Check | Bound | Master 2026-11-15 (24 cores) | Catches |
|---|---|---|---|
| `lt-sub.delivered` | ≥ 0.90 | 1.00–1.01 | Per-notification cost or contention that saturates the server. #4757/#4834 delivered 21–26 % on the full `LoadTest` shape. |
| `lt-sub.allocB` | ≤ 140 B | 103–117 B | Server bytes per delivered notification. |
| `lt-sub.allocB2` | ≤ 21,000 B | 15.7–17.6 KB | Server bytes per written value, including its fan-out to 150 monitored items. |
| `lt-events.delivered` | ≥ 0.90 | 0.98–1.00 | Events dropped or delivered late. |
| `lt-events.generatedPerSec / calibration.reqPerSec` | ≥ 0.01 | 0.085 (one G1 collapse round: 0.018) | Event fan-out cost: #4757 took the rate from ~4,000 to ~125 events/s (32×). |
| `lt-events.allocB2` | ≤ 150,000 B | 88–97 KB; 145–163 KB in a round with a G1 collapse | Server bytes per event for 750 subscriptions: #4757 was ~5 MB. E6 (+50 %) lands near the ceiling. |
| `write.valuesPerSec / calibration.valuesPerSec` | ≥ 0.2 | 0.54–0.62 | Write throughput 2.5× below master, for example a request-wide lock. |
| `write.allocB` | ≤ 98,000 B | 85.3–85.8 KB | Server bytes per 100-value Write. X1 (#4999) added 13.5 KB. |

`allocB` and `allocB2` are the harness `per-op` and `per-op2` values: allocated bytes of the
server process divided by the operations of the window. They vary by a few percent from run
to run and do not depend on machine speed, so their ceilings are tight (10–30 % above master).
Throughput varies by ±20–35 % from run to run on a shared machine, so a rate is checked only as
a ratio to the `read` calibration of the same round, with a floor several times below master.
Those floors catch collapses, not 10 % regressions. Latency (`p50Ms`, `p99Ms`), CPU per operation,
GC pause and lock contention are reported in the summary but not checked.

Ceilings are absolute. When an improvement lands (for example the X1 fix brings `write.allocB`
to about 72 KB), lower the ceiling in the same pull request so the gain is kept.

## Validating a floor

The harness can simulate a regression in the server child: `--inject-alloc <bytes>` allocates
and `--inject-spin-us <us>` burns CPU per written value (in `Write`) and per reported event. The
product code is unchanged. Pass the options through `-ExtraArgs`:

```bash
pwsh perf/regression/Invoke-LoadFloors.ps1 -Rounds 1 -Only write -ExtraArgs "--inject-alloc 200"
pwsh perf/regression/Invoke-LoadFloors.ps1 -Rounds 1 -Only lt-events -ExtraArgs "--inject-alloc 60000 --inject-spin-us 10000"
pwsh perf/regression/Invoke-LoadFloors.ps1 -Rounds 1 -Only lt-sub -ExtraArgs "--inject-alloc 6000"
```

On Windows, `-ExtraArgs "--client-cores 23"` leaves the server one processor, which simulates a
server that needs more CPU per notification than it has.

## Calibrating on a new runner

The values in the table were measured on a 24-core Windows machine. Byte counts should carry
over to Linux runners, but rates and delivered % depend on the core count. After changing the
runner or the shapes, run the script with the bounds of a check widened and set the bound from
the measured median.
