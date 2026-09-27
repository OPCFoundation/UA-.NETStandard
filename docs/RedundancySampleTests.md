# Redundant Sample Integration Tests

The `Opc.Ua.Redundancy.Samples.Tests` project runs process-level integration
tests against the `RedundantServer`, `RedundantClient`, and `RedundantPubSub`
sample applications. The tests launch the applications as external processes
and assert on logged high-availability events such as `FAILOVER:`, `DATA LOSS:`,
`HA OK:`, and reconnect messages. They cover supported sample configurations,
catch regressions in end-to-end failover, and demonstrate when failover loses
data.

The test project includes **short-haul** tests for normal pull-request
validation and **long-haul** soak tests for extended runs on dedicated CI jobs.

## Contents

- [Short-haul tests](#short-haul-tests-pull-request-validation)
- [Long-haul tests](#long-haul-tests-manual--scheduled-soak)
- [Multi-replica leader-election failover](#multi-replica-leader-election-failover)

## Short-haul tests (pull-request validation)

Short-haul tests are deterministic and complete in seconds. The standard
[CI](../.github/workflows/buildandtest.yml) workflow and Azure DevOps test
stages discover and run them because the project follows the
`tests/Opc.Ua.*.Tests` naming convention. They use the NUnit category
`SampleHaShortHaul`:

* **PubSub demo, hot mode** — runs `RedundantPubSub --role demo --ha-mode hot` and asserts the promoted publisher continues the SequenceNumber
  across failover with no reset (`SIMULATED: HA OK: sequence continued ...`).
* **PubSub demo, cold mode** — runs the same demo in cold mode and asserts the SequenceNumber reset (data loss) is made visible
  (`SIMULATED: DATA LOSS: sequence reset ...`).
* **Server + client connectivity** — launches a `RedundantServer` and a `RedundantClient` together and asserts the client connects and begins
  high-availability monitoring.

Run them locally from the repository root:

```powershell
dotnet test tests/Opc.Ua.Redundancy.Samples.Tests/Opc.Ua.Redundancy.Samples.Tests.csproj --filter "Category=SampleHaShortHaul"
```

## Long-haul tests (manual / scheduled soak)

Long-haul tests use `[Explicit]` and the NUnit category
`SampleHaLongHaul`, so they do not run on pull requests. They loop for a
configurable duration and check high-availability behavior across many
failovers:

* **PubSub failover soak** — repeatedly runs the PubSub demo in hot and cold modes for the whole duration, asserting the correct continuity
  (hot) and data-loss (cold) narrative every iteration.
* **Client reconnect soak** — runs a `RedundantClient` against a
  `RedundantServer` for the entire duration. The test repeatedly stops and
  restarts the server, then checks that the client detects the outage,
  reconnects when the server returns, and does not crash.

The `SAMPLE_HA_DURATION_MINUTES` environment variable controls the soak
duration (default: `60` minutes). Run a short local soak with:

```powershell
$env:SAMPLE_HA_DURATION_MINUTES = "5"
dotnet test tests/Opc.Ua.Redundancy.Samples.Tests/Opc.Ua.Redundancy.Samples.Tests.csproj --filter "Category=SampleHaLongHaul"
```

The long-haul tests run in CI through dedicated, manually triggerable jobs on both platforms:

* **GitHub Actions** — the [Sample HA Long-Haul Test](../.github/workflows/sample-ha-longhaul.yml) workflow (`workflow_dispatch` with a
  `duration` input, plus a weekly schedule).
* **Azure DevOps** — the [sample-ha-longhaul](../.azurepipelines/sample-ha-longhaul.yml) pipeline (manual run with a `durationMinutes`
  parameter, plus a weekly schedule).

## Multi-replica leader-election failover

Full multi-replica leader-election topologies include strong (Raft)
active/passive and eventual (CRDT gossip) active/active configurations. They
use a stable virtual endpoint with DNS-based re-resolution to a surviving
replica.

The Docker Compose setups under
[`samples/Redundancy/RedundantServer`](../samples/Redundancy/RedundantServer)
and [`samples/Redundancy/RedundantClient`](../samples/Redundancy/RedundantClient)
run several replicas on a container network. Stop the active replica to
observe cross-replica failover. The process-level long-haul soak above focuses
on deterministic failover detection, reconnect, and data-loss visibility
without container networking.
