<!-- SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL -->
<!-- SPDX-License-Identifier: MIT -->

# NodeManager lifecycle baselines (2026-11-24)

These are baselines for the NodeManager lifetime redesign follow-ups F3–F7 (#4931) and the
#4869 delivery gate. They use the `lifecycle-reload` and `historyread-paging` scenarios and the
`TraceAnalyzer frames` mode from [the perf README](../README.md).

## Setup

| | |
|---|---|
| Lifecycle builds | master `e6c29e434` plus the harness commits of this branch (no `src/` change) |
| #4869 pair | base `233b3930e` (parent of #5011) vs candidate `d8e5e29cd` (#5011 merged), both with the `perf/ServerLoadHarness` of `d8e5e29cd` |
| Host | 24 logical cores, Windows 11, .NET 10 (SDK 11.0.100), Release, net10.0 |
| Bin paths | `C:\ulh\net10.0`, `C:\ulb\...`, `C:\ulc\...`: short paths of the same shape |
| GC | Server GC unless stated; the #4869 command is also run with Workstation GC, as the issue does |
| Rounds | #4869: 5 alternating base/candidate pairs per GC mode. Lifecycle: 3 rounds per line. Traces: 1 run each |
| Run length | `--duration 15 --warmup 8` (#4869: the issue's `--duration 15 --warmup 10`) |
| Discard rule | Foreign testhost/ServerLoadHarness/dotnet/MSBuild/VBCSCompiler CPU over 3 cores |
| Discarded | **None.** Every run recorded `foreign=0` |

Every cell gives the median with the [min..max] of its rounds. "Net" figures subtract the
matching `-ctl` line, which is the same command with `--lc-interval 0` (no churn). Server
allocation per operation is the most reliable signal on this host. CPU per operation is given,
but under 1 core of total server load it is only good to about ±20 %.

## #4869 delivery gate

Command (the issue's exact command):
`--scenario sub --sessions 50 --subs 15 --nodes 600 --node-type all --pub 100 --write-interval 150 --security encrypt --duration 15 --warmup 10`

The gate is: at least 93 % median delivery, at least the matched baseline's delivery, and no
increase in server CPU per notification.

| GC | Side | Valid pairs | Delivery % | CPU µs/notif | Alloc B/notif | write→notify p50 / p99 ms |
|---|---|---|---|---|---|---|
| Workstation | base | 5/5 | 97.7 [97.2..99.7] | 3.29 [3.27..3.48] | 60 [60..63] | 125 / 215 |
| Workstation | cand | 5/5 | 98.1 [97.0..98.3] | 3.32 [3.15..3.46] | 61 [60..64] | 114 / 211 |
| Server | base | 5/5 | 100.6 [98.2..100.7] | 3.19 [3.17..3.31] | 57 [56..67] | 85 / 134 |
| Server | cand | 5/5 | 99.2 [98.6..99.6] | 3.24 [3.20..3.28] | 57 [56..58] | 91 / 148 |

Per-pair candidate minus base:

| GC | Delivery points, pairs 1–5 | CPU µs/notif, pairs 1–5 |
|---|---|---|
| Workstation | +0.9, −1.1, −1.8, +0.6, +0.8 | −0.07, +0.20, +0.03, −0.13, −0.08 |
| Server | +0.5, −1.4, −1.0, −2.1, −1.4 | +0.10, +0.05, −0.08, −0.03, +0.01 |

What this shows:

- All 20 runs were valid (no foreign load).
- **≥ 93 % median: met** in both GC modes (98.1 % and 99.2 %).
- **≥ matched baseline: met with Workstation GC** (+0.4 points median). **With Server GC it is not
  met:** −1.4 points median, and the candidate is lower in 4 of 5 pairs. The baseline's
  100.6–100.7 % rounds are counting-window over-delivery (more received than expected), so part
  of that gap is a ceiling artefact. Even so, the candidate never reached 100 %.
- **CPU per notification: no measurable increase.** +0.9 % (Workstation) and +1.7 % (Server)
  median, both inside the per-side spread, and the per-pair signs are mixed.
- **This workload does not reproduce the gate's regime.** On this host both builds deliver
  97–100 %, far above the 93 % threshold, so the command cannot separate a build that holds the
  gate from one that loses it. The 93 % figure in #4869 was measured with the classic client on a
  slower setup. The harness `native-subs` workload, which the issue's later measurements used, is
  not in this repository and was not run.

## Lifecycle under subscription and read load (`lifecycle-reload`)

Load: 10 sessions × 5 subscriptions × 100 nodes at 100 ms publishing, writes every 100 ms,
4 readers of 16 lifecycle variables, 8 runtime managers × 10 variables, and a monitor
subscription on all lifecycle variables. A connect probe runs every 500 ms. One lifecycle
operation runs every second.

| Line | Churn | Delivery % | Read p50 / p99 ms | Bad read values | Monitor Good / Bad | Apply p50 / p99 ms | Prepare p50 / p99 | Commit p50 / p99 | Commit→Retired p50 / p99 ms |
|---|---|---|---|---|---|---|---|---|---|
| lc-ctl | none | 72 [68.4..100.7] | 0.111 / 0.229 | 0 | 0 / 0 | – | – | – | – |
| lc-alt | migrate/invalidate alternating, remove+add every 5th | 100.7 [100..100.7] | 0.110 / 0.258 | 936 [766..944] | 50 / 20 | 6.07 / 22.6 | 1.93 / 17.5 | 3.61 / 7.22 | 0.26 / 3.19 |
| lc-migrate | Migrate reloads | 81.2 [68.7..100] | 0.113 / 0.242 | 0 | 150 / 0 | 6.81 / 54.5 | 1.93 / 3.40 | 5.04 / 28.9 | 0.23 / 1.29 |
| lc-invalidate | Invalidate reloads | 100 [66.8..100] | 0.113 / 0.213 | 0 | 0 / 10 | 6.32 / 56.1 | 2.11 / 3.56 | 3.81 / 33.0 | 0.48 / 1.35 |

Server per notification (alloc / CPU): lc-ctl 8.6 KB / 112 µs, lc-alt 6.4 KB / 84 µs,
lc-migrate 7.0 KB / 105 µs, lc-invalidate 6.4 KB / 87 µs. These include the 34k reads/s of the
reader sessions, so they are not a notification cost. Probe connect p50 is 7.4–8.0 ms and
re-activate p50 is 1.5 ms on every line.

What this shows:

- No lifecycle failures, issues or stuck retirements: `lcPending` was 0 at every report and
  `lcMaxPending` was 1.
- **Delivery in this load is bimodal with or without churn.** The control alone gives 68.4 %,
  72 % and 100.7 %. Writes every 100 ms against 100 ms sampling alias by phase: low-delivery runs
  have write→notify p50 near 17 ms, full runs near 64 ms. Delivery here therefore cannot carry a
  lifecycle signal. A future revision of the line should write every 150 ms (as #4869 does) or
  sample at 0.
- Migrate keeps the monitored items Good across reloads (150 Good, 0 Bad). Invalidate reports Bad
  once per item, and the items stay Bad until the client recreates them (0 Good afterwards).
  Remove+add makes Reads return Bad values while the manager is absent: about 900 per 15 s at
  34k reads/s.
- Under load, Migrate and Invalidate apply p99 is about 55 ms, against 18 ms at no load
  (f3-w1). The commit phase carries it.
- The reload's own allocation cannot be resolved against 4.5 GB of background allocation in
  15 s. The load-free figures below give it instead.

## F3: concurrent preparation and commit (lifecycle lock hold)

Settings: no client load, 16 managers × 10 variables, an operation every 50 ms per worker, and
every 3rd operation prepare-then-abort.

| Workers | Successful reloads | Aborts | Failures | Server contention / 15 s | Prepare p50 / p99 ms | Commit p50 / p99 | Apply p50 / p99 | Abort p50 | Retired p50 / p99 |
|---|---|---|---|---|---|---|---|---|---|
| 1 | 201 [200..201] | 100 | 0 | 2 [1..6] | 1.54 / 13.2 | 1.88 / 14.6 | 3.56 / 16.1 | 1.75 | 0.15 / 0.61 |
| 4 | 301 [301..302] | 400 | **503 [502..503]** | 305 [290..316] | 1.75 / 13.2 | 2.06 / 15.5 | 3.87 / 17.5 | 1.96 | 0.15 / 0.62 |

Server allocation per successful reload: 4.82 MB (1 worker) and 10.4 MB (4 workers, which also
covers the failed attempts).

What this shows:

- **Concurrent batches on disjoint managers mostly fail.** With 4 workers, 503 of 804 prepares
  (63 %) end in an exception. About 95 % are `NodeManagerRoutingConflictException` ("The address
  space changed while the batch was prepared", `NodeManagerLifecycle.cs:2252`). The rest are a
  plain `InvalidOperationException`, "A reference owner retired during discovery"
  (`MasterNodeManager.Batch.cs:838`). Any commit moves the single host `RoutingRevision`, so every
  batch prepared before it is rejected, even one that touches other managers.
- `NodeManagerRoutingConflictException` is `internal`. To a caller, both failures are a plain
  `InvalidOperationException`, so they cannot tell a retryable conflict from a real error.
- Latency per successful operation is unchanged by concurrency (p50 +0.2–0.3 ms), and lock
  contention stays small (305 events in 15 s).
- Where contention happens, about 66 % is on `NodeManagerGenerationRegistry.CreateReplacement`
  during `PrepareChangesAsync`. Another 7 % is `EventSourceRegistry.Dispose` during cleanup.
- Commit→Retired is fast at 0.15 ms p50 with no sessions. Retirement never backed up
  (`lcMaxPending` was 1).

## F5: many managers and generations in the Read path

Settings: no subscription load, 8 reader sessions, each Read for 64 lifecycle variables, and no
churn unless stated.

| Line | Managers | Managers per Read | Reads/s | Read p50 / p99 ms | Server alloc per Read | Server CPU per Read |
|---|---|---|---|---|---|---|
| f5-m64-r1 | 64 | 1 | 46,456 [46,372..47,257] | 0.161 / 0.573 | 13.2 KB | 156 µs |
| f5-m8-r8 | 8 | 8 | 40,498 [40,391..40,554] | 0.186 / 0.630 | 17.0 KB | 180 µs |
| f5-m64-r64 | 64 | 64 | 19,594 [18,926..19,687] | 0.402 / 0.979 | 160.3 KB | 424 µs |
| f5-m64-r64-churn | 64 (a reload every 250 ms) | 64 | 18,753 [18,553..19,532] | 0.419 / 0.954 | 161.1 KB | 460 µs |

What this shows:

- **A Read that spans many namespaces costs quadratically in namespaces.** Spreading the same 64
  nodes over 64 managers instead of 1 costs 12× the allocation, 2.7× the CPU, and 58 % less
  throughput. That is +2.3 KB for each extra manager touched. 64 managers in total cost nothing
  when a Read touches only one of them (r1 vs m8-r8).
- The allocation trace attributes **84 %** of all server allocation in f5-m64-r64 to
  `NamespaceDefaultPermissions[]`. The array is allocated by `OperationContext.SetDefaultPermissions`
  (`OperationContext.cs:1142`), which `NamespaceDefaultPermissionReader.ReadAsync` calls from
  `ValidateNodePermissionsAsync`. The per-request cache is copy-on-write and grows by one element
  per namespace, so a request over N namespaces allocates arrays of 1..N elements: O(N²) memory.
  `TryGetDefaultPermissions` also scans linearly, which makes lookup O(N²) CPU.
- Churn adds nothing measurable to the Read path (+0.5 % alloc per Read, within spread). Its
  NodeSet parsing, binding and retirement do not slow concurrent Reads.

## F7 / N5: lifecycle cost against idle sessions

Settings: no load, 8 managers (or 64 for `g64`), an operation every 250 ms with remove+add every
5th, plus N idle sessions. A probe opens, re-activates and closes a session every 250 ms.

| Idle sessions | Server alloc per op (net) | Server CPU per op (net) | Commit p50 / p99 ms | Apply p50 / p99 ms | Commit→Retired p50 / p99 ms | Probe connect p50 / p99 ms (churn / ctl) | Re-activate p50 ms | Close p50 ms |
|---|---|---|---|---|---|---|---|---|
| 0 | 3.30 MB | ~44 ms | 2.04 / 28.1 | 3.76 / 30.5 | 0.26 / 9.6 | 7.5 / 20.8 (7.3 / 9.7) | 0.31 | 0.64 |
| 100 | ~3.4 MB | ~54 ms | 2.42 / 25.7 | 4.28 / 26.1 | 0.44 / 1.9 | 7.5 / 19.4 | 0.31 | 0.70 |
| 500 | 3.87 MB | ~58 ms | 2.98 / 21.6 | 4.84 / 61.0 | 0.86 / 8.9 | 7.5 / 18.1 (6.8 / 11.1) | 0.33 | 0.82 |
| 1000 | 4.32 MB | ~67 ms | 3.76 / 25.1 | 5.76 / 26.8 | 1.52 / 5.3 | 7.5 / 16.9 (6.8 / 9.7) | 0.30 | 1.06 |
| 500, 64 managers | 4.07 MB | ~59 ms | 4.12 / 21.0 | 6.02 / 26.1 | 0.89 / 17.5 | 7.8 / 26.8 (7.1 / 12.0) | 0.40 | 0.92 |

The ~100-session line has no matching control, so its net figure uses the s0 control. Net CPU
comes from `cpuCores` given to two decimals at 0.15–0.47 cores, which makes it ±10 ms per
operation.

What this shows:

- **Each operation costs linearly in sessions.** Allocation grows by 1.0 KB per idle session per
  lifecycle operation (+31 % at 1000 sessions). Commit p50 grows by 1.7 µs per session and
  Commit→Retired p50 by 1.3 µs per session (6× at 1000 sessions). This is N5: every bind and
  reconcile enumerates all sessions (`GenerationReconciler.cs:39/:98`, and
  `GenerationReclaimer.cs:663` for continuation points).
- In the 1000-session CPU trace (668 on-CPU samples, so coarse), `GenerationReconciler` holds
  16 % of server CPU, `GenerationReclaimer` 8.5 % and `BindAsync` 7 %. Together that is a
  quarter of the server's lifecycle-time CPU.
- **Clients don't see it yet at 1000 sessions.** Probe connect and re-activate p50 stay flat
  (7.5 ms and 0.3 ms). Churn raises connect p99 from about 10 to about 17–21 ms independent of the
  session count. The cost is linear and sequential (`BindAsync` awaits `ActivateSessionAsync`
  per session), so at ~1.7 µs per session commit gains about 17 ms per 10,000 sessions.
- 64 managers instead of 8 adds 0.2 MB per operation and 1.1 ms of commit p50 at 500 sessions.

## F4 / N6: sampled monitored items, AddNodes/DeleteNodes and permission-cache invalidation

Settings for the f4 lines: no client load, 20 managers × 500 variables, and a monitor
subscription on all 10,000 variables sampled at 100 ms. No values change. The n6 lines use the
subscription load of the lifecycle lines, with one ReferenceServer manager and no readers.
`--addnodes 50` runs one AddNodes and one DeleteNodes of an Object under `Scalar` every 50 ms.
Each such request calls `NodeState.InvalidateRolePermissionsCache()` server-wide.

| Line | Server cores | Server alloc / 15 s | Contention / 15 s | Per notification alloc / CPU | AddNodes+DeleteNodes p50 / p99 ms | Delivery % |
|---|---|---|---|---|---|---|
| f4-sampled | 0.04 | 71.9 MB | 0 | – | – | – |
| f4-sampled-nm | 0.06 | 76.2 MB | 0 | – | 1.21 / 1.96 (301 ok) | – |
| f4-sampled-churn (a 500-variable reload every 1 s) | 0.22 | 942 MB | 12 | – | – | – |
| n6-sub | 0.36 [0.25..0.47] | 111.5 MB | 11 [9..11] | 171 B / 7.8 µs | – | 93.5 [76.7..99.3] |
| n6-sub-nm | 0.53 [0.38..0.71] | 551 MB | **19,341 [19,083..20,221]** | **774 B / 12.3 µs** | 2.27 / 7.63 (301 ok) | 99.3 [86.1..99.3] |
| f4-counters (sampled counters scenario) | 0.41 | 452 MB | 2,146 | 329 B / 4.2 µs | – | – |

What this shows:

- **N6 is not free on the write-driven data-change path.** At 20 AddNodes+DeleteNodes pairs per
  second (40 invalidations per second), per-notification allocation rises from 171 B to 774 B
  (4.5×). Server allocation rises 5×, lock contention goes from 11 to about 19,300 events in 15 s,
  and CPU per notification goes up 58 % (noisy).
- On the sampled path with unchanged values (f4) the same invalidation rate costs +0.02 cores and
  14 KB per AddNodes+DeleteNodes pair. Sampled read verdicts are rebuilt cheaply.
- From code reading, the invalidation runs in `DispatchAdmittedAddNodesAsync` /
  `DeleteNodes` (`MasterNodeManager.NodeManagement.cs:239/:286/:318/:360`) before per-item
  authorization, and again in `finally`. A request whose items are all rejected still retires
  every cached verdict. On the ReferenceServer an anonymous session's AddNodes succeeds, so any
  client can drive this cost.
- Reloading a 500-variable manager with 500 sampled items costs 54 MB of allocation and about
  170 ms of server CPU per operation, net of control. That is ~109 KB per variable, against
  3.3 MB for a 10-variable manager.

## F6: component cost per notification and per Read

| Scenario | Per notification alloc / CPU | Notes |
|---|---|---|
| sub, 10 × 5 × 100, writes every 100 ms | 155 B [153..160] / 6.6 µs | write: 7.7 KB / 326 µs per written value |
| events, 10 × 15, 1000 events/s | 235 B [234..237] / 5.3 µs | 35 KB / 794 µs per reported event |
| counters, 4 × 5 × 500 sampled | 329 B [325..332] / 4.2 µs | |

- **The namespace-default permission read is in every Read request and contends.** Under the
  lifecycle load (34k reads/s), 99 % of all lock contention, about 4,000 events per 15 s, is on
  `BaseVariableState.ReadValueAttribute` called from
  `NamespaceDefaultPermissionReader.ReadRolePermissions`, in the control line as well. The
  defaults are remembered only for one request (see F5), so every Read re-reads the namespace
  metadata variable under its node lock.

## HistoryRead paging (`historyread-paging`)

Settings: 8 sessions, 4 historized ReferenceServer nodes, 10,000 raw values each, and
`ReadRawModifiedDetails` scans that follow continuation points.

| Page size | Pages/s | Values/s | Page p50 / p99 ms | Scan p50 ms | Server cores | Alloc per page | Alloc per value | CPU per page | Contention / 15 s |
|---|---|---|---|---|---|---|---|---|---|
| 100 | 64,743 [63,885..66,515] | 6.47 M | 0.113 / 0.502 | 12.2 | 7.51 | 36.6 KB | 366 B | 116 µs | 66,513 [54,473..68,426] |
| 1000 | 15,578 [15,181..15,769] | 15.6 M | 0.483 / 1.248 | 5.0 | 6.60 | 312 KB | 312 B | 424 µs | 139,375 [137,047..142,569] |

- No short scans and no errors. Continuation points survive the whole scan.
- **Contention is high.** 71 % of it is in `HistorianDispatcher.DispatchRawReadAsync` and 28 % in
  `InMemoryHistorianProvider.ReadRawAsync`, so concurrent sessions reading different nodes
  serialise on the historian. Allocation per value stays around 310–370 B whatever the page size,
  so the cost is per value, not per page.

## Gaps

- The #4869 command does not reproduce the 93 % regime on this host (see above). The
  `native-subs` workload is not in the repository.
- The verdict-cache hit rates of `MonitoredNode` and `SamplingGroup` cannot be observed without
  product counters. N6 is measured by its effect (allocation, contention, CPU) only.
- F3 lock hold time is shown through contention traces and phase latencies. There is no
  hold-time EventCounter.
- Each lifecycle line has 3 rounds and each trace 1 run, which is below the 5 pairs used for
  #4869.
