# OPC UA Cranes and Hoists — client (preview)

Typed OPC-40020-1 1.00 workflows over an existing OPC UA session. This package
is preview-only and has no server-package dependency.

```csharp
using Opc.Ua;
using Opc.Ua.CranesHoists;
using Opc.Ua.CranesHoists.Client;

var cranes = new CranesHoistsClient(session, telemetry);
ArrayOf<CraneEntry> systems = await cranes.DiscoverAsync(cancellationToken);
CraneSystemSnapshot snapshot =
    await cranes.ReadSystemAsync(systems[0].NodeId, cancellationToken);
```

Discovery follows vendor subtype chains and browse continuations, uses the
session's namespace table, and fails explicitly at its configured bounds
rather than silently truncating. Snapshots retain original data quality and
source timestamps and distinguish a missing optional member from a bad value.
The native generated proxies remain available through `System(...)`,
`Axis(...)` and the model package.

`ConfigureLimitAsync` writes only standard limit-request variables. The
server's authorization and equipment provider determine acceptance. A
successful write is not evidence that a physical limit became active; read
the corresponding reported active limit separately.

`ObserveValuesAsync` and `ObserveEventsAsync` create exclusively owned,
bounded subscriptions and dispose them on completion, errors and cancellation.
Observing does not acknowledge conditions, configure equipment or close the
borrowed session. Options-based subscription support is required. Results
include count-limit and overflow information; oversized decoded payloads fail
explicitly.

Register `AddCranesHoistsClient()` on the existing OPC UA client builder to
obtain an injectable `CranesHoistsClientFactory`, or use the session extension
`session.CranesHoists(telemetry)`.
