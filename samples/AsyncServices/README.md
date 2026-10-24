# Recoverable Service Execution samples

`RecoverableServiceDemo` is a runnable .NET 10 console application. It starts
an isolated Server and Client, invokes a five-second Method through the normal
generated proxy, writes the typed result to a temporary acceptance file, and
explicitly acknowledges it. It uses UA Binary over UA-TCP with
SignAndEncrypt; no manager-Method calls appear in the application flow.

From the repository root:

```powershell
# Long-running operation without a network fault.
dotnet run --project samples\AsyncServices\RecoverableServiceDemo

# The same ordinary call, with a real connection outage during execution.
dotnet run --project samples\AsyncServices\RecoverableServiceDemo -- --network-outage
```

Both runs finish on their own and return a nonzero exit code if the observed
result violates the scenario. The process has a 60-second safety timeout.
No external Server, device, credentials, firewall change or manual timing is
required.

## What the outage demonstrates

The sample waits until the actual Server Method has started, then closes
established TCP connections in a loopback relay and rejects reconnects. The
Server performs the increment while disconnected and retains the result.
The relay restores connectivity only after the mutation has completed.
The original typed call then completes using the original Session; the
application does not repeat the Method.

The output includes:

```text
Method started once; five seconds of Server work remain.
Network interrupted: existing TCP connections closed; reconnects are rejected.
Server finished while disconnected; the result is retained.
Network restored; awaiting the original typed call, not repeating it.
Typed result written to the sample acceptance file; acknowledging now.
PASS: value=7, invocations=1, sameSession=True, acknowledged=True.
```

The decisive checks are one actual Server invocation, one increment, an
unchanged SessionId and an explicit acknowledged receipt. The relay forwards
opaque bytes: it neither simulates OPC UA replies nor supplies a fake result.
It is sample fault-injection infrastructure, not a deployment gateway.

`RecoveryDemo.cs` shows the application-facing flow:

```csharp
await using ManagedSession session = await new ManagedSessionBuilder(configuration, telemetry)
    .UseEndpoint(endpoint)
    .PreferRecoverableExecution()
    .ConnectAsync(ct);

await using var acknowledgement = session.BeginAcknowledgementScope(
    requireRecoverableReceipt: true);
int value = await device.SlowIncrementAsync(7, ct);
await File.WriteAllTextAsync(resultPath, value.ToString(CultureInfo.InvariantCulture), ct);
await acknowledgement.AcknowledgeAsync(ct);
```

Without the acknowledgement scope, the SDK acknowledges after typed validation,
local acceptance and caller completion. The sample selects the Server's advertised
execution/retention defaults, not a budget derived from the caller's timeout.
The acceptance file demonstrates ordering only: it is not a transactional
outbox, an MQTT implementation or a guarantee of filesystem crash durability.
The sample deletes the file and its private temporary PKI stores on exit.

## Security and recovery limits

Both listeners bind to loopback IP addresses. Certificates are created in
isolated temporary stores at run time; the sample explicitly enables auto-trust
for these local peers. Do not carry that setting into a production host.

In the two original modes, the Server process and its original Session remain
alive during the connection outage. This is **not restart durability or failover recovery**. If the Server
restarts, closes the Session, or the retention window expires, the caller must
report lost recovery instead of automatically repeating the original Method.
The protocol provides bounded recovery, not exactly-once business effects.

## Owner replacement-Session scenario

The executable Server fixture
[`OwnerInvocationSurvivesSessionDisposalAndReplacementCollectsOriginalOutcome`](../../tests/Opc.Ua.Server.Tests/AsyncServices/RecoverableOwnerExecutionTests.cs)
demonstrates independent invocation ownership and collection through a replacement
Session with the same validated stable owner:

```powershell
dotnet test tests\Opc.Ua.Server.Tests\Opc.Ua.Server.Tests.csproj `
    -f net10.0 -p:CustomTestTarget=net10.0 `
    --filter FullyQualifiedName~OwnerInvocationSurvivesSessionDisposalAndReplacementCollectsOriginalOutcome
```

