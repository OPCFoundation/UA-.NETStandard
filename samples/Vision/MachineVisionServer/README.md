# Machine Vision server

Hosts released OPC 40100-1 control/management and OPC 40100-2 assets in the existing
Machinery manager. A deterministic engine also implements the draft Vision inference
provider, so both command paths publish the same acquisition under both result models.
The sample requires no camera, GPU, or image-processing SDK.

From the repository root:

```powershell
dotnet run --project samples\Vision\MachineVisionServer\MachineVisionServer.csproj -f net10.0 -- --insecure true
```

The default endpoint is `opc.tcp://localhost:62550/MachineVision`.
Run the [client](../MachineVisionClient) in a second terminal.

| Option | Meaning |
| --- | --- |
| `--endpoint URL` | Override the listening endpoint. |
| `--pki PATH` | Override the application's certificate-store root. |
| `--insecure true` | Explicitly auto-accept untrusted application certificates for a local demonstration. |
| `--runForSeconds N` | Run for a positive, bounded number of seconds and then stop cleanly. |

The client uses Basic256Sha256 SignAndEncrypt even in demonstration mode.
Certificate auto-accept is **not appropriate for production**; omit `--insecure`
and provision trust through the stack's certificate stores. The server does not
enable SecurityPolicy None by default.

The server publishes `InspectionControl`, `InspectionAsset`, a synthetic camera,
and a draft inspection pipeline. The asset/control pair and camera representations
are linked using RepresentsSameEntityAs. Recipe content is opaque deterministic
sample input; preparation and job starts remain explicit.

See [Machine Vision](../../../docs/MachineVision.md) for providers, storage,
optional features, protocol semantics, and the implementation map.
