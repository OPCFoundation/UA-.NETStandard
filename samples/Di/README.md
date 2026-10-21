# Device Integration samples

| Folder | Contents |
| --- | --- |
| [PumpDeviceIntegrationServer](PumpDeviceIntegrationServer/README.md) | A simulated pump line hosted by a Device Integration node manager, with OPC 40223 pump data, alarms, history, software-update simulation and OpenUSD assets. |

The sample's [datasheet](PumpDeviceIntegrationServer/DATASHEET.md) describes the
simulated device. See [Device Integration](../../docs/DeviceIntegration.md) for
the model and hosting APIs.

## Run

From the repository root:

```powershell
dotnet run --project samples\Di\PumpDeviceIntegrationServer\PumpDeviceIntegrationServer.csproj -f net10.0
```

See the sample README for endpoint, certificate and simulation options.