It uses real in-process handlers and generated manager Calls. It is not a
native-wire or Server-process-restart demonstration. Owner selection requires
the configured owner and held eligibility providers; changing a Client option
alone cannot transfer a Session-bound resource.

Durable recovery additionally requires protected storage, a custom qualified
clock and recoverable work fencing. The local backend does not supply elapsed
time across a host restart. Do not adapt this sample by using an unchecked UTC
clock or resetting E/R/H when reopening a store. See the
[Durable hosting contract](../../docs/rse.md#durable-storage-and-restart).

## Supervised real child-process restart TEST

The additional `--process-restart-test` mode is Windows-only integration
infrastructure in the same executable, not a production Server/Client change.
**All three sample regression tests pass on Windows/net10, with no skips.**
This is bounded child-process restart evidence with explicitly simulated
supervisor time—not host-reboot, power-loss, native AOT Owner/Durable proof,
or production-complete Durable qualification. The two original modes and
`RecoveryDemo.RunAsync(bool, TextWriter, CancellationToken)` are preserved.

Run the process-restart script with:

```powershell
dotnet run --project samples\AsyncServices\RecoverableServiceDemo -- --process-restart-test
```

The script has the same 60-second safety bound, fails nonzero on an invariant
failure, and uses actual native loopback UA-TCP connections with
SignAndEncrypt. No external endpoint or supplied production credentials are
used. Its five Server incarnations are actual child OS processes:

The successful run ended with:

```text
PASS: Owner=3, retained original=10, invocations=3, business value=21, actual child kills=4, exact TEST H boundary=True, clock failure checked=True.
```

| Self-checking scenario | Required observation |
| --- | --- |
| `OwnerReplacementSessionAsync` | One generated `SlowIncrement(3)` starts; its issuing Session is closed; a new authenticated Session reattaches its protected checkpoint, collects value 3 and explicitly acknowledges. |
| `RetainedUnacknowledgedOutcomeAsync` / `RecoverRetainedAndReleaseAsync` | One `SlowIncrement(7)` commits value 10. The Client collects the complete original/header and saves protected acceptance **without ACK**. The supervisor kills and awaits that Server child, restarts the same store/PKI, and recovers an exactly matching fresh wire result before explicit ACK. |
| `ReleasedHistorySurvivesRestartAsync` | Another kill/restart after release retains `Acknowledged` / `AlreadyApplied`, no payload and the original H deadline. |
| `InterruptAfterBusinessEffectAsync` / `InterruptedOriginalBecomesIndeterminateAsync` | RSE3-064: one `SlowIncrement(11)` commits the actual business value from 10 to 21, then blocks **before handler return and RSE outcome commit**. The supervisor rereads `(invocations=3, value=21)`, verifies native RSE `Pending`/no payload, and kills it **without releasing the gate**. Recovery yields `ExecutionIndeterminate` / `BadUnexpectedError`, no original payload and a new R once, while the business value remains 21. |
| `IndeterminateRetentionIsNotRenewedAsync` | Another restart and repeated collections preserve that outcome/token and decrease rather than renew R. Explicit failure acceptance/ACK is followed by H checks immediately before and exactly at expiry in TEST time. |
| `ClockAuthorityChangeFailsClosedAsync` | After the business/history checks, an injected TEST clock-domain change makes the live child's time request fail closed. Restoring the former coordinate does not clear the failure latch. |

The retained-outcome scenario deliberately collects before the kill to establish
the outcome commit. It is **retained-unacknowledged-outcome recovery**, not a
claim of a lost original reply. Reattachment can restore locally accepted
checkpoint data, so the script also makes independent native `GetResult` Calls
and compares the complete original response, including its header and item
results. Each new Session discovers the five actual manager Method instances;
no declaration IDs are used as instance IDs and the probe has no Submit helper.
The probe uses a captured physical `SessionRequestContext` for discovery and
send-once Calls, not managed recoverable interception. It checks generated RSE
registrations in the decode context and reports status/count/rank/type metadata
without logging tokens or payloads. Client-ID GetResult explicitly sets
`ServerToken = default` (null ByteString); the generated empty ByteString
initializer is not equivalent. A direct pre-dispatch assertion requires exactly
one selector and requires token-form requests to have an empty ClientOperationId
and a token of 1–64 bytes. Recovery-only handles are never submitted.

The final protected counter must be **three original invocations and value 21**:
one Owner original, one retained Durable original, and one interrupted Durable
original whose business effect committed but whose handler never returned.
The same deterministic return gate is used after the business commit in all
three originals; no separate pre-effect interruption scenario is retained.
`EffectCommitted` is sent only after the real `FileSharedKeyValueStore.SetAsync`
business-value write returns, not after entry logging alone. Before the kill,
the supervisor independently rereads that protected record and observes native
RSE `Pending` with no outcome payload. After each recovery, it requires the
same `(3,21)` state and no new handler dispatch. This is the business-effect/RSE
outcome gap, not evidence that an indeterminate operation had no effects.

No sleep is used to infer an admission, outcome, effect or termination boundary.
The business counter and the RSE journal do **not** form an atomic business
transaction or an exactly-once-effects guarantee. A separate per-process dispatch
count also catches erroneous redispatch attempts that the handler's own fence
rejects before changing the business counter.

### Time, termination and authentication limits

- The CLI explicitly selects `SupervisorTestClock`. A single surviving
  supervisor supplies its domain and **simulated elapsed-time steps** through
  current-user-only named pipes. Both peers verify the OS-reported pipe peer PID.
  No child uses UTC or resets a Stopwatch to invent elapsed downtime. A lost
  authority, changed domain or rollback fails closed; no cached/default time is
  returned.
- This proves only the scripted **child-process restart** behavior. It does not
  physically reboot a host, power-fail storage, qualify host suspension, or
  establish a production-complete Durable facet. The time authority and its
  secret store must remain alive.
- The example eligibility provider qualifies only the held, exact generated
  Method/object/argument combination, not arbitrary services. Qualification
  accepts only the bound Method instance or its own non-null declaration for
  that same Object; deterministic binding checks reject another Object, an
  unrelated declaration and a null declaration alias. It does not rewrite the
  original MethodId, enable retry/fallback or change RSE-manager discovery.
  The callback rejects ordinary and Session-bound calls. It creates no external worker or
  surviving continuation. The supervisor associates each supplied durable
  execution ID with the exact launched `Process`; only an awaited actual exit
  supplies termination proof. Missing proof retains a fence, including against
  ordinary calls, and local reservation disposal does not clear that fence.
  Before killing the interrupted original, the script explicitly verifies that
  its still-running child does **not** qualify as terminated.
- The new mode uses explicit trust lists, not auto-trust. Runtime-generated
  Server, Client-application and X.509-user certificates are stable across child
  restarts. The built-in validator-backed `X509Authenticator` supplies validated
  issuer/subject claims. `ConfiguredRecoverableExecutionOwnerProvider` receives
  explicit application-certificate SHA256 and issuer/subject mappings. Client
  continuity also pins the actual signed channel/application and user
  credentials. Auto-trust remains confined to the two original isolated-loopback
  demonstrations described above.
- Record-protection keys and private-key passwords are generated into the
  existing secret-store infrastructure. They are neither printed nor passed in
  process arguments. Child arguments contain only a non-secret pipe name and
  supervisor PID; bootstrap secrets travel after pipe peer verification.
- Real `FileSharedKeyValueStore` instances use `IRecordProtector`, with a
  **4 MiB Server backend and 2 MiB Client backend**, under a uniquely owned
  temporary parent. With the 131072-byte accepted message limit, a journal
  admission reserves `4 * 131072 + 12296 = 536584` bytes before dispatch.
  No native directory synchronization or ACL check is bypassed. Only this run's
  children and directories are cleaned; unproven exit prevents directory removal.

The thin regression binding
[`RealServerProcessRestartPreservesOwnerOutcomesEffectsAndBoundaries`](../../tests/Opc.Ua.Server.Tests/AsyncServices/RecoverableServiceDemoTests.cs)
calls:

```csharp
string childHostPath = Path.Combine(
    TestContext.CurrentContext.TestDirectory,
    "rse-process-host",
    "RecoverableServiceDemo.exe");
ProcessRestartDemoResult result =
    await ProcessRestartDemo.RunTestAsync(childHostPath, output, cancellationToken);
```

The helper asserts before returning; the binding also checks all eight returned
result fields with their exact expected values. It retains the 60-second
cancellation budget and the existing nonparallel fixture. Only this new test
has NUnit's Windows platform constraint; the original two tests remain unchanged.
The existing project metadata includes this fixture and its sample reference
only for net10.0. Its net10-only `StageRecoverableServiceDemoChildHost` build
target queries the sample's `GetTargetPath` metadata without building references,
then stages the complete existing sample runtime output under
`rse-process-host`, preserving relative assets. This adds no package or framework
dependency. Required built host files must already exist; the target fails
clearly otherwise. A no-build test run uses the existing stage and does not
silently build or repair it.

The binding explicitly selects the staged apphost and its own adjacent
runtimeconfig/deps. That explicit path never falls back to NUnit's executable
or to the test-output root. The standalone
`RunTestAsync(TextWriter, CancellationToken)` entry remains available; it selects
the named sample apphost or DLL from `AppContext.BaseDirectory`.

The complete staged file inventory and SHA256 hashes match the current sample
output, with no key/PKI files present. The three-case fixture also passes within
the 1,044-case net10 Server RSE selection. Matching only the apphost, assembly,
deps and runtimeconfig is insufficient: the NUnit host's additional ASP.NET
shared framework does not supply dependencies to the separate sample child.
Linux/macOS are not qualified by the Windows peer-PID IPC implementation.

For an application-qualified time source, the separate injectable entry is
`ProcessRestartDemo.RunWithQualifiedClockAsync(IRecoverableExecutionClock,
TextWriter, CancellationToken)`. A clock is required, with no permissive
default. That path does not manipulate caller time and reports
`HistoryBoundaryVerified == false`; it does not pretend to perform the
deterministic H-boundary test or qualify the caller's clock.
It also reports `ClockFailureVerified == false`: only the named TEST-clock path
injects a clock-domain fault into the surviving authority.

## Certificate-management demonstration

The two-hop certificate example is currently an executable integration fixture,
not a separate interactive certificate-management application:

- [`CertificateCycleAcrossRecoverableGateway` in AsyncServiceTransportTests.cs](../../tests/Opc.Ua.Server.Tests/AsyncServices/AsyncServiceTransportTests.cs)
  hosts the GDS-role Client, gateway and device and runs the full CSR, test-CA
  issuance, UpdateCertificate, ApplyChanges and verification cycle.
- [`AsyncCertificateForwarder.cs`](../../tests/Opc.Ua.Server.Tests/AsyncServices/AsyncCertificateForwarder.cs)
  contains the typed downstream forwarding code.

Run all four UA-TCP/HTTPS mapping combinations with:

```powershell
dotnet test tests\Opc.Ua.Server.Tests\Opc.Ua.Server.Tests.csproj `
    -f net10.0 -p:CustomTestTarget=net10.0 `
    --filter FullyQualifiedName~CertificateCycleAcrossRecoverableGateway
```

The two original scenarios and the Windows-only restart binding share a
regression fixture (see the bounded validation status above):

```powershell
dotnet test tests\Opc.Ua.Server.Tests\Opc.Ua.Server.Tests.csproj `
    -f net10.0 -p:CustomTestTarget=net10.0 `
    --filter FullyQualifiedName~RecoverableServiceDemoTests
```

See the [combined implementation and protocol guide](../../docs/rse.md)
for eligibility, security, lifetime, acknowledgement and evidence details.
