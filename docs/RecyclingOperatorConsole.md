# Recycling operator console

The operator console is a **local-workstation sample for a synthetic recycling line**.
An ASP.NET Core 10 host serves local HTML, SVG, CSS and JavaScript and connects directly
to the coordinator over OPC UA. The browser does not connect to OPC UA, MCP or MQTT.
This is not an industrial safety system, a physical emergency-stop station or a historian.

The [recycling sample](../samples/Recycling/README.md) remains eleven independent server
processes. Starting or closing a browser or console does not start/reset production, renew
process membership or stop equipment. A new run starts at the existing ten-participant
initialization barrier, not at an invented Start button.

## Build and configure

Use the .NET 10 SDK from the repository root:

```powershell
dotnet build samples\Recycling\RecyclingOperatorConsole\RecyclingOperatorConsole.csproj `
    -f net10.0 -p:CustomTestTarget=net10.0
```

The sample-local `Recycling.Client` library shares the typed model and client with the
servers; neither it nor the web executable is a new NuGet package. Server and shared-client
managed targets remain .NET 8/9/10. The web host starts at .NET 10.

Provision application trust, a dedicated UA user identity and an unpredictable bootstrap
key **outside the repository and Docker context** before launching. Use the stack's
[certificate manager](CertificateManager.md) and [certificate-store guidance](Certificates.md).
Do not copy a process-view private key into the workstation, disable hostname validation or
enable unknown-certificate auto-acceptance.

The host reads standard .NET configuration environment variables:

| Key prefix `RecyclingConsole__` | Meaning |
| --- | --- |
| `Endpoint` | Explicit coordinator `opc.tcp` endpoint. |
| `LineId` | Expected line identity; must match the selected coordinator. |
| `Role` | Conservative permission ceiling: `Observer` (default), `Operator`, or `SimulatorAdministrator`. |
| `BootstrapKeyFile` | Required external text file containing a random 32-byte key encoded as hexadecimal. |
| `UserCertificatePath` | External directory certificate store for the single configured UA user identity. |
| `UserCertificateSubject` | Subject selector within that user store, for example `CN=Recycling-line-1-observer`. |
| `UserCertificatePasswordFile` | External text file holding the user store's private-key password; not the password itself. |
| `ApplicationCertificateStore` | Directory store for this host's separate application identity. |
| `TrustedPeerStore`, `TrustedIssuerStore`, `RejectedCertificateStore` | Application trust, issuer and rejected-certificate directory stores. |
| `AllowedHost` | Exact browser host/port, normally `localhost:8080`. |
| `ContainerBind` | `false` by default (loopback); explicit `true` listens on container interfaces on port 8080. |
| `ProcessEndpoints__<process>` | Explicit mappings using names such as `Collection`, `DensitySeparation`, `MeltFiltration` and `Bagging`. |

For example, after provisioning the files and peer/user trust:

```powershell
$env:RecyclingConsole__Endpoint = 'opc.tcp://localhost:62570/Recycling'
$env:RecyclingConsole__LineId = 'line-1'
$env:RecyclingConsole__Role = 'Observer'
$env:RecyclingConsole__BootstrapKeyFile = 'D:\recycling-client\secrets\bootstrap-key'
$env:RecyclingConsole__UserCertificatePath = 'D:\recycling-client\secrets\users'
$env:RecyclingConsole__UserCertificateSubject = 'CN=Recycling-line-1-observer'
$env:RecyclingConsole__UserCertificatePasswordFile = 'D:\recycling-client\secrets\user-password'
$env:RecyclingConsole__ApplicationCertificateStore = 'D:\recycling-client\pki\own'
$env:RecyclingConsole__TrustedPeerStore = 'D:\recycling-client\pki\trusted'
$env:RecyclingConsole__TrustedIssuerStore = 'D:\recycling-client\pki\issuer'
$env:RecyclingConsole__RejectedCertificateStore = 'D:\recycling-client\pki\rejected'
dotnet run --project samples\Recycling\RecyclingOperatorConsole\RecyclingOperatorConsole.csproj `
    -f net10.0 -p:CustomTestTarget=net10.0 --no-launch-profile
```

