# Industrial Automation information sample

This sample hosts stacklights, aggregate and rolling statistics, and
calibration-target information below one Machinery-owned DI device. It uses
the public IA server/client APIs and generated proxies. No physical light output
or calibration procedure occurs.

Run the bounded encrypted server/client scenario:

```powershell
dotnet run --project samples\IA\IndustrialAutomationSample.csproj -f net10.0 -- `
    --mode selfcheck --demo-trust true --permit-reset true `
    --pki-root D:\temp\ia-sample\pki
```

`--demo-trust true` explicitly accepts untrusted application certificates for
this local demonstration. `--permit-reset true` separately allows anonymous
sample resets. Both default to false. Production applications must configure
certificate trust and a requesting-user reset policy instead.

Use `--mode server` to publish each of the three modes for ten seconds, or
`--mode client` to connect to an existing server. The default endpoint is
`opc.tcp://localhost:62560/IA`; override it with `--endpoint`.
The server stops after its bounded sequence. A failed configuration, connection,
provider operation, or reset produces a nonzero exit code.

`IaSampleModel` owns the simulated observations. The aggregate sums every sample
since reset, while the rolling collection sums at most three samples.
Reset commits the complete collection and its `StartTime`. The sample has no
persistent store and therefore starts a new collection after process restart.
The stacklight provider publishes an acoustic audio sample. The calibration
provider publishes a reference value, capacity range, validation dates, quality,
a certificate URI without retrieving it, and an application-defined
`VendorGeometry` object feature. The client reads calibration metadata through
generated target accessors and discovers the custom feature through the IA
feature-inspection API.

The DI container owns both the server resources and the client's cached managed
session. The sample drains hosted services before disposing its bindings and
does not close a borrowed client session.

See the [Industrial Automation guide](../../docs/IndustrialAutomation.md) for
provider contracts, direct construction, fluent composition, and client usage.
