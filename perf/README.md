# Performance tooling

Tools for measuring server and client latency, throughput and GC cost under load. They are not
part of the product build (not in `UA.slnx`) and target `net10.0` only.

| Path | Purpose |
|---|---|
| `ServerLoadHarness/` | Starts a reference server in a child process and drives load against it from the parent process, which is also the measured client. |
| `TraceAnalyzer/` | Aggregates the `dotnet-trace` files the harness records (CPU, allocations, lock contention). |
| `ab.sh`, `ab-summary.ps1` | Paired A/B runs of two harness builds, and the median summary. |
| `server-scenarios.txt`, `client-scenarios.txt` | The scenario lists used for the server and client comparisons. |

The server runs in its own process, so the server-side numbers (CPU, allocated bytes, GC count
and pause time, `Monitor.LockContentionCount`) exclude the load generator, and the same
counters taken in the parent process measure the client stack without the server. The NUnit
`LoadTest` fixture in `tests/Opc.Ua.Sessions.Tests` checks correctness under load; this harness
measures it.

## Build

```bash
CustomTestTarget=net10.0 dotnet build perf/ServerLoadHarness -c Release
dotnet build perf/TraceAnalyzer -c Release
dotnet tool install -g dotnet-trace
```

## Run

```bash
perf/ServerLoadHarness/bin/Release/net10.0/ServerLoadHarness.exe run --scenario read --nodes 100 --sessions 20 --duration 15 --warmup 8
```

| Option | Default | Meaning |
|---|---|---|
| `--scenario` | `read` | `read`, `write`, `browse` (Server object) or `sub` (data-change subscriptions plus a writer). |
| `--sessions` | 20 | Concurrent sessions. |
| `--nodes` | 100 | Values per Read/Write request, or monitored items per subscription. |
| `--inflight` | 1 | Outstanding requests per session (closed loop). |
| `--security` | `none` | `none`, `sign` or `encrypt` (Basic256Sha256). |
| `--client` | `classic` | `classic`: `Session` with the classic subscription engine. `managed`: `ManagedSession` with the V2 subscription engine (`DefaultSubscriptionEngineFactory`). |
| `--callback` | `fast` | Classic data changes through `Subscription.FastDataChangeCallback` (`fast`) or the per-item `MonitoredItem.Notification` event (`item`). |
| `--subs`, `--pub`, `--write-interval` | 5, 100, 100 | Subscriptions per session, publishing interval and writer interval in ms (`sub`). |
| `--duration`, `--warmup` | 20, 10 | Measurement window and warm-up in seconds. |
| `--diag`, `--audit` | 0 | Server diagnostics and auditing. |
| `--trace <file>` | | Record the measurement window with `dotnet-trace`. |
| `--trace-profile` | `cpu` | `cpu` (sampled thread time), `alloc` (allocation ticks), `contention` or `gc`. |
| `--trace-side` | `server` | The process to record: `server` (the child) or `client` (the harness itself). |
| `--out <file>` | | Append the result block to a file. |

Environment variables prefixed `SERVER_` are passed to the server process without the prefix,
for example `SERVER_DOTNET_gcServer=1` to compare garbage collector modes. `DOTNET_gcServer=1`
set for the harness itself switches the client.

Managed subscription setup has a one-minute deadline per subscription. Rejected monitored
items and subscription error or deletion states fail the scenario instead of waiting
indefinitely or starting a measurement with incomplete subscriptions.

The result block reports client throughput and latency percentiles, the deltas of both
processes for the window (`server:`, `client-process:`), and the allocated bytes and CPU time
per request (or per notification) of the server (`per-op:`) and of the client
(`client-per-op:`). Classic sessions are created without diagnostics in responses, as an
application would; the test fixture otherwise asks for them. Allocated bytes per operation are
deterministic and the most reliable signal. On a shared machine, throughput and latency vary
from run to run, so compare builds with `ab.sh` rather than with two separate runs.

## Analyse a trace

```bash
dotnet perf/TraceAnalyzer/bin/Release/net10.0/TraceAnalyzer.dll alloc trace.nettrace 40
dotnet perf/TraceAnalyzer/bin/Release/net10.0/TraceAnalyzer.dll cpu trace.nettrace 40 Monitor.Enter_Slowpath
dotnet perf/TraceAnalyzer/bin/Release/net10.0/TraceAnalyzer.dll contention trace.nettrace 15
```

The sampled thread-time profile also samples blocked threads. The `cpu` mode drops samples
whose leaf frame is a known wait (thread-pool semaphore, I/O completion poll, GC suspension),
so the remaining samples approximate on-CPU time. Spin-waiting in `Monitor.Enter_Slowpath` or
`Lock.TryEnterSlow` is real CPU and shows up there; check it against a `contention` trace,
though, because a thread blocked in the native part of `Monitor.Enter` has the same leaf frame.
Socket sends and receives on loopback show up as on-CPU kernel time.

The `alloc` profile samples one allocation per ~100 KB and attributes the whole sample to the
object that crossed the threshold. That over-weights small objects allocated right after large
arrays, so read the type list as a pointer and the per-operation bytes in the result block as
the measurement.

## A/B comparison

Build the baseline harness in a second worktree, then alternate the two builds. `SCENARIOS`
selects the scenario list (`server-scenarios.txt` by default, `client-scenarios.txt` for the
client), `ROUNDS` the rounds per scenario and `OUT` the result file:

```bash
git worktree add --detach C:/uabase origin/master
cp -r perf C:/uabase/ && (cd C:/uabase && CustomTestTarget=net10.0 dotnet build perf/ServerLoadHarness -c Release)
cd perf && BASELINE=C:/uabase/perf/ServerLoadHarness/bin/Release/net10.0/ServerLoadHarness.exe SCENARIOS=client-scenarios.txt ./ab.sh
pwsh -File ab-summary.ps1
```

The summary prints medians as
`baseline -> candidate (change)`: throughput, latency percentiles, client allocated bytes and CPU
per operation (`calloc`, `ccpu`), GC pause and lock contention in the window (`cpause`,
`ccont`), and the same for the server (`alloc`, `cpu`, `pause`, `cont`).
