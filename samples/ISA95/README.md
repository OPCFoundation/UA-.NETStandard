# ISA-95 samples

| Folder | Contents |
| --- | --- |
| [MinimalIsa95Server](MinimalIsa95Server/README.md) | Hosts the ISA-95 Common Model and Job Control V1/V2, with personnel, equipment, material, geolocation and a seeded job that starts through the execution controller. |

See [ISA-95](../../docs/ISA95.md) for the server APIs and supported models.

## Run

From the repository root:

```powershell
dotnet run --project samples\ISA95\MinimalIsa95Server\MinimalIsa95Server.csproj -f net10.0
```

The sample README lists the endpoint and demonstrates the job lifecycle.
