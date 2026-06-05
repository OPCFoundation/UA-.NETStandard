# Channel-manager stress tests

`Tests/Opc.Ua.Channels.Stress.Tests` contains the layered stress and chaos coverage for
`IClientChannelManager` and managed-channel reconnect behavior.

## Categories

| Category | Layer | Where | Runs | Purpose |
|---|---|---|---|---|
| `Contract` | L1 | `Contract/`, `Fakes/`, `Helpers/` | Every PR | Deterministic fake-based coverage for sharing, reconnect coalescing, participant results, retry budgets, gates, leases, certificates, and leak accounting. |
| `Integration` | L2 | `Integration/` and live-server parts of `Chaos/` | Every PR | In-process server coverage for outages, live certificate rotation, and failover lease changes. |
| `ChaosTCP` | L3 | `Chaos/` | Nightly | TCP proxy chaos for transparent reconnect, subscription survival, accept-but-stall, and drop / block-accept schedules. |
| `Soak` | L4 | `Soak/` | Manual or nightly | Long randomized and combinatorial runs, including memory-stability checks. |
| `[Explicit]` | L5 | `Gaps/` | Never automatic | Known production carry-forward gaps such as faulted-entry reset, `RequiresSessionRecreate`, and bounded participant timeout. |

## Running

```bash
# Contract + Integration (default PR CI):
dotnet test Tests/Opc.Ua.Channels.Stress.Tests --filter "Category=Contract|Category=Integration"

# ChaosTCP (nightly):
dotnet test Tests/Opc.Ua.Channels.Stress.Tests --filter "Category=ChaosTCP" --TestRunParameters.Parameter(Seed=<n>)

# Soak (manual):
dotnet test Tests/Opc.Ua.Channels.Stress.Tests --filter "Category=Soak"
```

Every chaos test prints its seed at start. Re-run a failed chaos case with the printed seed by passing
`--TestRunParameters.Parameter(Seed=<n>)`.

## Adding a test

- Add fast fake-based behavior to `Contract/` and derive from `ContractTestBase`.
- Add live in-process server coverage to `Integration/` and derive from `IntegrationTestBase`.
- Add TCP proxy chaos scenarios to `Chaos/`, derive from `IntegrationTestBase`, and use `TcpChaosProxy`,
  `ChaosSchedule`, `StressRunner`, and `MetricsCollector` as needed.
- Add long randomized or matrix runs to `Soak/`. Use `IntegrationTestBase` when the test needs the live server or
  TCP proxy, and use `ContractTestBase` for fake-only soak coverage.
- Add known production gaps to `Gaps/`, derive from `GapTestBase`, and mark each test `[Explicit]` with a message
  that names the carry-forward gap.

## Inspecting failures

Chaos failures should include the printed seed in the NUnit output. Use the same seed to reproduce the schedule.
When a test uses `MetricsCollector`, inspect the dumped channel-manager metrics and EventSource records in the
failure output before changing production code or widening timing windows.
