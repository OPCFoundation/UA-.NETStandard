<!-- SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL -->
<!-- SPDX-License-Identifier: MIT -->

# Server load floors

`Invoke-LoadFloors.ps1` runs [ServerLoadHarness](../README.md) scenarios and checks the results
against the floors and ceilings in `load-floors.json`. It catches server throughput and delivery
regressions that the NUnit `LoadTest` fixture in `tests/Opc.Ua.Sessions.Tests` does not see:
that fixture runs client and server in one process, is `[Explicit]`, and passes as long as the
notifications arrive eventually.

The floors run nightly, on demand from the Actions tab, and on pull requests labelled
`performance`, in [load-floors.yml](../../.github/workflows/load-floors.yml). They are not a
required check: add the label to start a run, and each later push to the pull request restarts it.

```bash
dotnet build perf/ServerLoadHarness -c Release -p:CustomTestTarget=net10.0
pwsh perf/regression/Invoke-LoadFloors.ps1            # 3 rounds, about 12 minutes
pwsh perf/regression/Invoke-LoadFloors.ps1 -Rounds 1 -Only write
```

Exit code 0 means every check passed, 1 means a check failed, and 2 means a scenario produced
no result or `-Only` named an unknown scenario or one without a check. A check fails when any
round lacks its value, and every scenario fails when a round reports a workload error (a failed
request, or a Read or Write with a rejected operation, which the harness does not count as
throughput). The summary (Markdown, also written to the GitHub job summary), `results.json` and the
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
| `write.allocB` | ≤ 29,000 B | 20.6–22.6 KB (2026-11-28; 85 KB before the X1, N3 and R0 fixes) | Server bytes per 100-value Write. X1 (#4999) added 13.5 KB. |

`allocB` and `allocB2` are the harness `per-op` and `per-op2` values: allocated bytes of the
server process divided by the operations of the window. They vary by a few percent from run
to run and do not depend on machine speed, so their ceilings are tight (10–30 % above master).
Throughput varies by ±20–35 % from run to run on a shared machine, so a rate is checked only as
a ratio to the `read` calibration of the same round, with a floor several times below master.
Those floors catch collapses, not 10 % regressions. Latency (`p50Ms`, `p99Ms`), CPU per operation,
GC pause and lock contention are reported in the summary but not checked.

Ceilings are absolute. When an improvement lands, lower the ceiling in the same pull request so
the gain is kept. `write.allocB` has room for the idle allocation of the reference server (about
5 MB/s), which adds 1–2 KB per Write at the request rate of a 4-core runner.

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

## Allocation gate

`alloc-gate.json` is a second floors file for the same script. It runs on every pull request in
[perf-gate.yml](../../.github/workflows/perf-gate.yml) (the scenarios take about 9 minutes;
most of it is fixed warm-up, baseline and measurement time) and checks only allocated bytes per operation, of the server and
of the client, for ten scenarios: Read, Write, Browse, TranslateBrowsePaths, Call, classic and
managed subscriptions, events, HistoryRead paging and connect. Each check names the path and
the earlier regression or optimization it guards.

```bash
dotnet build perf/ServerLoadHarness -c Release -p:CustomTestTarget=net10.0
pwsh perf/regression/Invoke-LoadFloors.ps1 -Floors perf/regression/alloc-gate.json
```

It must not fail on a slow or busy runner, so it checks nothing that depends on speed:

| Measure | Why |
|---|---|
| Bytes per operation only | A busy machine completes fewer operations in a window; each one allocates the same. Throughput, latency and delivered % stay in the load floors. |
| Reference server simulation off (`--simulation 0`) | The simulation assigns random values to the `Scalar_Simulation_*` variables every second, about 5 MB/s (mostly XmlElement comparisons). Divided by the operations of a starved window, that added 50-100 % to a Read. No scenario reads those variables. Without it the idle server allocates about 8 KB/s. |
| Net of the idle allocation rate (`--baseline 3`) | What is left on timers (keep-alives, publish cycles) is measured in an idle window of the same run and taken out: the gate checks `netAllocB`, `netAllocB2` and `netClientAllocB`. |
| Keys reloaded before the window (connect) | The CertificateManager reuses a loaded application key for 30 seconds (#5171/#5184); after that a connect loads it from the store, about 115 K more. The connect workload reloads the keys right before the window, so the check never depends on how long setup took. |
| Smallest of three rounds (`aggregate: min`) | Work outside the operation only ever adds bytes. |
| Confirmation (`confirmRounds: 2`) | A failed scenario runs twice more and fails the job only if it fails again. A regression fails every time. |
| Retried runs, free ports | A run without a result (a server port taken by another process) is retried up to three times. |
| Workstation GC in both processes | Allocation does not depend on the GC mode; fewer GC threads compete with the workload on a 4-core runner. |

Measured on a 24-core Windows machine (2026-11-27), idle and with 24 busy processes that cut
the request rate 20-fold: the net server bytes of the request scenarios agree within 10 %, the
client bytes within 1 %, where the plain `allocB` of a Read rose from 20.4 K to 32.6-36.1 K.
A simulated regression of 50 bytes per written value (`-ExtraArgs "--inject-alloc 50"`, +8 K
per 100-value Write) fails `write.netAllocB` in every round and confirmation round.

With the simulation on, `sub-classic.netAllocB2` (about 45 server bytes per notification) varied
by ±40 % between rounds; with it off, by ±5 %. Its ceiling is still near twice its value and
catches a fan-out regression such as #4757, not a few percent. The managed subscription scenario
checks only the client. A workload error in a confirmation round fails the job even when the
first rounds had none.

The first Linux run (ubuntu-latest, 2026-11-28) agreed with Windows within 2 % except Browse
(42 K against 50-52 K) and the client bytes of connect (264 K against 244 K); the catches text
of each check lists both. When a pull request lowers the bytes of a path, the summary lists the
ceilings that are more than twice the measured value; lower them in the same pull request. When
a change adds bytes on purpose, raise the ceiling and say why in the pull request. To recalibrate,
run the gate on the runner with `-Rounds 5` and set each ceiling 15 % above the largest round.
