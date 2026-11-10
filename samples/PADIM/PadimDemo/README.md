# PA-DIM demonstration

This application hosts a standalone PA-DIM transmitter, positioner, discrete
input and pH analyser, or connects as a client to discover their typed values
and IRDI references. It uses the DI address-space owner without Machinery.
See the [PA-DIM guide](../../../docs/PADIM.md) for provider ownership and APIs.

## Run the server and client

Use separate certificate stores for the server and client. Both modes select
signed and encrypted connections by default; trust the peer certificates
through those stores before connecting.

```powershell
$env:TEMP='D:\temp'
$env:TMP='D:\temp'
dotnet run --project samples\PADIM\PadimDemo -p:CustomTestTarget=net10.0 -- `
    --pki-root D:\temp\padim-server-pki --run-seconds 120
```

In another terminal:

```powershell
$env:TEMP='D:\temp'
$env:TMP='D:\temp'
dotnet run --project samples\PADIM\PadimDemo -p:CustomTestTarget=net10.0 -- `
    --client true --pki-root D:\temp\padim-client-pki
```

The default endpoint is `opc.tcp://localhost:62562/PadimDemo`; override it with
`--endpoint`. `--run-seconds` accepts an integer from 1 through 3,600. Without
it, the server runs until host shutdown.

On the client, `--observe true` prints one temperature subscription
notification and removes its monitored item. The observation has a ten-second
timeout. `--allow-demo-writes true` also sets a simulation value, enables
simulation, prints the substituted value and disables simulation in cleanup
before printing restored acquisition. It then invokes the positioner and pH
demonstration operations. For each operation it waits up to ten seconds for
the provider's completion timestamp to change, then prints the measurement
readback. The server must separately authorize these requests. These timestamps
describe completion in this sample provider; another device can expose a
different operation lifecycle through its condition and calibration members.

The simulation cleanup also attempts disable when the enable request throws or
is cancelled, because the server may already have accepted the write. Disable
uses no caller cancellation token; a cleanup error is reported to the client.

The client also prints the device's identity and health values, the pH signal's
condition fields, calibration metadata and neutral calibration point without
requiring write authorization.

`--auto-accept true` accepts untrusted peer certificates. `--insecure true`
adds an unencrypted endpoint on the server or selects one on the client.
These options print a warning and are intended only for an isolated
demonstration environment.

## Provider behavior

The provider publishes fixed typed values with source timestamps advanced by
refresh count. Temperature includes units/range, optional simulation and
provider-backed damping. The pH signal includes calibration metadata, a
calibration point and sensing-element impedance. Device-health conditions use
the existing DI alarm types.

Writes and method calls are denied unless the server is started with
`--allow-demo-writes true`. This authorizes every caller reaching the
demonstration provider and is not an application authorization policy.

The positioner accepts Start, completes after two refresh cycles and publishes
a 50-percent readback with an updated last-change timestamp. Abort cancels a
pending cycle. The pH adjustment records a neutral reading and calibration
timestamp. Neither operation performs physical calibration, and FactoryReset
is not exposed.

Configuration is held in memory and is lost when the process stops. The
application owns its providers; the client borrows the host-owned session
and stops the host instead of closing that session itself.
