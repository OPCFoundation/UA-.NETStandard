# OPC UA .NET Standard — Diagnostics (packet capture, dissection, replay)

`OPCFoundation.NetStandard.Opc.Ua.Core.Diagnostics` is an opt-in binding
that captures, dissects, and replays the wire-level traffic of the
OPC UA channels a host process opens — outbound client channels and/or
inbound channels accepted by a hosted server. It composes with the
existing `opc.tcp` transport as a transparent decorator — no application
changes are needed beyond wiring the binding through DI (`AddPcap()`) or
by hand (`PcapBindings.Install*`).

## Overview

The package contains:

- `PcapTransportChannelBinding` — `ITransportChannelFactory` decorator
  installed via `AddPcap()` that wraps every
  `TcpTransportChannel` with a capture-aware byte transport (outbound
  client channels).
- `PcapTransportListenerBinding` — `ITransportListenerFactory` decorator
  that wraps a hosted server's `opc.tcp` listener so every accepted
  inbound client→server channel is capture-aware.
- `ChannelCaptureRegistry` + `CaptureSessionManager` — runtime control
  surface to start / stop a capture, manage rolling files, and emit
  pcap + keylog artifacts.
- `ReplaySessionManager` — offline pcap dissection and frame replay
  (optional, gated by `PcapOptions.AllowMockClientReplay`).
- Audit sinks (`LoggerPcapAuditSink`, `HashChainedAuditFileSink`) for
  tamper-evident capture provenance.

## Getting started

```csharp
using Microsoft.Extensions.DependencyInjection;
using Opc.Ua;

services.AddOpcUa();
services.AddPcap(opts =>
{
    opts.BaseFolder = "/var/log/opcua-pcap";
    opts.MaxActiveSessions = 4;
});
```

### Without dependency injection

Install the binding by hand, then start a capture session sharing the
same `IChannelCaptureRegistry`. Installing a binding only makes channels
capture-*aware*; nothing is recorded until a session is running.

```csharp
using Opc.Ua.Pcap.Bindings;

// server (inbound): install before the server starts its listeners
IChannelCaptureRegistry registry =
    PcapBindings.InstallServer(server.Server!.TransportBindings);

// client (outbound): install into the process-wide client default
// PcapBindings.InstallClient(ClientChannelManager.DefaultChannelBindings, registry);
```

See the [Diagnostics guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/Docs/Diagnostics.md#enabling-pcap-capture-without-dependency-injection)
for the full non-DI recipe (server, client, and both directions).

## Target frameworks

`net8.0`, `net9.0`, `net10.0`.

## Additional documentation

See the [main repository README](https://github.com/OPCFoundation/UA-.NETStandard)
and the [Diagnostics guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/Docs/Diagnostics.md)
for the full design (capture-session lifecycle, key-escrow, audit
chaining, replay security model).
