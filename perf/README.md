# Performance tooling

Tools for measuring server and client latency, throughput and GC cost under load. They are not
part of the product build (not in `UA.slnx`) and target `net10.0` only.

| Path | Purpose |
|---|---|
| `ServerLoadHarness/` | Starts a reference server in a child process and drives load against it from the parent process, which is also the measured client. |
| `TraceAnalyzer/` | Aggregates the `dotnet-trace` files the harness records (CPU, allocations, lock contention, garbage collections). |
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
| `--scenario` | `read` | `read`, `write`, `browse` (Server object), `sub` (data-change subscriptions plus writers), `events` (event subscriptions on the Server object, events reported by the server), `counters`, or `native-read`/`native-counters` (see below). |
| `--sessions` | 20 | Concurrent sessions. |
| `--nodes` | 100 | Values per Read/Write request, or monitored items per subscription. |
| `--inflight` | 1 | Outstanding requests per session (closed loop). |
| `--security` | `none` | `none`, `sign` or `encrypt` (Basic256Sha256). |
| `--client` | `classic` | `classic`: `Session` with the classic subscription engine. `managed`: `ManagedSession` with the V2 subscription engine (`DefaultSubscriptionEngineFactory`). `shared`: classic sessions on secure channels shared through one `ClientChannelManager`, created one at a time (`--connect-parallel`). |
| `--callback` | `fast` | Classic data changes through `Subscription.FastDataChangeCallback` (`fast`) or the per-item `MonitoredItem.Notification` event (`item`). |
| `--subs`, `--pub`, `--write-interval` | 5, 100, 100 | Subscriptions per session, publishing interval and writer interval in ms (`sub`). `events` defaults to 15 subscriptions; `counters` to a 1000 ms publishing interval. |
| `--write-nodes` | all | Number of the monitored nodes the writer changes (`sub`), for large subscriptions in which few items change. |
| `--writers` | 0 | Dedicated writer sessions in addition to `--sessions` (`sub`). 0: the first subscriber session writes. |
| `--write-interval 0` | | Writers write in a closed loop instead of on a timer (`sub`). |
| `--write-batch` | all | Values per Write request; writers rotate through the written nodes in slices (`sub`). |
| `--node-type` | `int` | `int`: Int32 mass nodes. `all`: the Int16…UInt64 mass node set of the Sessions `LoadTest` (up to 600 nodes) (`sub`). |
| `--queue-size` | 1 (`sub`), 10000 (`events`) | Monitored item queue size. |
| `--publish-requests` | engine default | Minimum outstanding Publish requests per classic session. |
| `--event-rate` | 0 | Events per second the server reports (`events`); 0 reports as fast as one thread can. |
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
from run to run, so compare builds with `ab.sh` rather than with two separate runs; see
[Measurement method](#measurement-method).

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
| `native-counters` | `subscription/native/client.c`: one subscription, `--nodes` monitored UInt64 counters, `--sampling` 10 ms, `--pub` 1000 ms, queue size 1. The counters advance in their read callback. | Every item reaches 99 % of the samples in the measured window (10 s at 10/1000 ms); a `FAIL` exits with 1. `samples/s` counts the samples the server took (counter advancement); `notif/s` is shown when the worker reports delivered notifications. |

| Option | Default | Meaning |
|---|---|---|
| `--native-client` | `client.exe` (`native-read`), `sub_client.exe` (`native-counters`) | Path of the worker executable. |
| `--clients` | 10 | Read worker processes (`native-read`). |
| `--inflight` | 100 | Outstanding Reads per worker (`native-read`). |
| `--iterations`, `--warmup-calls` | 100000, 10000 | Measured and warm-up Reads per worker (`native-read`). The defaults are the benchmark's own and are a smoke test for a .NET server; see [Steady-state parameters](#steady-state-parameters). |
| `--nodes`, `--sampling`, `--pub` | 1, 10, 1000 | Monitored counters, sampling and publishing interval in ms (`native-counters`). |
| `--server-exe`, `--server-args` | harness child, `--port {port}` | An external server executable and its arguments; `{port}`, `{items}`, `{sampling}` and `{pki}` are replaced. `--server-args` takes the next argument verbatim, even if it starts with `--`. |
| `--server-affinity` | all processors | Hexadecimal processor mask of the server process, for example `0x1` to compare the throughput per core with a single-threaded server. |
| `--client-affinity` | all processors | Hexadecimal processor mask of the worker processes, for example the mask of `--server-affinity` to co-locate clients and server. |
| `--server-priority` | `Normal` | `ProcessPriorityClass` of the server process, for example `BelowNormal` so that co-located workers preempt server threads. |
| `--thread-states` | 0 | 1: sample the scheduling state of the server threads (see below). |
| `--security` | `none` | `encrypt`: Basic256Sha256 SignAndEncrypt with DER identities written to `{pki}` (`native-read` only; the subscription worker has no identity, so `native-counters` rejects it). |
| `--async-node-manager`, `--counter-lock`, `--queue` | see below | Configuration of the harness child. |
| `--threads` | 200 | `MaxRequestThreadCount` of the harness child. |
| `--trace <file>`, `--trace-profile`, `--duration` | | Record the server for `--duration` seconds: `native-read` from the start of the measurement (default 20 s); `native-counters` from before the window is armed, by default long enough for the settle time, the warm-up and the window. |
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
| `MaxQueuedRequestCount` | clients × in flight + 3 (minimum 200): the whole worker pipeline plus the three reserved bootstrap, reconnect and control slots, as the benchmark runner sizes it for 2.0.1 (current builds add the reserved slots on top of the configured value, so the + 3 is headroom) | `--queue` |

The `counters` scenario keeps `CustomNodeManager2` with interlocked increments.

`--server-affinity` (Windows and Linux) is applied after the server started, when the .NET
runtime has already sized the thread pool and the GC heaps for all processors. Set
`SERVER_DOTNET_PROCESSOR_COUNT` to the number of processors in the mask so that the runtime sizes
them for the restricted machine:

```bash
SERVER_DOTNET_PROCESSOR_COUNT=4 ServerLoadHarness.exe run --scenario native-read --server-affinity 0xF --native-client <workers>/build/client.exe
```

With `--thread-states 1` a thread in the harness samples the threads of the server process
every ~3 ms during the measurement and appends
`sched: samples=<n> running=<avg> ready=<avg> readyMax=<max>` to the `server:` line. `ready`
counts threads in the Ready or Standby state, which wait for a processor: on pinned or shared
processors that is oversubscription, which delays a GC suspension until the preempted thread
runs again. The sampler enumerates the server threads every time, so leave it off for
throughput comparisons.

```bash
ServerLoadHarness.exe run --scenario native-read --native-client <workers>/build/client.exe --clients 10 --inflight 100 --iterations 100000 --warmup-calls 10000
ServerLoadHarness.exe run --scenario native-read --native-client <workers>/build/client.exe --server-exe <workers>/build/server.exe --server-args "--port {port}"
ServerLoadHarness.exe run --scenario native-counters --native-client <workers>/build/sub_client.exe --nodes 4096
```

### Steady-state parameters

The default `native-read` run (10,000 warm-up and 100,000 measured Reads per worker) takes a few
seconds. The .NET server then still runs much of its request path as tier-0 JIT code, and the
CPU per request measures the JIT warm-up rather than the server: on 4 pinned logical processors
about 28–31 µs per Read in 3-second runs against 8.8 µs at steady state, on 24 logical
processors about 60 µs against 28 µs. A native server such as open62541 has no warm-up, so short
runs also skew a comparison between stacks. Use the defaults as a smoke test, and for CPU per
request and comparisons with other stacks measure at steady state:

```bash
SERVER_DOTNET_PROCESSOR_COUNT=4 ServerLoadHarness.exe run --scenario native-read --native-client <workers>/build/client.exe --server-affinity 0xF --iterations 300000 --warmup-calls 100000
```

The per-request cost should not change when the warm-up is made longer; if it still falls,
extend the warm-up. `SERVER_DOTNET_TieredCompilation=0` (every method fully optimized at its
first call) is a quick check of how much of a short run is JIT warm-up, but it also removes
dynamic PGO, so do not use it for the reported numbers.

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
dotnet perf/TraceAnalyzer/bin/Release/net10.0/TraceAnalyzer.dll gc trace.nettrace
dotnet perf/TraceAnalyzer/bin/Release/net10.0/TraceAnalyzer.dll samples trace.nettrace 30
dotnet perf/TraceAnalyzer/bin/Release/net10.0/TraceAnalyzer.dll suspend trace.nettrace 5
dotnet perf/TraceAnalyzer/bin/Release/net10.0/TraceAnalyzer.dll threads trace.nettrace 10
```

`cpu` with a focus frame also lists the callers of that frame and the first `Opc.Ua` frame
above it, past framework wrappers such as `Monitor.Enter` or `SemaphoreSlim`, and the frame's
direct callees. `gc` reads a
trace recorded with `--trace-profile gc`, lists the collections by generation and trigger
reason and splits each pause into the time needed to suspend the threads and the whole pause;
a long suspension means a thread ran for a while without reaching a safe point. `samples`
lists all thread-time samples by type and leaf frame (and by the first `Opc.Ua` frame),
including the ones `cpu` leaves out.

`suspend <trace> [thresholdMs]` (default 40) reads a `--trace-profile gc` trace and prints every
GC suspension that took at least the threshold, with the events recorded between SuspendEE
start and stop by thread: an `AllocationTick` inside that window comes from a thread that was
still running managed code, the one that had not reached a safe point. It also counts all
events, including `HEAPS <n> gen0budget~<bytes>` (the heap count DATAS chose, from
`GlobalHeapHistory`) and `HANDLEKIND <kind>` (GC handle churn by kind), and ends with
`SetGCHandle=<count> trace ms=<duration>`.

`threads` lists the busy share of each thread (its on-CPU samples over all of its samples) with
its main roles; one thread near 100 % while the process has idle cores is a serialized stage.

The `sub` and `events` scenarios add a `per-op2` line with the server cost per written value or
per generated event. Async methods with loops run on-stack-replaced code that the trace rundown
does not name (samples show as `!?`); set `SERVER_DOTNET_TC_QuickJitForLoops=0` and
`SERVER_DOTNET_TC_OnStackReplacement=0` when recording to attribute them.

The sampler suspends running threads at their next GC poll, so many on-CPU samples have
`Thread.PollGCWorker` as their leaf; `cpu` attributes those to the calling frame instead of
dropping them as a wait (threads that wait for a real collection land there too, which the
`gc` mode shows). The EventPipe sampler attributes the on-CPU time since the previous sample to
the poll site it stops at, and the poll is often the return from a native call
(`DateTime.UtcNow`, `TimeProvider.GetTimestamp`), so such frames are heavily over-represented:
in one pinned `native-read` trace the clock reads held about 70 % of the samples, yet removing
them did not change the CPU per request. Read the shares as where the threads run, not as
costs, and check a suspected hot spot with an A/B run of the change. For reliable CPU
attribution use ETW CPU sampling, which samples at the instruction the processor executes:
PerfView (`PerfView collect`) or WPR/WPA (`wpr -start CPU`), both from an elevated prompt on
Windows.

The sampled thread-time profile also samples blocked threads. The `cpu` mode drops samples
whose leaf frame is a known wait (thread-pool semaphore, I/O completion poll, sleep),
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
cd perf && BASELINE=C:/uabase/perf/ServerLoadHarness/bin/Release/net10.0/ServerLoadHarness.exe SCENARIOS=client-scenarios.txt ROUNDS=5 ./ab.sh
pwsh -File ab-summary.ps1
```

The summary prints medians as
`baseline -> candidate (change)`: throughput, latency percentiles, client allocated bytes and CPU
per operation (`calloc`, `ccpu`), GC pause and lock contention in the window (`cpause`,
`ccont`), and the same for the server (`alloc`, `cpu`, `pause`, `cont`). It prints medians
only; take the ranges from the result blocks in `ab-results.txt`.

## Measurement method

These rules proved reliable on a developer machine shared with other work:

- **Allocated bytes per operation** (`per-op: allocB`) are deterministic and the primary metric:
  a change of a few percent is real, also on a loaded machine.
- **CPU per operation** (`per-op: cpuUs`) is comparable only with the server pinned. In the
  `native-read` and `native-counters` scenarios use `--server-affinity 0xF` and
  `SERVER_DOTNET_PROCESSOR_COUNT=4` for the child, so that the runtime sizes the thread pool
  and the GC heaps for the mask. The other scenarios ignore `--server-affinity`: start the
  harness itself with the affinity (`start /affinity F` on Windows, `taskset -c 0-3` on
  Linux), which the server child inherits at its start, keeping in mind that the in-process
  client then shares those processors. On a hybrid CPU, choose a mask
  of one core type only (all performance or all efficiency cores): the same request costs
  measurably more CPU time on an efficiency core, so a mask that mixes them measures the
  scheduler. Measure the native scenarios at [steady state](#steady-state-parameters).
- **Throughput and latency** only from paired, alternating A/B rounds (`ab.sh`), never from two
  separate runs. Other test hosts, load harnesses or benchmarks running on the machine at the
  same time make a build look slower (fewer requests per second while cores sit idle); discard
  every round during which one ran, and repeat it.
- Run **5 rounds** per scenario and report the **median with the range** (min–max) of each side.
  A difference inside the ranges of both sides is not a result.
- Name the machine, the operating system, the .NET version, the commit and the date with every
  number you publish.
