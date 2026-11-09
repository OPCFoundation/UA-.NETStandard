# Simulated Base Network Model

`NetworkModelSample` starts a Part 22 server and connects a client over a secured
OPC TCP connection. It exposes a physical interface, a layered VLAN interface,
an inherited priority mapping, talker/listener streams and LLDP neighbors.
All network data is simulated; the sample does not change host networking or
reserve bandwidth.

Run from the repository root with the .NET 10 SDK:

```powershell
dotnet run --project samples\Network\NetworkModelSample -- 62588
```

The optional argument selects the local endpoint port. The scenario rejects a
mapping change while its local authorization switch is off, permits one after
the switch is enabled, and rejects an out-of-range mapping. It then observes
stream configuration, operation, bandwidth failure and recovery through a
subscription while a simulated LLDP neighbor appears and disappears. A
nonzero exit indicates an unexpected operation result or an incomplete
observation.

The sample automatically accepts local application certificates and stores
generated certificates beneath the process temporary directory. Its
authorization switch is a demonstration mechanism, not a production identity
policy. Configure trusted certificate stores and an authenticated-user
authorization policy before using the runtime in a deployed application.

`NetworkScenario.cs` composes acquisitions through `NetworkModelBuilder`.
Replace its `ReferenceNetworkModelProvider` with an application-owned
`INetworkModelProvider` to connect a deployment adapter; the standard address
space and typed client remain unchanged. See
[Base Network Model](../../docs/BaseNetworkModel.md) for provider ownership,
quality, timestamps and mapping-method results.
