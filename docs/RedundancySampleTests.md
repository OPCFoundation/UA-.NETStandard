# Redundant Sample Integration Tests

The `Opc.Ua.Redundancy.Samples.Tests` project contains process-level integration tests that launch the redundant sample applications
&mdash; [`RedundantServer`](../samples/RedundantServer), [`RedundantClient`](../samples/RedundantClient) and
[`RedundantPubSub`](../samples/RedundantPubSub) &mdash; as real external processes and assert on the high-availability behavior they log
(`FAILOVER:`, `DATA LOSS:`, `HA OK:`, and reconnect events). They exercise the samples in their supported setups so that regressions in the
end-to-end high-availability experience are caught automatically, and they demonstrate both successful failover *and* the visibility of data loss.

The tests come in a **short-haul** form that runs as part of normal pull-request validation, and a **long-haul** soak form that runs on a
dedicated, manually triggered CI job (and on a weekly schedule) to validate high-availability operation over an extended period.

## Short-haul tests (pull-request validation)

Short-haul tests are deterministic and complete in seconds. They are discovered and run automatically by the normal
[Build and Test](../.github/workflows/buildandtest.yml) workflow (and the Azure DevOps test stages), because the project follows the
`tests/Opc.Ua.*.Tests` naming convention. They are tagged with the NUnit category `SampleHaShortHaul`:

* **PubSub demo, hot mode** &mdash; runs `RedundantPubSub --role demo --ha-mode hot` and asserts the promoted publisher continues the SequenceNumber
  across failover with no reset (`SIMULATED: HA OK: sequence continued ...`).
* **PubSub demo, cold mode** &mdash; runs the same demo in cold mode and asserts the SequenceNumber reset (data loss) is made visible
  (`SIMULATED: DATA LOSS: sequence reset ...`).
* **Server + client connectivity** &mdash; launches a `RedundantServer` and a `RedundantClient` together and asserts the client connects and begins
  high-availability monitoring.

Run them locally from the repository root:

```powershell
dotnet test tests/Opc.Ua.Redundancy.Samples.Tests/Opc.Ua.Redundancy.Samples.Tests.csproj --filter "Category=SampleHaShortHaul"
```

## Long-haul tests (manual / scheduled soak)

Long-haul tests are marked `[Explicit]` (NUnit category `SampleHaLongHaul`) so they never run on pull requests. They loop for a configurable
duration and assert the high-availability guarantees hold across many failovers:

* **PubSub failover soak** &mdash; repeatedly runs the PubSub demo in hot and cold modes for the whole duration, asserting the correct continuity
  (hot) and data-loss (cold) narrative every iteration.
* **Client reconnect soak** &mdash; runs a `RedundantClient` against a `RedundantServer` for the whole duration and repeatedly kills and restarts the
  server, asserting the client transparently detects the loss, reconnects when the server returns, and never crashes.

The soak duration is controlled by the `SAMPLE_HA_DURATION_MINUTES` environment variable (default `60`). Run a short local soak with:

```powershell
$env:SAMPLE_HA_DURATION_MINUTES = "5"
dotnet test tests/Opc.Ua.Redundancy.Samples.Tests/Opc.Ua.Redundancy.Samples.Tests.csproj --filter "Category=SampleHaLongHaul"
```

The long-haul tests run in CI through dedicated, manually triggerable jobs on both platforms:

* **GitHub Actions** &mdash; the [Sample HA Long-Haul Test](../.github/workflows/sample-ha-longhaul.yml) workflow (`workflow_dispatch` with a
  `duration` input, plus a weekly schedule).
* **Azure DevOps** &mdash; the [sample-ha-longhaul](../.azurepipelines/sample-ha-longhaul.yml) pipeline (manual run with a `durationMinutes`
  parameter, plus a weekly schedule).

## Multi-replica leader-election failover

The full multi-replica leader-election topologies &mdash; strong (Raft) active/passive and eventual (CRDT gossip) active/active &mdash; rely on a
stable virtual endpoint with DNS-based re-resolution to a surviving replica. These are demonstrated end-to-end by the docker-compose setups under
[`samples/RedundantServer`](../samples/RedundantServer) and [`samples/RedundantClient`](../samples/RedundantClient), which run
several replicas on a container network and let you kill the active replica to observe cross-replica failover. The process-level long-haul soak
above focuses on the failover-detection, reconnect, and data-loss-visibility behavior that runs deterministically inside a CI runner without
container networking.
