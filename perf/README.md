# Server performance tooling

Tools for measuring server latency, throughput and GC cost under load. They are not part of
the product build (not in `UA.slnx`) and target `net10.0` only.

| Path | Purpose |
|---|---|
| `ServerLoadHarness/` | Starts a reference server in a child process and drives load against it from the parent process. |
| `TraceAnalyzer/` | Aggregates the `dotnet-trace` files the harness records (CPU, allocations, lock contention). |
| `ab.sh`, `ab-summary.ps1` | Paired A/B runs of two harness builds, and the median summary. |

The server runs in its own process, so the server-side numbers (CPU, allocated bytes, GC count
and pause time, `Monitor.LockContentionCount`) exclude the load generator. The NUnit
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
| `--subs`, `--pub`, `--write-interval` | 5, 100, 100 | Subscriptions per session, publishing interval and writer interval in ms (`sub`). |
| `--duration`, `--warmup` | 20, 10 | Measurement window and warm-up in seconds. |
| `--diag`, `--audit` | 0 | Server diagnostics and auditing. |
| `--trace <file>` | | Record the measurement window with `dotnet-trace`. |
| `--trace-profile` | `cpu` | `cpu` (sampled thread time), `alloc` (allocation ticks), `contention` or `gc`. |
| `--out <file>` | | Append the result block to a file. |

Environment variables prefixed `SERVER_` are passed to the server process without the prefix,
for example `SERVER_DOTNET_gcServer=1` to compare garbage collector modes.

The result block reports client throughput and latency percentiles, the server deltas for the
window, and the server's allocated bytes and CPU time per request (or per notification).
Allocated bytes per operation are deterministic and the most reliable signal. On a shared
machine, throughput and latency vary from run to run, so compare builds with `ab.sh` rather
than with two separate runs.

## Analyse a trace

```bash
dotnet perf/TraceAnalyzer/bin/Release/net10.0/TraceAnalyzer.dll alloc trace.nettrace 40
dotnet perf/TraceAnalyzer/bin/Release/net10.0/TraceAnalyzer.dll cpu trace.nettrace 40 Monitor.Enter_Slowpath
dotnet perf/TraceAnalyzer/bin/Release/net10.0/TraceAnalyzer.dll contention trace.nettrace 15
```

The sampled thread-time profile also samples blocked threads. The `cpu` mode drops samples
whose leaf frame is a known wait (thread-pool semaphore, I/O completion poll, GC suspension),
so the remaining samples approximate on-CPU time. Spin-waiting in `Monitor.Enter_Slowpath` or
`Lock.TryEnterSlow` is real CPU and shows up there.

## A/B comparison

Build the baseline harness in a second worktree, then alternate the two builds:

```bash
git worktree add --detach C:/uabase origin/master
cp -r perf C:/uabase/ && (cd C:/uabase && CustomTestTarget=net10.0 dotnet build perf/ServerLoadHarness -c Release)
cd perf && BASELINE=C:/uabase/perf/ServerLoadHarness/bin/Release/net10.0/ServerLoadHarness.exe ./ab.sh
pwsh -File ab-summary.ps1
```
