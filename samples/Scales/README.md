# OPC 40200 Scales samples

Two console applications show the `Opc.Ua.Scales*` libraries end to end:

- a server that publishes weighing equipment with the fluent builders;
- a client that discovers the scales, drives them and streams their weights
  and notifications with `ScalesClient`.

| Sample | What it does |
| --- | --- |
| [ScalesServer](ScalesServer/) | Serves a packing line and four stand-alone scales, and simulates them twice a second |
| [ScalesClient](ScalesClient/) | Discovers every scale, prints each one, drives the checkweigher, then streams its weights, events and alarms |

The [Scales developer guide](../../docs/Scales.md) explains every API used here.

## Run

In two terminals:

```sh
dotnet run --project samples/Scales/ScalesServer -- --port 62548
dotnet run --project samples/Scales/ScalesClient -- --insecure --seconds 20
```

Client options:

- `--insecure` makes the client accept the server's self-signed certificate.
  Never use it in production.
- `--url <endpoint>` sets the server endpoint. The default is
  `opc.tcp://localhost:62548/ScalesServer`.
- `--seconds <n>` sets how long to stream. `0` skips streaming.

The client prints something like:

```text
Scale system PackingLine1 (Contoso Weighing PL-0001), PackML Idle

Checkweigher1 - Checkweigher scale
  Contoso Weighing CW-600, serial CW-0100
  range 0 .. 0.6, d = 0.0001, e = 0.0002
  current    gross 0.5057 kg, net 0.5057, tare 0 (None_0)
  registered gross 0.5089 kg, net 0.5089, tare 0 (None_0)
  products   CEREAL-500 (Cereal 500 g), CEREAL-375 (Cereal 375 g); in processing: CEREAL-500
  PackML     Idle

AnalyticalBalance - Laboratory scale
  Contoso Lab XP-220, serial AB-220
  range 0 .. 0.22, d = 1E-07, e = 1E-06
  current    gross 0.1 kg, net 0.1, tare 0 (None_0)
  registered none yet
…

Driving Checkweigher1:
  SetTare: done
  ClearTare: done
  registered gross 0.4968 kg, net 0.4968, tare 0 (None_0)
  RegisterWeight: done
  SwitchProduct CEREAL-375: done
  current products: CEREAL-375

Streaming weights and notifications of Checkweigher1 for 20 s ...
  weight gross 0.5062, net 0.5062, tare 0 (None_0)
  ALARM 401 Component: The labeler is out of labels.
  event 103 Process: Package of 0.4846 kg rejected.
  cleared 401 Component: Labels restocked.
  …
```

NodeIds and namespace indices are whatever the server assigned. Only the
browse names are stable.

## What the server shows

| File | Shows |
| --- | --- |
| [`Program.cs`](ScalesServer/Program.cs) | `AddScales()` and `ConfigureScales(...)` on the generic host |
| [`PackingLine.cs`](ScalesServer/PackingLine.cs) | The equipment, listed below |
| [`ScaleSimulation.cs`](ScalesServer/ScaleSimulation.cs) | A `BackgroundService` publishing loads through the scale handles, listed below |

**The equipment in `PackingLine.cs`:**

- **Scale system.** `CreateScaleSystemAsync` builds a system with PackML and
  production output.
- **Checkweigher.** It has products that are locked, selectable and
  manageable, plus an infeed feeder with a speed range and a labeler.
- **Laboratory balance.** It is legal for trade and accepts kg, g and mg.
- **Counting scale.** A piece-counting scale with a selected product.
- **Weighbridge.** A vehicle scale whose `VehicleInformationProvider`
  answers `GetVehicleInformation`.
- **Batching scale.** A recipe scale with a two-branch `BREAD` recipe and a
  `RecipeFileHandler` for uploaded recipe files.

**What the simulation does:**

- Rejects about one package in ten below T1, with a `BAD_PACK` event and a
  rejected count.
- Runs the labeler out of labels every 20 s, raising and clearing a
  `PRINTER_FAULT` alarm.
- Drives the laboratory balance, the counting scale and a truck crossing the
  weighbridge.

**Runtime updates.** The simulation calls `PublishLoad` and the controller
methods on the handles it kept at configuration time. Every call publishes
its changes, so subscribed clients see each update without further code.

## What the client shows

| Code in [`Program.cs`](ScalesClient/Program.cs) | Shows |
| --- | --- |
| `connection.Session.Scales(telemetry)` | Creating the client; registers the OPC 40200 structured types |
| `scales.IsSupported` | Detecting whether the server publishes OPC 40200 |
| `DiscoverScalesAsync`, `EnumerateSystemScalesAsync` | Discovery from the DI `DeviceSet` and Machinery `Machines`, then the scales of a system |
| `ReadSnapshotAsync` → `ScaleSnapshot` | A whole scale in one pass |
| `ReadPackMLStateAsync` | The innermost active PackML state |
| `SetTareAsync`, `ClearTareAsync`, `RegisterWeightAsync`, `SwitchProductAsync` | Driving a scale that sits inside a scale system |
| `ObserveWeightAsync`, `ObserveNotificationsAsync` | Streaming weights, events and alarms of that nested scale |

[`SampleSession.cs`](ScalesClient/SampleSession.cs) sets up the session. It
keeps the `ApplicationInstance` alive until the session closes, because the
instance owns the `CertificateManager` the session validates with.

## Publish as NativeAOT

Both samples are NativeAOT-compatible on `net10.0`:

```sh
dotnet publish samples/Scales/ScalesServer -c Release -f net10.0 -r osx-arm64
```

## Troubleshooting

| Symptom | Fix |
| --- | --- |
| Client reports `BadCertificateUntrusted` | Pass `--insecure` for the demo, or trust the server certificate in the client's PKI |
| `The server publishes no scales.` | Wait for the server log line `Materialised scale 'BatchingScale'` before starting the client |
| Port already in use | Another server (often an earlier sample run) holds it. Stop it, or start the server with `--port <n>` and the client with `--url opc.tcp://localhost:<n>/ScalesServer` |
| No events or alarms while streaming | Stream for at least 20 s. Rejects are random and the labeler runs out every 20 s |
