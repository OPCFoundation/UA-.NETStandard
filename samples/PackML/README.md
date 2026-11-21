# PackML sample

`PackMLSample` hosts a standalone OPC 30050 unit and drives it with the typed
client over a real OPC UA session. Produce and Maintenance use the complete
state graph; Manual has a restricted subset. The provider simulates equipment
and never operates physical control outputs.

Run the sample with an explicit certificate-store directory:

```powershell
dotnet run --project samples\PackML\PackMLSample -f net10.0 `
    -p:CustomTestTarget=net10.0 -- --pki-root D:\temp\packml-sample-pki
```

The default endpoint is `opc.tcp://127.0.0.1:62563/PackML`. Change it with
`--endpoint`; only loopback endpoints are accepted. Accepted loopback DNS names,
including `localhost`, are converted to `127.0.0.1` before registering the
shared server/client endpoint, so the transport binds a numeric loopback
address rather than a wildcard listener. Server and client stores
are separate subdirectories of `--pki-root`. The sample automatically accepts
untrusted application certificates for this local simulation; production
applications must configure explicit trust and user authorization.

The sequence submits products, parameters and a speed setpoint, confirms mode
changes from equipment snapshots, and delays completion of Reset, Start and
Stop. The output shows each accepted request identity while the machine
remains in its acting state. It also checks that a mode change while Execute
is rejected, projects material readiness and production counts, then shuts
down the host. A failing command terminates the sample with an error.

The dependency container owns the managed session and equipment provider.
The [PackML guide](../../docs/PackML.md) explains the provider contract,
state-machine completion, administration and composition under a chosen parent.