Use the actual advertised endpoint and matching certificate DNS names. `localhost` in the
example is not an endpoint-rewriting mechanism. Missing native process mappings must be
shown as unavailable, not replaced by coordinator values pretending to be native readings.

Open `http://localhost:8080/bootstrap?token=<contents-of-bootstrap-key-file>` locally.
This exchanges the launch credential for a short-lived HttpOnly, SameSite=Strict browser
session and redirects to `/`. Treat the bootstrap URL as a credential: do not paste it into
shared logs, screenshots or committed scripts; protect browser history and the key file.
The browser uses its session and a CSRF token for same-origin command POSTs. Keep the
allowed host explicit; do not add broad CORS or expose this sample as a remote multiuser service.
Rotate the bootstrap key and restart to invalidate launch access when appropriate.

The UA user certificate and application certificate have different purposes. Trust the
application with the servers, trust the user certificate in user trust, and bind its public
fingerprint to the intended role in the coordinator's external identity mapping. A local
`Role` setting cannot grant permissions the server denies. One host uses one UA identity,
not one elevated identity per browser tab.

## Readings, commands and limits

- The coherent plant overview comes from the coordinator's committed typed snapshot.
  Supplementary native process readings have their own quality, source timestamps and
  projection timing; they must not silently be merged across runs/configurations.
- Distinguish browser transport health, coordinator session health, and process membership.
  A paused tick or unchanged source timestamp alone is not disconnection. On a connection
  gap, retained observations are stale/unavailable, not new zero measurements.
- Display mass/energy in labeled units. Exact integer mass arithmetic stays in C#;
  64-bit revisions and identifiers cross JSON as strings. Run-to-date averages are not
  instantaneous rates. No capacity, OEE, ETA or supply percentage is inferred.
- Trends and recent command outcomes are bounded workstation-session observations, not a
  lossless event feed, complete audit log or persisted historian. Logical ticks differ from
  wall-clock connection time; gaps and run resets must remain visible.
- Material/genealogy reads are bounded and run-scoped. Equipment recovery does not release
  persistent quality holds, and produced packages do not necessarily equal released product.

The exception dock is a compact preview of recent outstanding holds. The **Holds** section
pages the complete retained history, with Outstanding/All/Released/Rejected filters and
Previous/Next navigation. Pages carry their run and revision, so holds beyond the preview
remain inspectable and eligible for authorized disposition. The read-only `/api/holds`
endpoint accepts `runId`, an `after` hold ID, `limit` (1..100), and an optional `disposition`.

| Host mode | Intended actions (server authorization still applies) |
| --- | --- |
| Observer / unknown | Read-only inspection. |
| Operator | Enable/disable a process; explicitly release/reject a quality hold. |
| Simulator administrator | Operator actions plus pause, resume, one-tick step, stop, reset and checkpoint. |

Commands use the coordinator's typed `ExecuteAsync`, not arbitrary UA writes/calls or
process membership methods. Destructive actions require explicit confirmation. Reset requires
a stopped run. Stop is simulator administration, **not a physical emergency stop**.
Submission revalidates the run/lifecycle/revision. Stale input is a visible conflict; the host
does not silently rewrite and resubmit a mutation.

A request keeps its original request ID and envelope. A timeout or generic server/response
failure is an unknown outcome, not success or a proven rejection: a response failure can occur
after a durable journal commit. Only an explicit retry reuses that exact envelope. Browser HTTP success does not
optimistically change authoritative plant state. In-memory pending actions cannot be promised
recoverable after host restart; the coordinator's durable receipts remain authoritative.

Transport recovery marks retained observations unavailable even if a managed session retains
its session ID. A channel returning to Ready does not by itself make old data fresh; the console
requires a new usable coordinator observation before enabling controls again.

Version one does not edit recipes, inject faults, switch scenarios, launch/stop server processes,
or add acknowledgement/shelving semantics to boolean fault flags. The schematic is an
information layout, not a surveyed plant. Companion inspection and browser controls must be
qualified against the actual configured endpoints and identities before use.

