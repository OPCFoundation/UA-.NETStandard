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
| `--scenario` | `read` | `read`, `write`, `browse` (Server object), `sub` (data-change subscriptions plus a writer), `counters`, or `native-read`/`native-counters` (see below). |
| `--sessions` | 20 | Concurrent sessions. |
| `--nodes` | 100 | Values per Read/Write request, or monitored items per subscription. |
| `--inflight` | 1 | Outstanding requests per session (closed loop). |
| `--security` | `none` | `none`, `sign` or `encrypt` (Basic256Sha256). |
| `--client` | `classic` | `classic`: `Session` with the classic subscription engine. `managed`: `ManagedSession` with the V2 subscription engine (`DefaultSubscriptionEngineFactory`). `shared`: classic sessions on secure channels shared through one `ClientChannelManager`, created one at a time (`--connect-parallel`). |
| `--callback` | `fast` | Classic data changes through `Subscription.FastDataChangeCallback` (`fast`) or the per-item `MonitoredItem.Notification` event (`item`). |
| `--subs`, `--pub`, `--write-interval` | 5, 100, 100 | Subscriptions per session, publishing interval and writer interval in ms (`sub`). |
| `--write-nodes` | all | Number of the monitored nodes the writer changes (`sub`), for large subscriptions in which few items change. |
| `--duration`, `--warmup` | 20, 10 | Measurement window and warm-up in seconds. |
| `--diag`, `--audit` | 0 | Server diagnostics and auditing. |
| `--threads`, `--queue` | 200, 2000 | `MaxRequestThreadCount` and `MaxQueuedRequestCount` of the server; `MinRequestThreadCount` is the smaller of 50 and `--threads`. |
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

## External benchmark workers

`--scenario native-read` and `--scenario native-counters` drive a server with the native
open62541 workers of [o6-automation/opcua-benchmarks](https://github.com/o6-automation/opcua-benchmarks),
the client used for that project's published comparison of OPC UA stacks, so the .NET server and
other stacks are measured with the same client. The harness starts the server, the worker
processes and the measurement window; the result block has the same lines as the other
scenarios, with the CPU time of the native workers in `client-per-op:`.

| Scenario | Workload | Pass / metric |
|---|---|---|
| `native-read` | `throughput/open62541/client.c --worker async`: `--clients` processes, each with `--inflight` single-value Reads of ns=1;i=1001..1100 in flight, `--iterations` measured calls after `--warmup-calls`, common start. | Aggregate req/s, server CPU per request. |
| `native-counters` | `subscription/native/client.c`: one subscription, `--nodes` monitored UInt64 counters, `--sampling` 10 ms, `--pub` 1000 ms, queue size 1. The counters advance in their read callback. | Every item reaches 99 % of the samples in the measured window (10 s at 10/1000 ms). |

| Option | Default | Meaning |
|---|---|---|
| `--native-client` | `client.exe` (`native-read`), `sub_client.exe` (`native-counters`) | Path of the worker executable. |
| `--clients` | 10 | Read worker processes (`native-read`). |
| `--inflight` | 100 | Outstanding Reads per worker (`native-read`). |
| `--iterations`, `--warmup-calls` | 100000, 10000 | Measured and warm-up Reads per worker (`native-read`). |
| `--nodes`, `--sampling`, `--pub` | 1, 10, 1000 | Monitored counters, sampling and publishing interval in ms (`native-counters`). |
| `--server-exe`, `--server-args` | harness child, `--port {port}` | An external server executable and its arguments; `{port}`, `{items}`, `{sampling}` and `{pki}` are replaced. `--server-args` takes the next argument verbatim, even if it starts with `--`. |
| `--server-affinity` | all processors | Hexadecimal processor mask of the server process, for example `0x1` to compare the throughput per core with a single-threaded server. |
| `--security` | `none` | `encrypt`: Basic256Sha256 SignAndEncrypt with DER identities written to `{pki}`. |
| `--async-node-manager`, `--counter-lock`, `--queue` | see below | Configuration of the harness child. |
| `--threads` | 200 | `MaxRequestThreadCount` of the harness child. |
| `--trace <file>`, `--trace-profile`, `--duration` | | Record the server for `--duration` seconds from the start of the measurement. |
| `--verbose` | 0 | 1: echo the worker output. |

Without `--server-exe` the server is the harness child in contract mode (`server --contract 1`):
endpoint `opc.tcp://127.0.0.1:<port>`, server URI `urn:o6:benchmark:server`, which the workers
require because the contract puts its nodes in the namespace of the server URI (ns=1). The
external server can be, for example, the open62541 server of the benchmark or one of its .NET
workers.

The harness child is configured like the benchmark's .NET worker for UA-.NETStandard 2.0.1:

| Setting | Default | Override |
|---|---|---|
| Node managers (contract and counters) | `AsyncCustomNodeManager` | `--async-node-manager 0`: `CustomNodeManager2` |
| Counter increment | under a lock owned by the counters | `--counter-lock 0`: interlocked |
| Garbage collector | Server GC | `SERVER_DOTNET_gcServer=0` |
| `MaxQueuedRequestCount` | clients × in flight + 3 (minimum 200): the whole worker pipeline plus the three reserved bootstrap, reconnect and control slots | `--queue` |

The `counters` scenario keeps `CustomNodeManager2` with interlocked increments.

`--server-affinity` (Windows and Linux) is applied after the server started, when the .NET
runtime has already sized the thread pool and the GC heaps for all processors. Set
`SERVER_DOTNET_PROCESSOR_COUNT` to the number of processors in the mask so that the runtime sizes
them for the restricted machine:

```bash
SERVER_DOTNET_PROCESSOR_COUNT=4 ServerLoadHarness.exe run --scenario native-read --server-affinity 0xF --native-client <workers>/build/client.exe
```

```bash
ServerLoadHarness.exe run --scenario native-read --native-client <workers>/build/client.exe --clients 10 --inflight 100 --iterations 100000 --warmup-calls 10000
ServerLoadHarness.exe run --scenario native-read --native-client <workers>/build/client.exe --server-exe <workers>/build/server.exe --server-args "--port {port}"
ServerLoadHarness.exe run --scenario native-counters --native-client <workers>/build/sub_client.exe --nodes 4096
```

### Building the workers

The benchmark repository is licensed under the AGPL-3.0, so nothing of it is part of this
repository: clone it and build its workers separately. On Windows they build with `zig cc` and
CMake against the open62541 commit the benchmark pins (`deps/open62541`, OpenSSL backend,
`UA_ENABLE_DEBUG_SANITIZER=OFF`, Release). The subscription workers additionally need `poll()` on
stdin replaced by `PeekNamedPipe` and unbuffered control output (the C runtime treats `_IOLBF` as
full buffering). The open62541 win32 event loop is `select()` based, so open62541 numbers on
Windows are lower than on Linux.

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
