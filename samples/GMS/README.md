# GMS sample pair

`GmsServer` and `GmsClient` use the public fluent/DI hosting, generated proxies
and bounded GMS clients. These are **simulations, not physical measuring
equipment**. There are no measurement-start, job-execution, calibration or
machine-correction commands.

The released model is 1.0.0; its reference document is 1.0.1. The upstream
undefined `GMS_ProductionStatisticsType` remains an explicit conformance
exception: Annex A.1 makes the released NodeSet normative. Neither sample
claims the complete Production facet.

## Run

From the repository root, use separate terminals:

```powershell
dotnet run --project samples\GMS\GmsServer\GmsServer.csproj -f net10.0 -- `
    --advanced --instances 2 --seconds 60 --demo-trust
dotnet run --project samples\GMS\GmsClient\GmsClient.csproj -f net10.0 -- `
    --events --characteristic Bore --download --demo-trust
```

Omit `--advanced` for mandatory structure and inline results. Optional
equipment, corrections and file-transfer absence are reported explicitly.
The server prints `READY` only after OPC UA initialization; `--port 0` selects
a free port and prints its actual URL. Pass that URL with the client's
`--endpoint` option.

Certificate trust is required by default. The separate server/client stores
under `artifacts\gms-pki` are retained for administration; trust each peer's
application certificate in the other application's trusted peer store.
`--demo-trust` is an explicit acceptance-only exception: the server actually
binds the numeric IPv4 loopback address, the client rejects remote endpoints,
and both retain encryption. The client requires Basic256Sha256
SignAndEncrypt and certificate-domain checks. No credentials are embedded.

The default client is passive. To acknowledge **one result only**, specify
its exact system and result and confirm the state change:

```powershell
dotnet run --project samples\GMS\GmsClient\GmsClient.csproj -f net10.0 -- `
    --system Gms1 --acknowledge Gms1-F-0000 --confirm-acknowledgement --seconds 0 --demo-trust
```

Acknowledgement does not run a job or change a correction. Sensor warnings
are observed, never implicitly acknowledged. Their inputs are actual
additional environmental measurements, not primary-probe qualification.

## Bounds and cleanup

`--help` lists every bound. The server runs for at most 120 seconds and owns
one or two independent providers/stores, each limited to 256 results and
8 MiB. Each instance permits one metadata handle and one file transfer.
The client queries at most 16 IDs per instance, preserves raw `DataValue`
quality/timestamps, bounds every observation by time/count/bytes and releases
its subscriptions. Use `--count 32` to capture changing advanced measurements
beyond the initial inventory (the notification cap is 64). Downloads return
bounded `ByteString` values in memory; no result file is written to disk.

With an advanced local server, add
`--system Gms1 --download --verify-cleanup --demo-trust` to demonstrate a
missing-result error, file-byte-limit refusal, cancellation after a remote
file read and cancellation after monitored-item creation. A subsequent
download and metadata read verify reuse of the single-handle capacity
without waiting for expiration. Other failures return a nonzero exit code;
the client does not silently retry acknowledgement. Ctrl+C awaits cleanup.

Both projects use `AppTargetFrameworks` and are nonpackable. NativeAOT is
selected **inside each application project only**, conditionally for net10:

```powershell
dotnet publish samples\GMS\GmsServer\GmsServer.csproj -c Release -f net10.0 -r win-x64
dotnet publish samples\GMS\GmsClient\GmsClient.csproj -c Release -f net10.0 -r win-x64
```

Do not pass a graph-wide `PublishAot` property. Execute the published
applications with the same options to exercise the real transport.

On Windows, the PowerShell 7 `GmsClient\Test-GmsSamplePair.ps1` acceptance harness takes
`-ServerExecutable`, `-ClientExecutable` and `-EvidenceDirectory`.
Use `-Advanced -AllChecks` for positive, refusal and cancellation checks;
add `-AssertNative` for published NativeAOT binaries. It verifies the actual
loopback listener, natural exits, subscription/handle reuse and owned PKI
cleanup, and records exact commands, output logs and `summary.json`.