## Container images

The existing **Images CI** workflow registers `recyclingserver` and `recyclingclient`.
They inherit its existing GHCR repository naming, branch/version tags, PR no-publish policy,
platforms and `images summary` gate. No new scheduled workflow is introduced. Registry
availability depends on a successful publishing run; the following commands build locally.

```powershell
docker build -f samples\Recycling\RecyclingServer\Dockerfile -t recycling-server:local .
docker build -f samples\Recycling\RecyclingOperatorConsole\Dockerfile -t recycling-client:local .
```

Both Dockerfiles compile portable framework-dependent .NET 10 IL on `$BUILDPLATFORM`, without
a runtime identifier, app host or NativeAOT. The server uses the .NET runtime; the client uses
the ASP.NET runtime and copies the entire publish output, including `wwwroot`.
The final images run as the non-root .NET `app` user.

CI publishes once natively and selects the prebuilt stage, reusing that output for
`linux/amd64` and `linux/arm64/v8` without compiling an SDK under QEMU. Reproduce the client path:

```powershell
dotnet publish samples\Recycling\RecyclingOperatorConsole\RecyclingOperatorConsole.csproj `
    -c Release -f net10.0 -p:CustomTestTarget=net10.0 `
    -p:PublishAot=false -p:UseAppHost=false -p:SelfContained=false `
    -o artifacts\docker\publish\recyclingclient
docker build -f samples\Recycling\RecyclingOperatorConsole\Dockerfile `
    --build-arg PUBLISH_SOURCE=prebuilt `
    --build-arg PUBLISH_DIR=artifacts/docker/publish/recyclingclient `
    -t recycling-client:prebuilt .
```

For the server, substitute its project, Dockerfile and `recyclingserver` publish directory.
Do not put secrets, private scenarios or runtime journals under either publish directory.
NativeAOT publication/smoke testing is a separate validation target, not the payload of these
portable multiarchitecture images.

