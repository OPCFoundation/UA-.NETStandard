# CranesHoistsServer

A bounded, explicitly simulated standalone server using `AddCranesHoists` and
`ConfigureCranesHoists`. See the [paired walkthrough](../README.md).

```powershell
dotnet run --project samples\CranesHoists\CranesHoistsServer --framework net10.0 -- --help
```

| Flag | Default | Meaning |
| --- | --- | --- |
| `--port <0..65535>` | `62557` | Local endpoint port; `0` chooses a free port before startup. |
| `--seconds <1..600>` | `60` | Acquisition window after startup. |
| `--instances <1..4>` | `1` | Independent `Crane1` through `Crane4` systems/providers. |
| `--pki <directory>` | `artifacts\craneshoists-pki\server` | Private certificate-store root. |
| `--advanced` | Off | Explicit optional monitoring, brakes, protections, events and limit requests. |
| `--demo-trust` | Off | Accept unknown peer certificates for local testing only. |
| `--allow-limit-requests` | Off | Authorize anonymous demo callers; requires both preceding flags. |
| `--help`, `-h` | Off | Print help and exit without allocating resources. |

The mandatory graph contains a generated `CraneMotionDeviceSystemState`,
`CraneMotionDeviceState`, `CraneAxisState` and `CraneMotorState`, plus the inherited
controller/software/task-control/safety obligations and Machinery nameplates.
Optional capabilities are selected only in the `--advanced` branch of
[`CraneDemo.Define`](CraneDemo.cs).

[`CraneAcquisition`](CraneAcquisition.cs) supplies explicit, complete inputs to
the existing `SimulatedCraneProvider`. Motor temperature varies and deliberately
has `UncertainDataSubNormal` quality. Every acquisition retains its
`TimeProvider` source time. An unavailable default position is `BadNoData`.
No accepted request is used to manufacture an active measurement.

Advanced mode adds working-period monitoring, actual position/function/moving
state/load/rated speed, service and backup brakes, an overload protection with a
native forward `ProtectsAxis` relationship, external-control state, functional
groups, Components and standard `SystemEventType` acquisition diagnostics.
Protection state is monitoring only, not a safety control.

The injected [`DemoWriteAuthorizer`](DemoWriteAuthorizer.cs) checks the actual
session identity and denies requests unless the operator explicitly opts into
the local anonymous policy. This is **not a production authorization policy**.
Each bounded provider accepts at most one request, then returns
`BadResourceUnavailable`. Server limits enforce the standard type/range rules
before acceptance. Requested values never change the supplied active limits
(`27.5` minus / `31.5` plus), brakes or external-control state.

`READY` waits for the existing `IServerStartupTask` post-start callback, not just
Generic Host startup or an open TCP socket. `READY`, `INSTANCE`, `REQUESTS` and
`STOPPED` identify the actual endpoint,
selected instance/CU state, per-provider accepted counts and completed cleanup.
Shutdown awaits the acquisition loop and host stop before disposing borrowed
providers, so an aborted startup is also drained. Initial startup is bounded to
30 seconds; host stop has a 20-second
budget. No detached task or hardware executor is started.

Only encrypted policies are enabled. A localhost URL is not a firewall rule:
keep local demo trust/write flags off on exposed machines. See
[CranesHoists](../../../docs/CranesHoists.md) for shared-owner composition,
production provider contracts and the exact 11-CU implementation/test mapping.
