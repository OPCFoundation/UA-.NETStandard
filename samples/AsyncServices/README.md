# Recoverable Service Execution samples

`RecoverableServiceDemo` is a runnable .NET 10 console application. It starts
an isolated Server and Client, invokes a five-second Method through the normal
generated proxy, writes the typed result to a temporary acceptance file, and
explicitly acknowledges it. It uses UA Binary over UA-TCP with
SignAndEncrypt; no Submit/Get/Control calls appear in the application flow.

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

The Server process and its original Session remain alive during the connection
outage. This is **not restart durability or failover recovery**. If the Server
restarts, closes the Session, or the retention window expires, the caller must
report lost recovery instead of automatically repeating the original Method.
The protocol provides bounded recovery, not exactly-once business effects.

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

The standalone sample's two scenarios also have regression checks:

```powershell
dotnet test tests\Opc.Ua.Server.Tests\Opc.Ua.Server.Tests.csproj `
    -f net10.0 -p:CustomTestTarget=net10.0 `
    --filter FullyQualifiedName~RecoverableServiceDemoTests
```

See the [combined implementation and protocol guide](../../docs/AsyncServices.md)
for eligibility, security, lifetime, acknowledgement and evidence details.