The [deployment guide](../samples/Recycling/RecyclingServer/deploy/README.md#optional-operator-console)
describes the optional Compose `operator-console` profile, explicit network mappings and
isolated client mounts. Container binding is explicitly enabled there, but the published
HTTP port is still **host loopback only**. This does not turn the Kubernetes server sample
into a remote web deployment.

## Validation

Image contract tests are in `RecyclingImageContractTests` in `Opc.Ua.Recycling.Operator.Tests`:

```powershell
dotnet test tests\Opc.Ua.Recycling.Operator.Tests\Opc.Ua.Recycling.Operator.Tests.csproj -f net10.0 `
    -p:CustomTestTarget=net10.0 --filter FullyQualifiedName~RecyclingImageContractTests
```

These static contract checks do not prove a working image, live encrypted UA connection,
browser interaction or NativeAOT compatibility. Separately build both Dockerfiles from
source and prepublished output, inspect both target architectures and static assets, and
smoke-test a provisioned isolated Compose line with strict trust and an authorized identity.
Check unavailable endpoints, reconnect, pause, stale/unauthorized commands and exact retry
outcomes. Run image-build and Compose qualification on a host with a working Docker engine;
static checks alone cannot establish source/prebuilt or multiarchitecture runtime success.

### Browser interaction profile

The .NET 10 `Opc.Ua.Recycling.Operator.Tests` project contains the explicit
`OperatorBrowserTests` profile. Build first so Playwright generates its installation script,
install Chromium once for the current user, then select the browser class explicitly:

```powershell
dotnet build tests\Opc.Ua.Recycling.Operator.Tests\Opc.Ua.Recycling.Operator.Tests.csproj `
    -c Release -f net10.0 -p:CustomTestTarget=net10.0
pwsh tests\Opc.Ua.Recycling.Operator.Tests\bin\Release\net10.0\playwright.ps1 install chromium
dotnet test tests\Opc.Ua.Recycling.Operator.Tests\Opc.Ua.Recycling.Operator.Tests.csproj `
    -c Release -f net10.0 -p:CustomTestTarget=net10.0 --no-build `
    --filter FullyQualifiedName~OperatorBrowserTests
```

Browser installation downloads the Playwright-pinned Chromium runtime; it is a test prerequisite,
not an application dependency or an npm build. The class is marked NUnit `Explicit`, so a
normal unfiltered test run does not substitute for this selected browser profile.

These tests serve the actual local static UI to headless Chromium with deterministic,
test-only HTTP responses. They exercise rendering and interaction, including viewport,
keyboard, reduced-motion and command-confirmation behavior. They do **not** prove live UA
connectivity, application/user certificate trust, production HTTP authentication or successful
container startup; qualify those separately against the real host and provisioned servers.
Commands here are instructions, not recorded passing browser results.

### NativeAOT publication

Use the repository-pinned .NET SDK (`global.json`: 10.0.401) and a matching platform's native
toolchain. On Windows, install Visual Studio C++ Build Tools with the Desktop development with
C++ workload and a supported Windows SDK. Linux publication needs the platform's NativeAOT
prerequisites, including Clang and development libraries. A .NET SDK alone is not sufficient;
do not treat cross-architecture portable IL packaging as native compilation.

From the repository root in a Visual Studio Developer PowerShell with its linker and Windows SDK
environment initialized, for Windows x64:

```powershell
dotnet publish samples\Recycling\RecyclingOperatorConsole\RecyclingOperatorConsole.csproj `
    -c Release -f net10.0 -r win-x64 -p:CustomTestTarget=net10.0 `
    -o artifacts\recycling-console-native\published
.\artifacts\recycling-console-native\published\RecyclingOperatorConsole.exe
```

Supplying a runtime identifier enables AOT in this executable's project. Avoid setting
`PublishAot=true` globally: it would also apply to the .NET Standard source-generator projects.

The explicit executable profile provisions isolated test certificates and a real coordinator:

```powershell
$env:OPCUA_RECYCLING_OPERATOR_NATIVE_EXE = (
    Resolve-Path artifacts\recycling-console-native\published\RecyclingOperatorConsole.exe).Path
dotnet test tests\Opc.Ua.Recycling.Operator.Tests\Opc.Ua.Recycling.Operator.Tests.csproj `
    -c Release -f net10.0 -p:CustomTestTarget=net10.0 `
    --filter FullyQualifiedName~NativeConsoleAuthenticatesStreamsAndControlsTheRealCoordinatorAsync
```

Also select the explicit graceful-shutdown profile against the same native executable:

```powershell
dotnet test tests\Opc.Ua.Recycling.Operator.Tests\Opc.Ua.Recycling.Operator.Tests.csproj `
    -c Release -f net10.0 -p:CustomTestTarget=net10.0 `
    --filter FullyQualifiedName~NativeConsoleGracefullyStopsWithoutStoppingTheCoordinatorAsync
```

This profile keeps a live SSE request open while delivering an operating-system shutdown
signal. It requires a clean native exit, then reads and resumes the independent coordinator
through encrypted OPC UA and verifies continued logical ticks in the same run. Windows
uses an isolated console and the built-in Windows PowerShell signal helper; the helper refuses
to signal a console containing an unrelated process. Other platforms use a PID-targeted
`SIGTERM`. Forced process termination is only failure cleanup, never evidence of graceful shutdown.

SSE requests observe application shutdown as well as browser cancellation and session expiry.
An open browser must not consume the host's shutdown budget before UA services can stop.

Provision and export the same external certificate, bootstrap and endpoint settings described
above before launching. Select a runtime identifier compatible with the native build host;
the example command is not a claim that a native build or startup was validated.
Native qualification must also exercise browser authentication, SSE, typed reads, an authorized
command, static assets and graceful shutdown against a provisioned line. A successful publish
alone does not prove those behaviors.

Follow the [developer guide](DeveloperGuide.md#running-tests) for solution-level PR gates;
do not report unavailable or unexecuted checks as passed.
