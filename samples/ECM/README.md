# Simulated ECM server and typed client

This server publishes OPC 34100 standby and WOL objects backed by
`SimulatedEcmPowerProvider`. Every operation changes only in-memory
device state. The sample never powers down the host or sends wake packets.

Run the server from the repository root:

```powershell
dotnet run --project samples\ECM\EcmServer -f net10.0 -p:CustomTestTarget=net10.0
```

The endpoint is `opc.tcp://localhost:62549/ECM`. The server prints the
standby, nested WOL and WOL-only NodeIds after creation. Both WOL representations
use the same simulated device provider; the WOL-only object has no standby parent.
Its background service publishes new acquisitions every 100 milliseconds.
Without an explicit simulation option, it does not issue power commands.
The acquisition service starts after the OPC UA server and stops before it,
so no refresh continues after the manager releases its bindings.

Browse the standby instance to read supported modes, transition estimates
and `StandbyManagementStatus`. Write `PauseTime` to request a suitable
mode, or use `StartPause` and `SwitchToEnergySavingMode`. Write zero to
`PauseTime`, or call `EndPause`, to return to operation. A successful
method result means the provider accepted the request; observe the
moving state and subsequent arrival separately.

The sample deliberately permits anonymous control of simulated state.
Production hosts should retain the default role policy or supply their
own authorizer and optional DI exclusive access. Certificate trust remains
an explicit deployment decision; this sample does not enable automatic
certificate acceptance.

The client uses `AddEcmClient`, the generated variable accessors and a
borrowed managed session. With no control option it only reads and observes:

```powershell
dotnet run --project samples\ECM\EcmClient -f net10.0 -p:CustomTestTarget=net10.0
```

Establish mutual certificate trust before connecting. The client stores its
application certificate under `pki\EcmClient` in the current directory;
`--pki <directory>` selects another store. It requires a Basic256Sha256
SignAndEncrypt endpoint and never enables automatic certificate acceptance.

Select one explicit control option after `--`: `--pause-ms 10000` writes
the PauseTime variable, `--end-pause true` invokes EndPause, or
`--sleep true` invokes SwitchOffWOL. Use these options only against the
simulated reference server unless controlling the target device is intended.
The client prints Call status separately from ReturnCode and observes
standby updates for ten seconds. It never sends a wake packet or repeats
a failed power command.

Use `--wol-only true` to discover the WOL-only object directly under DeviceSet
and read its MAC metadata without discovering standby. Add `--sleep true`
only when an explicit sleep request is intended. Standby controls cannot be
combined with `--wol-only`.

`SwitchOffWOL` requests simulated sleep with mode ID `0xFE`.
`WOLMagicPacket` is a six-byte MAC address. The server remains reachable
in simulated sleep; application code can explicitly call the provider's
`SimulateWakeAsync` to model recovery without network traffic.

To observe sleep and recovery without changing sample code, start the server
with an explicit simulated wake delay:

```powershell
dotnet run --project samples\ECM\EcmServer -f net10.0 -p:CustomTestTarget=net10.0 -- --simulate-wake-after-ms 3000
```

Then run the client with `--sleep true`. The server waits three seconds
after it observes the arrived sleep state, calls `SimulateWakeAsync` and
publishes recovery until the provider reports ready. This delay applies to
each sleep request and starts after entry has completed, not when the
method accepts the request. The option accepts a nonnegative 32-bit
millisecond value; zero requests wake on the first sleep acquisition, while
the provider still includes any remaining minimum stay in its recovery estimate.
No wake packet is sent.

Standalone measurements and grouped statistics reset are not part of this
sample yet. See [ECM](../../docs/ECM.md) for the current integration boundary.
