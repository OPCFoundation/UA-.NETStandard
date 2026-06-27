# Redundant client sample

This console sample demonstrates the non-transparent redundant managed client in `Opc.Ua.Client`. It connects to one seed server, reads `Server.ServiceLevel` and `Server.ServerRedundancy`, resolves peer application URIs with `FindServers`, creates a redundant subscription to `Server_ServerStatus_CurrentTime`, and logs the active server selected by highest `ServiceLevel`.

Run against two or three servers that expose the same non-transparent `RedundantServerSet`:

```powershell
dotnet run --project Applications\RedundantClient\RedundantClient.csproj -- `
  --server opc.tcp://localhost:62543/RedundantServer `
  --server opc.tcp://localhost:62544/RedundantServer `
  --mode hot-b --autoaccept --nosecurity --duration 00:05:00
```

Modes:

- `cold`: connect initially to one server; on failover, connect the selected standby and create the subscription there.
- `warm`: keep all resolved peer sessions connected; only the active server publishes while standbys sample.
- `hot-a`: hot redundancy with reporting handoff; all peers host the subscription, but only the active server publishes notifications.
- `hot-b`: hot redundancy with reporting merge; all peers publish and `RedundantManagedClient` suppresses duplicate notifications.

The server reports the actual OPC UA `RedundancySupport` mode. The `--mode` option selects the client-side hot notification behavior and documents the expected server topology; the sample warns if the server reports a different mode. To observe failover, lower the active server's service level (for example by using the HA sample's manual failover support) or stop the active server. The client periodically refreshes service levels and calls `FailoverAsync`, then logs the newly active session.
