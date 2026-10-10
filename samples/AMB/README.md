# OPC 10000-110 Asset Management Basics samples

Two console applications that show the `Opc.Ua.AMB*` libraries end to end: a
server that makes Device Integration devices and a Machinery machine
manageable assets, and a client that finds and reads them with `AmbClient`.

| Sample | What it does |
| --- | --- |
| [AmbServer](AmbServer/) | Serves a cooling pump, the flow sensor it relies on and a hydraulic press as assets, and simulates their health alarms and maintenance |
| [AmbClient](AmbClient/) | Discovers every asset, prints a snapshot of each, the location hierarchies, adds and removes a documentation link, and streams health alarms and maintenance activities |

The [Asset Management Basics developer guide](../../docs/AMB.md)
explains every API used here.

## Run

In two terminals:

```sh
dotnet run --project samples/AMB/AmbServer -- --port 62553
dotnet run --project samples/AMB/AmbClient -- --insecure --seconds 60
```

`--insecure` makes the client accept the server's self-signed certificate;
never use it in production. The client also takes `--url <endpoint>` (default
`opc.tcp://localhost:62553/AmbServer`), `--seconds <n>` (how long to stream,
`0` to skip) and `--asset-id <id>` (writes a new AssetId to the first asset
that has one and finds the asset by it).

The server keeps what clients write - AssetIds, edited and added
documentation links, the writable location and local time - below the local
application data folder (`OPC Foundation/AmbServer/state`), or in the folder
given with `--state <directory>`. Stop the server, start it again, and the
client reads the values it wrote before.

## What the server shows

| File | Shows |
| --- | --- |
| [`Program.cs`](AmbServer/Program.cs) | `AddMachinery()` as the owner of the Device Integration address space, `AddAssetManagement(...)` with `UseFileSystemStores`, `ConfigureAssetManagement(...)` defining the location hierarchies and the ECLASS dictionary entry, `ConfigureMachinery<T>()` and `ConfigureDevicesFor<MachineryNodeManager>(...)` |
| [`Plant.cs`](AmbServer/Plant.cs) | Two DI devices registered with `RegisterAsAssetAsync`: every building block - configurable AssetId, DeviceHealth derived from the alarms, health alarms, maintenance activities with details, documentation links (fixed, editable, user links), version information, locations and local time, classification, requirements and capabilities, and a `0:Utilizes` relation between the assets |
| [`PressConfigurator.cs`](AmbServer/PressConfigurator.cs) | A Machinery machine registered through `IMachineHandle<T>.AsNode()`; its servicing switches the machine into the `Maintenance` operation mode while it executes |
| [`PlantSimulation.cs`](AmbServer/PlantSimulation.cs) | A `BackgroundService` that raises and clears a bearing temperature alarm with the sensor as root cause once a minute, fails the self test of the sensor every five minutes (`AssetRootCauses.Self`), and moves a maintenance activity of the pump and of the press on every two minutes |

Health alarms and maintenance activities are created while the owning node
manager builds its address space, which is why `Plant` and
`PressConfigurator` register the assets from the DI and Machinery setup.

## What the client shows

| Code in [`Program.cs`](AmbClient/Program.cs) | Shows |
| --- | --- |
| `connection.Session.AssetManagement(telemetry)` | Creating the client; registers the AMB structured types |
| `amb.IsSupported` | Detecting whether the server publishes the AMB namespace |
| `DiscoverAssetsAsync` | All assets from the AMB alias categories |
| `ReadAssetAsync` → `AssetSnapshot` | Identification, DeviceHealth, alarms, maintenance, links, locations and classification in one pass |
| `BrowseLocationsAsync` | The hierarchical and operational location trees and the assets in them |
| `AddDocumentationLinkAsync`, `ReadDocumentationLinksAsync`, `RemoveDocumentationLinkAsync` | The DocumentationLinks AddIn |
| `WriteAssetIdAsync`, `FindAssetsAsync` | Configuring an AssetId and finding the asset by it |
| `ObserveHealthAlarmsAsync`, `AcknowledgeAsync` | Health alarms with severity, fault category and root causes; a cleared alarm is acknowledged |
| `ObserveMaintenanceAsync` | Maintenance activities moving through Planned, Executing and Finished |

[`SampleSession.cs`](AmbClient/SampleSession.cs) is the session setup. It
keeps the `ApplicationInstance` alive until the session closes, because the
instance owns the `CertificateManager` the session validates with, and it
creates a `ManagedSession`, whose default streaming subscription the observe
calls use.

## Troubleshooting

| Symptom | Fix |
| --- | --- |
| Client reports `BadCertificateUntrusted` | Pass `--insecure` for the demo, or trust the server certificate in the client's PKI (under the local application data folder, `OPC Foundation/AmbClient/pki`) |
| `AddLink / RemoveLink: refused: BadUserAccessDenied` | The server refuses anonymous link editing unless `AuthorizeLinkEdit` allows it; the sample server allows it |
| The stream shows nothing for a while | The alarm becomes active 20 s into every minute and maintenance moves on every two minutes; stream longer with `--seconds` |
| Port already in use | Start the server with `--port <n>` and the client with `--url opc.tcp://localhost:<n>/AmbServer` |
